using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SocksTun.Properties;

namespace SocksTun.Services
{
	class Natter : IService
	{
		private const int bufferSize = 10000;

		private readonly TunTapDevice tunTapDevice;
		private readonly FileStream tap;
		private readonly DebugWriter debug;
		private readonly ConnectionTracker connectionTracker;
		private readonly TransparentSocksServer transparentSocksServer;
		private readonly byte[] buf = new byte[bufferSize];
		private readonly EventWaitHandle stoppedEvent = new ManualResetEvent(false);

		private bool running;

		public Natter(DebugWriter debug, IDictionary<string, IService> services)
		{
			this.debug = debug;
			connectionTracker = (ConnectionTracker)services["connectionTracker"];
			transparentSocksServer = (TransparentSocksServer)services["transparentSocksServer"];

			tunTapDevice = new TunTapDevice(Settings.Default.TunTapDevice);
			tap = tunTapDevice.Stream;
		}

		public void Start()
		{
			debug.Log(0, "Name = " + tunTapDevice.Name);
			debug.Log(0, "Guid = " + tunTapDevice.Guid.ToString("B"));
			debug.Log(0, "Mac = " + tunTapDevice.GetMac());
			debug.Log(0, "Version = " + tunTapDevice.GetVersion());
			debug.Log(0, "Mtu = " + tunTapDevice.GetMtu());

			var localIP = IPAddress.Parse(Settings.Default.IPAddress);
			var remoteNetwork = IPAddress.Parse(Settings.Default.RemoteNetwork);
			var remoteNetmask = IPAddress.Parse(Settings.Default.RemoteNetmask);
			tunTapDevice.ConfigTun(localIP, remoteNetwork, remoteNetmask);

			var adapterNetmask = IPAddress.Parse(Settings.Default.DHCPNetmask);
			var dhcpServerAddr = IPAddress.Parse(Settings.Default.DHCPServer);
			var dhcpLeaseTime = Settings.Default.DHCPLeaseTime;
			tunTapDevice.ConfigDhcpMasq(localIP, adapterNetmask, dhcpServerAddr, dhcpLeaseTime);

			tunTapDevice.ConfigDhcpSetOptions(
				new DhcpOption.Routers(
					dhcpServerAddr
				),
				new DhcpOption.VendorOptions(
					new DhcpVendorOption.NetBIOSOverTCP(2)
				)
			);

			tunTapDevice.SetMediaStatus(true);

			BeginRun(NatterStopped, null);
		}

		private void NatterStopped(IAsyncResult ar)
		{
			EndRun(ar);
			stoppedEvent.Set();
		}

		public void Stop()
		{
			running = false;
			if (backgroundWorker == null || !backgroundWorker.IsBusy) return;
			backgroundWorker.CancelAsync();
			tap.Close();
			stoppedEvent.WaitOne();
		}

		public string GetStatus()
		{
			return tunTapDevice.GetInfo();
		}

		public void Run()
		{
			running = true;
			while (running)
			{
				var bytesRead = tap.Read(buf, 0, bufferSize);
				debug.LogBuffer(">", buf, bytesRead);

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

							// TODO: Make this configurable
							if (sourcePort != transparentSocksServer.Port)
							{
								var endPoint = new IPEndPoint(destination, sourcePort);
								if (!connectionTracker.mappings.ContainsKey(endPoint))
									connectionTracker.mappings[endPoint] = destinationPort;
								destinationPort = transparentSocksServer.Port;
							}
							else
							{
								var endPoint = new IPEndPoint(destination, destinationPort);
								if (connectionTracker.mappings.ContainsKey(endPoint))
									sourcePort = connectionTracker.mappings[endPoint];
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

				debug.LogBuffer("<", buf, bytesRead);
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

		private static void SetArray<T>(T[] array, int offset, T[] data)
		{
			Array.Copy(data, 0, array, offset, data.Length);
		}

		private System.ComponentModel.BackgroundWorker backgroundWorker;
		private AsyncCallback asyncCallback;
		private NatterAsyncResult asyncResult;

		public IAsyncResult BeginRun(AsyncCallback callback, object state)
		{
			backgroundWorker = new System.ComponentModel.BackgroundWorker {WorkerSupportsCancellation = true};
			backgroundWorker.DoWork += backgroundWorker_DoWork;
			backgroundWorker.RunWorkerCompleted += backgroundWorker_RunWorkerCompleted;
			backgroundWorker.RunWorkerAsync();

			asyncCallback = callback;
			asyncResult = new NatterAsyncResult(state);
			return asyncResult;
		}

		public void EndRun(IAsyncResult ar)
		{
		}

		private void backgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			Run();
		}

		private void backgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			asyncResult.IsCompleted = true;
			asyncCallback(asyncResult);
		}

		class NatterAsyncResult : IAsyncResult
		{
			public NatterAsyncResult(object state)
			{
				AsyncState = state;
				AsyncWaitHandle = new ManualResetEvent(false);
			}

			#region Implementation of IAsyncResult

			public bool IsCompleted
			{
				get; set;
			}

			public WaitHandle AsyncWaitHandle
			{
				get; private set;
			}

			public object AsyncState
			{
				get; private set;
			}

			public bool CompletedSynchronously
			{
				get; set;
			}

			#endregion
		}
	}
}
