using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocksTun.Services
{
	class LogConnection
	{
		private readonly TcpClient client;
		private readonly DebugWriter debug;
		private readonly ConnectionTracker connectionTracker;
		private readonly Natter natter;
		private readonly NetworkStream stream;
		private readonly byte[] buffer = new byte[0x1000];

		private bool connected;

		public LogConnection(TcpClient client, DebugWriter debug, ConnectionTracker connectionTracker, Natter natter)
		{
			this.client = client;
			this.debug = debug;
			this.connectionTracker = connectionTracker;
			this.natter = natter;
			stream = client.GetStream();
		}

		private void ReadComplete(IAsyncResult ar)
		{
			try
			{
				var count = stream.EndRead(ar);
				if (count > 0)
					stream.BeginRead(buffer, 0, 0x1000, ReadComplete, null);
				else
					connected = false;
			}
			catch (SystemException)
			{
				connected = false;
			}
		}

		public void Process()
		{
			connected = true;
			var writer = new StreamWriter(stream);

			stream.BeginRead(buffer, 0, 0x1000, ReadComplete, null);

			var localEndPoint = (IPEndPoint)client.Client.LocalEndPoint;
			var remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;

			var tcpConnection = connectionTracker.GetTCPConnection(remoteEndPoint, localEndPoint);

			var logMessage = string.Format("{0}[{1}] {2} {{0}} log",
				tcpConnection != null ? tcpConnection.ProcessName : "unknown",
				tcpConnection != null ? tcpConnection.PID : 0,
				client.Client.RemoteEndPoint);

			debug.Log(1, logMessage, "connected to");
			debug.Log(1, natter.GetStatus());

			try
			{
				writer.Write(debug.Status);
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

			debug.Log(1, logMessage, "disconnected from");
		}
	}
}
