using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SocksTun.Properties;

namespace SocksTun.Services
{
	class LogServer : IService
	{
		private readonly DebugWriter debug;
		private readonly IDictionary<string, IService> services;
		private readonly TcpListener logServer;

		private ConnectionTracker connectionTracker;
		private Natter natter;

		public LogServer(DebugWriter debug, IDictionary<string, IService> services)
		{
			this.debug = debug;
			this.services = services;

			logServer = new TcpListener(IPAddress.Loopback, Settings.Default.LogPort);
			logServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
		}

		public void Start()
		{
			connectionTracker = (ConnectionTracker)services["connectionTracker"];
			natter = (Natter)services["natter"];

			logServer.Start();
			debug.Log(0, "LogPort = " + ((IPEndPoint)logServer.LocalEndpoint).Port);
			logServer.BeginAcceptTcpClient(NewLogConnection, null);
		}

		public void Stop()
		{
			// TODO: This should close established connections
		}

		private void NewLogConnection(IAsyncResult ar)
		{
			try
			{
				var client = logServer.EndAcceptTcpClient(ar);

				var connection = new LogConnection(client, debug, connectionTracker, natter);
				connection.Process();
			}
			catch (SystemException)
			{
			}

			logServer.BeginAcceptTcpClient(NewLogConnection, null);
		}
	}
}
