using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using IpHlpApidotnet;

namespace SocksTun.Services
{
	class ConnectionTracker : IService
	{
		public readonly Dictionary<EndPoint, int> mappings = new Dictionary<EndPoint, int>();

		private readonly Timer mappingCleanupTimer;
		private readonly Queue<KeyValuePair<DateTime, EndPoint>> mappingCleanUp = new Queue<KeyValuePair<DateTime, EndPoint>>();
		private readonly TCPUDPConnections tcpUdpConnections = new TCPUDPConnections { FetchTcpConnections = true };

		public ConnectionTracker(DebugWriter debug, IDictionary<string, IService> services)
		{
			mappingCleanupTimer = new Timer { Interval = 10000 };
			mappingCleanupTimer.Elapsed += mappingCleanupTimer_Elapsed;
		}

		public void Start()
		{
			mappingCleanupTimer.Start();
		}

		public void Stop()
		{
			mappingCleanupTimer.Stop();
		}

		public void mappingCleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
		{
			lock (mappingCleanUp)
				while (mappingCleanUp.Count > 0 && mappingCleanUp.Peek().Key < DateTime.Now)
				{
					var endPoint = mappingCleanUp.Dequeue().Value;
					if (mappings.ContainsKey(endPoint))
						mappings.Remove(endPoint);
				}
		}

		public void QueueForCleanUp(EndPoint endPoint)
		{
			lock (mappingCleanUp)
				mappingCleanUp.Enqueue(new KeyValuePair<DateTime, EndPoint>(DateTime.Now.AddSeconds(30), endPoint));
		}

		public TCPUDPConnection GetTCPConnection(EndPoint localEndPoint, EndPoint remoteEndPoint)
		{
			tcpUdpConnections.Refresh();
			return tcpUdpConnections.SingleOrDefault(c => c.Local.Equals(localEndPoint) && c.Remote.Equals(remoteEndPoint));
		}
	}
}
