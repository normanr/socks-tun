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
		}

		public void Run(string[] args)
		{
			OnStart(args);
			Console.WriteLine("Press enter to exit");
			Console.ReadLine();
			OnStop();
		}

		private const int debug = 1;
		private const int bufferSize = 10000;

		protected override void OnStart(string[] args)
		{
			server = new TcpListener(IPAddress.Any, 59000);
			server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
			server.Start();
			serverPort = ((IPEndPoint)server.LocalEndpoint).Port;
			Console.WriteLine("server.Port = " + serverPort);
			server.BeginAcceptSocket(NewSocket, null);

			var tunTapDevice = new TunTapDevice(null);
			Console.WriteLine("tunTapDevice.HumanName = " + tunTapDevice.HumanName);

			var mtu = 0;
			var result = tunTapDevice.GetMtu(ref mtu);
			Console.WriteLine("tunTapDevice.GetMtu = " + result + ":" + mtu);

			var localIP = IPAddress.Parse("10.3.0.1");
			var remoteNetwork = IPAddress.Parse("0.0.0.0");
			var remoteNetmask = IPAddress.Parse("0.0.0.0");
			result = tunTapDevice.ConfigTun(localIP, remoteNetwork, remoteNetmask);
			Console.WriteLine("tunTapDevice.ConfigTun = " + result);
			if (!result) return;

			var adapterNetmask = IPAddress.Parse("255.255.255.0");
			var dhcpServerAddr = IPAddress.Parse("10.3.0.255");
			var dhcpLeaseTime = 86400 * 365; // one year
			result = tunTapDevice.ConfigDhcpMasq(localIP, adapterNetmask, dhcpServerAddr, dhcpLeaseTime);
			Console.WriteLine("tunTapDevice.ConfigDhcpMasq = " + result);
			if (!result) return;

			result = tunTapDevice.SetMediaStatus(true);
			Console.WriteLine("tunTapDevice.SetMediaStatus = " + result);
			if (!result) return;

			tap = new FileStream(tunTapDevice.Handle, FileAccess.ReadWrite, bufferSize, true);

			backgroundWorker1.RunWorkerAsync();
		}

		protected override void OnStop()
		{
			if (backgroundWorker1 == null || !backgroundWorker1.IsBusy) return;
			backgroundWorker1.CancelAsync();
			tap.Close();
			stopEvent.WaitOne();
		}

		private TcpListener server;
		private int serverPort;
		private FileStream tap;

		private readonly byte[] buf = new byte[bufferSize];
		private readonly Dictionary<KeyValuePair<IPAddress, int>, int> mappings = new Dictionary<KeyValuePair<IPAddress, int>, int>();
		private readonly Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>> mappingCleanUp = new Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>>();
		private readonly EventWaitHandle stopEvent = new ManualResetEvent(false);

		private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
		{
			while (!backgroundWorker1.CancellationPending)
			{
				while (mappingCleanUp.Count > 0 && mappingCleanUp.Peek().Key < DateTime.Now)
				{
					var key = mappingCleanUp.Dequeue().Value;
					if (mappings.ContainsKey(key))
						mappings.Remove(key);
				}

				var bytesRead = tap.Read(buf, 0, bufferSize);
				if (debug > 0) WriteBuffer(">", buf, bytesRead);

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

							if (sourcePort != serverPort)
							{
								var key = new KeyValuePair<IPAddress, int>(destination, sourcePort);
								if (!mappings.ContainsKey(key))
									mappings[key] = destinationPort;
								destinationPort = serverPort;
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

				if (debug > 0) WriteBuffer("<", buf, bytesRead);
				tap.Write(buf, 0, bytesRead);
			}
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

		private void NewSocket(IAsyncResult ar)
		{
			var client = server.EndAcceptSocket(ar);
			server.BeginAcceptSocket(NewSocket, null);

			var localEndPoint = (IPEndPoint)client.LocalEndPoint;
			var remoteEndPoint = (IPEndPoint)client.RemoteEndPoint;
			var key = new KeyValuePair<IPAddress, int>(remoteEndPoint.Address, remoteEndPoint.Port);

			if (mappings.ContainsKey(key))
			{
				var remotePort = mappings[key];
				var requestedEndPoint = new IPEndPoint(remoteEndPoint.Address, remotePort);
				Console.WriteLine("[{0}:{1}] Request connection to {2}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint);

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
					Console.WriteLine("[{0}:{1}] Closing connection to {2}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint);
				}
				catch (Exception ex)
				{
					client.Send(Encoding.ASCII.GetBytes(ex.ToString()));
					Console.WriteLine("[{0}:{1}] Failed connection to {2}: {3}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint, ex);
				}

				mappingCleanUp.Enqueue(new KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>(DateTime.Now.AddSeconds(30), key));
			}
			else
			{
				Console.WriteLine("[{0}] No mapping", remoteEndPoint);
				client.Send(Encoding.ASCII.GetBytes("No mapping\r\n"));
			}

			client.Close();
		}

		public void SetArray<T>(T[] array, int offset, T[] data)
		{
			Array.Copy(data, 0, array, offset, data.Length);
		}

		private static void WriteBuffer(string prefix, byte[] buf, int bytesRead)
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
						Console.WriteLine("{0}: {1}: {2}:{4} -> {3}:{5}", DateTime.Now, protocol, source, destination, sourcePort, destinationPort);
						break;
					default:
						Console.WriteLine("{0}: {1}: {2} -> {3}", DateTime.Now, protocol, source, destination);
						break;
				}
			}

			if (debug < 2) return;
			for (var i = 0; i < bytesRead; i += 0x10)
			{
				Console.Write(prefix + " " + i.ToString("x8") + " ");
				for (var j = i; j < bytesRead && j < i + 0x10; j++)
				{
					if ((j & 0xf) == 0x8) Console.Write(" ");
					Console.Write(buf[j].ToString("x2") + " ");
				}
				Console.WriteLine();
			}
			Console.WriteLine();
		}

		private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			stopEvent.Set();
		}
	}
}
