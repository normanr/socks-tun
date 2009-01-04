using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Org.Mentalis.Network.ProxySocket;

namespace SocksTun
{
	public partial class SocksTunService : ServiceBase
	{
		public SocksTunService()
		{
			InitializeComponent();
			debug.LogLevel = 2;
		}

		public void Run(string[] args)
		{
			debug.Writer = Console.Out;
			OnStart(args);
			debug.Log(-1, "SocksTun running in foreground mode, press enter to exit");
			Console.ReadLine();
			OnStop();
		}

		private const int bufferSize = 10000;

		private readonly DebugWriter debug = new DebugWriter();
		private readonly byte[] buf = new byte[bufferSize];
		private readonly Dictionary<KeyValuePair<IPAddress, int>, int> mappings = new Dictionary<KeyValuePair<IPAddress, int>, int>();
		private readonly Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>> mappingCleanUp = new Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>>();
		private readonly EventWaitHandle stopEvent = new ManualResetEvent(false);

		private TunTapDevice tunTapDevice;
		private FileStream tap;
		private TcpListener transparentSocksServer;
		private int transparentSocksPort;
		private TcpListener logServer;

		protected override void OnStart(string[] args)
		{
			transparentSocksServer = new TcpListener(IPAddress.Any, 59000);
			transparentSocksServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
			transparentSocksServer.Start();
			transparentSocksPort = ((IPEndPoint)transparentSocksServer.LocalEndpoint).Port;
			debug.Log(0, "TransparentSocksPort = " + transparentSocksPort);
			transparentSocksServer.BeginAcceptSocket(NewTransparentSocksConnection, null);

			if (debug.Writer == null)
			{
				logServer = new TcpListener(IPAddress.Loopback, 58000);
				logServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
				logServer.Start();
				debug.Log(0, "LogPort = " + ((IPEndPoint) logServer.LocalEndpoint).Port);
				logServer.BeginAcceptTcpClient(NewLogConnection, null);
			}

			tunTapDevice = new TunTapDevice(null);
			debug.Log(0, "Name = " + tunTapDevice.Name);
			debug.Log(0, "Guid = " + tunTapDevice.Guid.ToString("B"));
			debug.Log(0, "Mac = " + tunTapDevice.GetMac());
			debug.Log(0, "Version = " + tunTapDevice.GetVersion());
			debug.Log(0, "Mtu = " + tunTapDevice.GetMtu());

			var localIP = IPAddress.Parse("10.3.0.1");
			var remoteNetwork = IPAddress.Parse("0.0.0.0");
			var remoteNetmask = IPAddress.Parse("0.0.0.0");
			tunTapDevice.ConfigTun(localIP, remoteNetwork, remoteNetmask);

			var adapterNetmask = IPAddress.Parse("255.255.255.0");
			var dhcpServerAddr = IPAddress.Parse("10.3.0.255");
			var dhcpLeaseTime = 86400 * 365; // one year
			tunTapDevice.ConfigDhcpMasq(localIP, adapterNetmask, dhcpServerAddr, dhcpLeaseTime);

			tunTapDevice.SetMediaStatus(true);

			tap = tunTapDevice.Stream;
			mappingCleanupTimer.Enabled = true;
			backgroundWorker1.RunWorkerAsync();
		}

		protected override void OnStop()
		{
			mappingCleanupTimer.Enabled = false;
			if (backgroundWorker1 == null || !backgroundWorker1.IsBusy) return;
			backgroundWorker1.CancelAsync();
			tap.Close();
			stopEvent.WaitOne();
		}

		private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
		{
			while (!backgroundWorker1.CancellationPending)
			{
				var bytesRead = tap.Read(buf, 0, bufferSize);
				WriteBuffer(">", buf, bytesRead);

				var packetOffset = 0;

				var version = buf[packetOffset] >> 4;
				if (version != 0x4) continue; // IPv4

				var checkSumOffset = packetOffset + 10;
				var sourceOffset = packetOffset + 12;
				var destinationOffset = packetOffset + 16;

				var headerLength = (buf[packetOffset] & 0xf) * 4;
				var totalLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buf, 2)) & 0xffff;
				var protocol = (ProtocolType)buf[packetOffset + 9];
				var source = new IPAddress(BitConverter.ToInt32(buf, sourceOffset) & 0xffffffff);
				var destination = new IPAddress(BitConverter.ToInt32(buf, destinationOffset) & 0xffffffff);

