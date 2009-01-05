using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Org.Mentalis.Network.ProxySocket;

namespace SocksTun
{
	class TransparentSocksConnection
	{
		private readonly Socket client;
		private readonly DebugWriter debug;
		private readonly ConnectionTracker connectionTracker;
		private readonly ConfigureProxySocket configureProxySocket;

		public delegate void ConfigureProxySocket(ProxySocket proxySocket, IPEndPoint requestedEndPoint);

		public TransparentSocksConnection(Socket client, DebugWriter debug, ConnectionTracker connectionTracker, ConfigureProxySocket configureProxySocket)
		{
			this.client = client;
			this.debug = debug;
			this.connectionTracker = connectionTracker;
			this.configureProxySocket = configureProxySocket;
		}

		public void Process()
		{
			var localEndPoint = (IPEndPoint)client.LocalEndPoint;
			var remoteEndPoint = (IPEndPoint)client.RemoteEndPoint;
			var key = new KeyValuePair<IPAddress, int>(remoteEndPoint.Address, remoteEndPoint.Port);

			if (connectionTracker.mappings.ContainsKey(key))
			{
				var remotePort = connectionTracker.mappings[key];
				var requestedEndPoint = new IPEndPoint(remoteEndPoint.Address, remotePort);

				try
				{
					var proxy = new ProxySocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					configureProxySocket(proxy, requestedEndPoint);

					debug.Log(1, "{0}:{1} requested connection to {2} via {3}", localEndPoint.Address, remoteEndPoint.Port, requestedEndPoint, proxy.ProxyEndPoint);

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

				connectionTracker.QueueForCleanUp(key);
			}
			else
			{
				debug.Log(1, "{0} has no mapping", remoteEndPoint);
				client.Send(Encoding.ASCII.GetBytes("No mapping\r\n"));
			}

			client.Close();
		}
	}
}
