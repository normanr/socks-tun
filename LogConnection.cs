using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace SocksTun
{
	class LogConnection
	{
		private readonly TcpClient client;
		private readonly DebugWriter debug;
		private readonly TunTapDevice tunTapDevice;
		private readonly NetworkStream stream;
		private readonly byte[] buffer = new byte[0x1000];

		private bool connected;

		public LogConnection(TcpClient client, DebugWriter debug, TunTapDevice tunTapDevice)
		{
			this.client = client;
			this.debug = debug;
			this.tunTapDevice = tunTapDevice;
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

			debug.Log(1, "{0} connected to log", client.Client.RemoteEndPoint);
			debug.Log(1, tunTapDevice.GetInfo());

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
		}
	}
}