				switch (protocol)
				{
					case ProtocolType.Tcp:
						{
							var sourcePortOffset = headerLength + 0;
							var destinationPortOffset = headerLength + 2;
							var tcpCheckSumOffset = headerLength + 16;

							var sourcePort = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buf, sourcePortOffset)) & 0xffff;
							var destinationPort = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buf, destinationPortOffset)) & 0xffff;

							if (sourcePort != transparentSocksPort)
							{
								var key = new KeyValuePair<IPAddress, int>(destination, sourcePort);
								if (!mappings.ContainsKey(key))
									mappings[key] = destinationPort;
								destinationPort = transparentSocksPort;
							}
							else
							{
								var key = new KeyValuePair<IPAddress, int>(destination, destinationPort);
								if (mappings.ContainsKey(key))
									sourcePort = mappings[key];
							}
							SetArray(buf, sourcePortOffset, BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(sourcePort))));
							SetArray(buf, destinationPortOffset, BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(destinationPort))));

							// Fix TCP checksum
							SetArray(buf, tcpCheckSumOffset, BitConverter.GetBytes((ushort)0));
							var tcpLength = totalLength - headerLength;
							var pseudoPacket = new byte[12 + tcpLength];
							Array.Copy(buf, sourceOffset, pseudoPacket, 0, 8);
							pseudoPacket[9] = (byte)protocol;
							SetArray(pseudoPacket, 10, BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)(tcpLength))));
							Array.Copy(buf, headerLength, pseudoPacket, 12, tcpLength);
							SetArray(buf, tcpCheckSumOffset, BitConverter.GetBytes(generateIPChecksum(pseudoPacket, pseudoPacket.Length)));
						}
						break;
				}

				//
				// Reverse IPv4 addresses and send back to tun
				//
				SetArray(buf, sourceOffset, destination.GetAddressBytes());
				SetArray(buf, destinationOffset, source.GetAddressBytes());

				// Fix IP checksum
				SetArray(buf, checkSumOffset, BitConverter.GetBytes((ushort)0));
				SetArray(buf, checkSumOffset, BitConverter.GetBytes(generateIPChecksum(buf, headerLength)));

				WriteBuffer("<", buf, bytesRead);
				tap.Write(buf, 0, bytesRead);
			}
		}

		private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			stopEvent.Set();
		}

		private static ushort generateIPChecksum(byte[] arBytes, int nSize)
		{
			//generate an IP checksum based on a given data buffer
			ulong chksum = 0;

			var i = 0;

			while (nSize > 1)
			{
				chksum += (ulong)((((ushort)arBytes[i + 1]) << 8) + (ushort)arBytes[i]);
				nSize--;
				nSize--;
				i++;
				i++;
			}

			if (nSize > 0)
			{
				chksum += arBytes[i];
			}

			chksum = (chksum >> 16) + (chksum & 0xffff);
			chksum += (chksum >> 16);

			return (ushort)(~chksum);
		}

		public void SetArray<T>(T[] array, int offset, T[] data)
		{
			Array.Copy(data, 0, array, offset, data.Length);
		}

		private void WriteBuffer(string prefix, byte[] buf, int bytesRead)
		{
			var packetOffset = 0;
			var version = buf[packetOffset] >> 4;
			if (version == 0x4)
			{
				var sourceOffset = packetOffset + 12;
				var destinationOffset = packetOffset + 16;

				var headerLength = (buf[packetOffset] & 0xf) * 4;
				var protocol = (ProtocolType)buf[packetOffset + 9];
				var source = new IPAddress(BitConverter.ToInt32(buf, sourceOffset) & 0xffffffff);
				var destination = new IPAddress(BitConverter.ToInt32(buf, destinationOffset) & 0xffffffff);

				switch (protocol)
				{
					case ProtocolType.Tcp:
					case ProtocolType.Udp:
						var sourcePort = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buf, headerLength + 0)) & 0xffff;
						var destinationPort = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buf, headerLength + 2)) & 0xffff;
						debug.Log(2, "{0}: {1}:{3} -> {2}:{4}", protocol, source, destination, sourcePort, destinationPort);
						break;
					default:
						debug.Log(2, "{0}: {1} -> {2}", protocol, source, destination);
						break;
				}
			}

			var sb = new StringBuilder();
			for (var i = 0; i < bytesRead; i += 0x10)
			{
				sb.Append(prefix + " " + i.ToString("x8") + "  ");
				for (var j = i; j < bytesRead && j < i + 0x10; j++)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					sb.Append(buf[j].ToString("x2") + " ");
				}
				for (var j = i + 0xf; j >= bytesRead; j--)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					sb.Append("   ");
				}
				sb.Append(" ");
				for (var j = i; j < bytesRead && j < i + 0x10; j++)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					var b = (char)buf[j];
					if (b < 0x20) b = '.';
					sb.Append(b);
				}
				sb.AppendLine();
			}
			debug.Log(3, sb.ToString());
		}

		private void NewTransparentSocksConnection(IAsyncResult ar)
		{
			var client = transparentSocksServer.EndAcceptSocket(ar);
			transparentSocksServer.BeginAcceptSocket(NewTransparentSocksConnection, null);

			var localEndPoint = (IPEndPoint)client.LocalEndPoint;
			var remoteEndPoint = (IPEndPoint)client.RemoteEndPoint;
			var key = new KeyValuePair<IPAddress, int>(remoteEndPoint.Address, remoteEndPoint.Port);

			if (mappings.ContainsKey(key))
			{
				var remotePort = mappings[key];
				var requestedEndPoint = new IPEndPoint(remoteEndPoint.Address, remotePort);
				debug.Log(1, "{0}:{1} requested connection to {2}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint);

				try
				{
					var proxy = new ProxySocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
					{
						ProxyEndPoint = new IPEndPoint(IPAddress.Loopback, 1080),
						ProxyType = ProxyTypes.Socks4
					};
					proxy.Connect(requestedEndPoint);

					SocketPump.Pump(client, proxy);

					proxy.Close();
					debug.Log(1, "{0}:{1} closing connection to {2}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint);
				}
				catch (Exception ex)
				{
					client.Send(Encoding.ASCII.GetBytes(ex.ToString()));
					debug.Log(1, "{0}:{1} failed connection to {2}: {3}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint, ex);
				}

				lock (mappingCleanUp)
					mappingCleanUp.Enqueue(new KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>(DateTime.Now.AddSeconds(30), key));
			}
			else
			{
				debug.Log(1, "{1} No mapping", remoteEndPoint);
				client.Send(Encoding.ASCII.GetBytes("No mapping\r\n"));
			}

			client.Close();
		}

		private void NewLogConnection(IAsyncResult ar)
		{
			var client = logServer.EndAcceptTcpClient(ar);

			var stream = client.GetStream();
			var writer = new StreamWriter(stream);

			var connected = true;

			var buffer = new byte[0x1000];
			AsyncCallback reader = null;
			reader = delegate(IAsyncResult rar) {
				try
				{
					var count = stream.EndRead(rar);
					if (count > 0)
						stream.BeginRead(buffer, 0, 0x1000, reader, null);
					else
						connected = false;
				}
				catch (SystemException)
				{
					connected = false;
				}
			};
			stream.BeginRead(buffer, 0, 0x1000, reader, null);

			try
			{
				writer.Write(debug.Status);
				debug.Log(1, tunTapDevice.GetInfo());
				writer.Flush();
				while (connected && client.Connected)
				{
					string line;
					if (!debug.Queue.TryDequeue(1000, out line)) continue;
					writer.WriteLine(line);
					writer.Flush();
				}
			}
			catch (SystemException)
			{
			}

			writer.Close();
			logServer.BeginAcceptTcpClient(NewLogConnection, null);
		}

		private void mappingCleanupTimer_Tick(object sender, EventArgs e)
		{
			lock (mappingCleanUp)
				while (mappingCleanUp.Count > 0 && mappingCleanUp.Peek().Key < DateTime.Now)
				{
					var key = mappingCleanUp.Dequeue().Value;
					if (mappings.ContainsKey(key))
						mappings.Remove(key);
				}
		}
	}
}
