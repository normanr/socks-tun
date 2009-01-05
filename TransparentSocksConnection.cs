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

			if (connectionTracker.mappings.ContainsKey(remoteEndPoint))
			{
				var initialEndPoint = new IPEndPoint(localEndPoint.Address, remoteEndPoint.Port);
				var requestedEndPoint = new IPEndPoint(remoteEndPoint.Address, connectionTracker.mappings[remoteEndPoint]);
				var tcpConnection = connectionTracker.GetTCPConnection(initialEndPoint, requestedEndPoint);

				var logMessage = string.Format("{0}[{1}] {2} {{0}} connection to {3}",
					tcpConnection != null ? tcpConnection.ProcessName : "unknown",
					tcpConnection != null ? tcpConnection.PID : 0,
					initialEndPoint, requestedEndPoint);
				try
				{
					var proxy = new ProxySocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
					configureProxySocket(proxy, requestedEndPoint);

					debug.Log(1, logMessage + " via {1}", "requested", proxy.ProxyEndPoint);

					proxy.Connect(requestedEndPoint);

					SocketPump.Pump(client, proxy);

					proxy.Close();
					debug.Log(1, logMessage, "closing");
				}
				catch (Exception ex)
				{
					client.Send(Encoding.ASCII.GetBytes(ex.ToString()));
					debug.Log(1, logMessage + ": {1}", "failed", ex);
				}

				connectionTracker.QueueForCleanUp(remoteEndPoint);
			}
			else
			{
				var tcpConnection = connectionTracker.GetTCPConnection(remoteEndPoint, localEndPoint);
				debug.Log(1, "{0}[{1}] {2} has no mapping",
					tcpConnection != null ? tcpConnection.ProcessName : "unknown",
					tcpConnection != null ? tcpConnection.PID : 0,
					remoteEndPoint);
				client.Send(Encoding.ASCII.GetBytes("No mapping\r\n"));
			}

			client.Close();
		}
	}
}
