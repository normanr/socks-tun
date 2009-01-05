using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocksTun
{
	class DebugWriter
	{
		public TextWriter Writer { get; set; }
		public int LogLevel { get; set; }
		public int MaxQueuedMessages = 20;
		public readonly StringWriter Status = new StringWriter();
		public readonly BlockingQueue<string> Queue = new BlockingQueue<string>();

		private const string dateTimeFormat = "yyyy'-'MM'-'dd' 'HH':'mm':'ss";
		public void Log(int level, string message)
		{
			if (level >= LogLevel) return;
			var line = DateTime.Now.ToString(dateTimeFormat) + ": " + message;
			if (Writer != null) Writer.WriteLine(line);
			if (level < 1)
			{
				Status.WriteLine(line);
			}
			else
			{
				Queue.Enqueue(line);
				var count = Queue.Count;
				if (count > 0 && count > MaxQueuedMessages)
					Queue.Dequeue();
			}
		}

		public void Log(int level, string format, params object[] args)
		{
			Log(level, string.Format(format, args));
		}

		public void LogBuffer(string prefix, byte[] buf, int bytesRead)
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
						Log(2, "{0}: {1}:{3} -> {2}:{4}", protocol, source, destination, sourcePort, destinationPort);
						break;
					default:
						Log(2, "{0}: {1} -> {2}", protocol, source, destination);
						break;
				}
			}

			var sb = new StringBuilder();
			for (var i = 0; i < bytesRead; i += 0x10)
			{
				sb.Append(prefix + " " + i.ToString("x8") + "  ");
				for (var j = i; j < bytesRead && j < i + 0x10; j++)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					sb.Append(buf[j].ToString("x2") + " ");
				}
				for (var j = i + 0xf; j >= bytesRead; j--)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					sb.Append("   ");
				}
				sb.Append(" ");
				for (var j = i; j < bytesRead && j < i + 0x10; j++)
				{
					if ((j & 0xf) == 0x8) sb.Append(" ");
					var b = (char)buf[j];
					if (b < 0x20) b = '.';
					sb.Append(b);
				}
				sb.AppendLine();
			}
			Log(3, sb.ToString());
		}
	}
}
