using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using IpHlpApidotnet;

namespace SocksTun.Services
{
	class ConnectionTracker : IService
	{
		private readonly DebugWriter debug;
		private readonly Dictionary<Connection, Connection> mappings = new Dictionary<Connection, Connection>();
		private readonly Timer mappingCleanupTimer;
		private readonly Queue<KeyValuePair<DateTime, Connection>> mappingCleanUp = new Queue<KeyValuePair<DateTime, Connection>>();
		private readonly TCPUDPConnections tcpUdpConnections = new TCPUDPConnections { FetchTcpConnections = true };

		public ConnectionTracker(DebugWriter debug, IDictionary<string, IService> services)
		{
			this.debug = debug;

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
					var connection = mappingCleanUp.Dequeue().Value;
					if (!mappings.ContainsKey(connection)) continue;
					var expect = mappings[connection];
					debug.Log(3, "src={0} dst={1} [CLN] src={2} dst={3}", connection.Source, connection.Destination, expect.Source, expect.Destination);
					mappings.Remove(expect.Mirror);
					mappings.Remove(connection);
				}
		}

		public void QueueForCleanUp(Connection connection)
		{
			lock (mappingCleanUp)
				mappingCleanUp.Enqueue(new KeyValuePair<DateTime, Connection>(DateTime.Now.AddSeconds(30), connection));
		}

		public TCPUDPConnection GetTCPConnection(EndPoint localEndPoint, EndPoint remoteEndPoint)
		{
			tcpUdpConnections.Refresh();
			return tcpUdpConnections.SingleOrDefault(c => c.Local.Equals(localEndPoint) && c.Remote.Equals(remoteEndPoint));
		}

		public Connection this[Connection connection]
		{
			get
			{
				return mappings.ContainsKey(connection) ? mappings[connection] : null;
			}
			set
			{
				mappings[connection] = value;
				mappings[value.Mirror] = connection.Mirror;
			}
		}
	}

	public class Connection : IEquatable<Connection>
	{
		public readonly ProtocolType Protocol;
		public readonly IPEndPoint Source;
		public readonly IPEndPoint Destination;

		public Connection(ProtocolType protocol, IPEndPoint source, IPEndPoint destination)
		{
			Protocol = protocol;
			Source = source;
			Destination = destination;
		}

		public bool Equals(Connection other)
		{
			return Protocol.Equals(other.Protocol) && Source.Equals(other.Source) && Destination.Equals(other.Destination);
		}

		public override int GetHashCode()
		{
			return Protocol.GetHashCode() ^ Source.GetHashCode() ^ Destination.GetHashCode();
		}

		public Connection Mirror
		{
			get
			{
				return new Connection(Protocol, Destination, Source);
			}
		}
	}
}
