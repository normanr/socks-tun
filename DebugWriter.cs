using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
			if (Writer != null)
				Writer.WriteLine(line);
			else if (level < 1)
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
	}
}
