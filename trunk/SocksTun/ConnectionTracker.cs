using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;

namespace SocksTun
{
	class ConnectionTracker : IDisposable
	{
		public readonly Dictionary<KeyValuePair<IPAddress, int>, int> mappings = new Dictionary<KeyValuePair<IPAddress, int>, int>();

		private readonly Timer mappingCleanupTimer;
		private readonly Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>> mappingCleanUp = new Queue<KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>>();

		public ConnectionTracker()
		{
			mappingCleanupTimer = new Timer { Interval = 10000 };
			mappingCleanupTimer.Elapsed += mappingCleanupTimer_Elapsed;
			mappingCleanupTimer.Start();
		}

		public void Dispose()
		{
			mappingCleanupTimer.Stop();
		}

		public void mappingCleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
		{
			lock (mappingCleanUp)
				while (mappingCleanUp.Count > 0 && mappingCleanUp.Peek().Key < DateTime.Now)
				{
					var key = mappingCleanUp.Dequeue().Value;
					if (mappings.ContainsKey(key))
						mappings.Remove(key);
				}
		}

		public void QueueForCleanUp(KeyValuePair<IPAddress, int> key)
		{
			lock (mappingCleanUp)
				mappingCleanUp.Enqueue(new KeyValuePair<DateTime, KeyValuePair<IPAddress, int>>(DateTime.Now.AddSeconds(30), key));
		}
	}
}
