using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocksTun
{
	public static class SocketPump
	{
		public static void Pump(Socket s1, Socket s2)
		{
			var wh = new ManualResetEvent(false);
			var p1 = new SinglePump(s1, s2, wh);
			var p2 = new SinglePump(s2, s1, wh);
			p1.Pump();
			p2.Pump();
			while (s1.Connected && s2.Connected && !p1.finished && !p2.finished)
			{
				wh.WaitOne(10000);
			}
		}

		class SinglePump
		{
			const int bufferSize = 10000;
			private readonly Socket s1;
			private readonly Socket s2;
			private readonly EventWaitHandle finishedEvent;
			private readonly byte[] buf = new byte[bufferSize];
			public bool finished { get; private set; }

			public SinglePump(Socket s1, Socket s2, EventWaitHandle finishedEvent)
			{
				this.s1 = s1;
				this.s2 = s2;
				this.finishedEvent = finishedEvent;
			}

			public void Pump()
			{
				try
				{
					if (!s1.Connected || !s2.Connected)
					{
						finished = true;
						finishedEvent.Set();
						return;
					}
					s1.BeginReceive(buf, 0, bufferSize, SocketFlags.None, ReceiveCallback, null);
				}
				catch (ObjectDisposedException)
				{
					finished = true;
					finishedEvent.Set();
				}
				catch (SocketException)
				{
					finished = true;
					finishedEvent.Set();
				}
			}

			private void ReceiveCallback(IAsyncResult ar)
			{
				try
				{
					if (!s1.Connected || !s2.Connected)
					{
						finished = true;
						finishedEvent.Set();
						return;
					}
					var bytesReceived = s1.EndReceive(ar);
					if (bytesReceived > 0)
					{
						s2.Send(buf, 0, bytesReceived, SocketFlags.None);
						Pump();
					}
					else
					{
						finished = true;
						finishedEvent.Set();
					}
				}
				catch (ObjectDisposedException)
				{
					finished = true;
					finishedEvent.Set();
				}
				catch (SocketException)
				{
					finished = true;
					finishedEvent.Set();
				}
			}
		}
	}
}
