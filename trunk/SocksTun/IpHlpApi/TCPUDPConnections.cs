using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IEnumerator=System.Collections.IEnumerator;

namespace IpHlpApidotnet
{
	/// <summary>
	/// Store information concerning TCP/UDP connections
	/// </summary>
	public class TCPUDPConnections : IEnumerable<TCPUDPConnection>
	{
		private List<TCPUDPConnection> _list;
		System.Timers.Timer _timer = null;
		private int _DeadConnsMultiplier = 10; //Collect dead connections each 5 sec.
		private int _TimerCounter = -1;

		public TCPUDPConnections()
		{
			_list = new List<TCPUDPConnection>();
			_timer = new System.Timers.Timer();
			_timer.Interval = 1000; // Refresh list every 1 sec.
			_timer.Elapsed += new System.Timers.ElapsedEventHandler(_timer_Elapsed);
		}

		/// <summary>
		/// Coefficient multiplies on AutoRefresh timer interval. The parameter determinate how 
		/// often detecting of dead connections occures.
		/// </summary>
		public int DeadConnsMultiplier
		{
			get { return _DeadConnsMultiplier; }
			set { _DeadConnsMultiplier = value; }
		}

		/// <summary>
		/// AutoRefresh timer. 
		/// </summary>
		public System.Timers.Timer Timer
		{
			get { return _timer; }
		}

		public delegate void ItemAddedEvent(Object sender, TCPUDPConnection item);
		/// <summary>
		/// Event occures when <seealso cref="TCPUDPConnection"/> deleted.
		/// </summary>
		public event ItemAddedEvent ItemAdded;
		private void ItemAddedEventHandler(TCPUDPConnection item)
		{
			if (ItemAdded != null)
			{
				ItemAdded(this, item);
			}
		}

		public delegate void ItemChangedEvent(Object sender, TCPUDPConnection item, int Pos);
		/// <summary>
		/// Event occures when <seealso cref="TCPUDPConnection"/> changed.
		/// </summary>
		public event ItemChangedEvent ItemChanged;
		private void ItemChangedEventHandler(TCPUDPConnection item, int Pos)
		{
			if (ItemChanged != null)
			{
				ItemChanged(this, item, Pos);
			}
		}

		public delegate void ItemInsertedEvent(Object sender, TCPUDPConnection item, int Position);
		/// <summary>
		/// Event occures when <seealso cref="TCPUDPConnection"/> inserted into list.
		/// </summary>
		public event ItemInsertedEvent ItemInserted;
		private void ItemInsertedEventHandler(TCPUDPConnection item, int Position)
		{
			if (ItemInserted != null)
			{
				ItemInserted(this, item, Position);
			}
		}

		public delegate void ItemDeletedEvent(Object sender, TCPUDPConnection item, int Position);
		/// <summary>
		/// Event occures when <seealso cref="TCPUDPConnection"/> deleted from list. 
		/// </summary>
		public event ItemDeletedEvent ItemDeleted;
		private void ItemDeletedEventHandler(TCPUDPConnection item, int Position)
		{
			if (ItemDeleted != null)
			{
				ItemDeleted(this, item, Position);
			}
		}

		void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			this.Refresh();
		}

		/// <summary>
		/// Refresh connections list.
		/// </summary>
		public void Refresh()
		{
			lock (this)
			{
				this._LastRefreshDateTime = DateTime.Now;
				if (FetchTcpConnections) this.GetTcpConnections();
				if (FetchUdpConnections) this.GetUdpConnections();
				_TimerCounter++;
				if (_DeadConnsMultiplier == _TimerCounter)
				{
					this.CheckForClosedConnections();
					_TimerCounter = -1;
				}
			}
		}

		/// <summary>
		/// Get last refresh <seealso cref="DateTime"/>.
		/// </summary>
		private DateTime _LastRefreshDateTime = DateTime.MinValue;
		public DateTime LastRefreshDateTime
		{
			get { return _LastRefreshDateTime; }
		}

		public void StopAutoRefresh()
		{
			_timer.Stop();
		}

		public void StartAutoRefresh()
		{
			_timer.Start();
		}

		/// <summary>
		/// Enable or Disable connections list auto refresh.
		/// </summary>
		public bool AutoRefresh
		{
			get { return _timer.Enabled; }
			set { _timer.Enabled = value; }
		}

		/// <summary>
		/// Add new <seealso cref="TCPUDPConnection"/> connection.
		/// </summary>
		/// <param name="item"></param>
		public void Add(TCPUDPConnection item)
		{
			int Pos = 0;
			TCPUDPConnection conn = IndexOf(item, out Pos);
			if (conn == null)
			{
				item.WasActiveAt = DateTime.Now;
				if (Pos > -1)
				{
					this.Insert(Pos, item);
				}
				else
				{
					_list.Add(item);
					ItemAddedEventHandler(item);
				}
			}
			else
			{
				_list[Pos].WasActiveAt = DateTime.Now;
				if (conn.iState != item.iState ||
					conn.PID != item.PID)
				{
					conn.iState = item.iState;
					conn.PID = item.PID;
					ItemChangedEventHandler(conn, Pos);
				}
			}
		}

		public int Count
		{
			get { return _list.Count; }
		}

		public TCPUDPConnection this[int index]
		{
			get { return _list[index]; }
			set { _list[index] = value; }
		}

		private SortConnections _connComp = new SortConnections();
		public void Sort()
		{
			_list.Sort(_connComp);
		}

		public TCPUDPConnection IndexOf(TCPUDPConnection item, out int Pos)
		{
			int index = -1;
			foreach (TCPUDPConnection conn in _list)
			{
				index++;
				int i = _connComp.CompareConnections(item, conn);
				if (i == 0)
				{
					Pos = index;
					return conn;
				}
				if (i > 0) // If current an item more then conn, try to compare with next one until finding equal or less.
				{
					continue; //Skip
				}
				if (i < 0) // If there is an item in list with row less then current, insert current before this one.
				{
					Pos = index;
					return null;
				}
			}
			Pos = -1;
			return null;
		}

		/// <summary>
		/// Method detect and remove from list all dead connections.
		/// </summary>
		public void CheckForClosedConnections()
		{
			int interval = (int)_timer.Interval * this._DeadConnsMultiplier;
			//Remove item from the end of the list
			for (int index = _list.Count - 1; index >= 0; index--)
			{
				TCPUDPConnection conn = this[index];
				TimeSpan diff = (this._LastRefreshDateTime - conn.WasActiveAt);

				int interval1 = Math.Abs((int)diff.TotalMilliseconds);
				if (interval1 > interval)
				{
					this.Remove(index);
				}
			}
		}

		public void Remove(int index)
		{
			TCPUDPConnection conn = this[index];
			_list.RemoveAt(index);
			this.ItemDeletedEventHandler(conn, index);
		}

		public void Insert(int index, TCPUDPConnection item)
		{
			_list.Insert(index, item);
			ItemInsertedEventHandler(item, index);
		}

		public bool FetchTcpConnections { get; set; }
		public bool FetchUdpConnections { get; set; }

		public void GetTcpConnections()
		{
			int AF_INET = 2;    // IP_v4
			int buffSize = 20000;
			byte[] buffer = new byte[buffSize];
			int res = IPHlpAPI32Wrapper.GetExtendedTcpTable(buffer, out buffSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
			if (res != Utils.NO_ERROR) //If there is no enouth memory to execute function
			{
				buffer = new byte[buffSize];
				res = IPHlpAPI32Wrapper.GetExtendedTcpTable(buffer, out buffSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
				if (res != Utils.NO_ERROR)
				{
					return;
				}
			}
			int nOffset = 0;
			// number of entry in the
			int NumEntries = Convert.ToInt32(buffer[nOffset]);
			nOffset += 4;
			for (int i = 0; i < NumEntries; i++)
			{
				TCPUDPConnection row = new TCPUDPConnection();
				// state
				int st = Convert.ToInt32(buffer[nOffset]);
				// state  by ID
				row.iState = st;
				nOffset += 4;
				row.Protocol = Protocol.TCP;
				row.Local = Utils.BufferToIPEndPoint(buffer, ref nOffset, false);
				row.Remote = Utils.BufferToIPEndPoint(buffer, ref nOffset, true);
				row.PID = Utils.BufferToInt(buffer, ref nOffset);
				this.Add(row);
			}
		}

		public void GetUdpConnections()
		{
			int AF_INET = 2;    // IP_v4
			int buffSize = 20000;
			byte[] buffer = new byte[buffSize];
			int res = IPHlpAPI32Wrapper.GetExtendedUdpTable(buffer, out buffSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
			if (res != Utils.NO_ERROR)
			{
				buffer = new byte[buffSize];
				res = IPHlpAPI32Wrapper.GetExtendedUdpTable(buffer, out buffSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
				if (res != Utils.NO_ERROR)
				{
					return;
				}
			}
			int nOffset = 0;
			int NumEntries = Convert.ToInt32(buffer[nOffset]);
			nOffset += 4;
			for (int i = 0; i < NumEntries; i++)
			{
				TCPUDPConnection row = new TCPUDPConnection();
				row.Protocol = Protocol.UDP;
				row.Local = Utils.BufferToIPEndPoint(buffer, ref nOffset, false);
				row.PID = Utils.BufferToInt(buffer, ref nOffset);
				this.Add(row);
			}
		}

		#region IEnumerable<TCPUDPConnection> Members

		public IEnumerator<TCPUDPConnection> GetEnumerator()
		{
			return _list.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _list.GetEnumerator();
		}

		#endregion
	}

	public class SortConnections : IComparer<TCPUDPConnection>
	{
		/// <summary>
		/// Method is used to compare two <seealso cref="TCPUDPConnection"/>. 
		/// 
		/// </summary>
		/// <param name="first"></param>
		/// <param name="second"></param>
		/// <returns></returns>
		public virtual int CompareConnections(TCPUDPConnection first, TCPUDPConnection second)
		{
			int i;
			i = Utils.CompareIPEndPoints(first.Local, second.Local);
			if (i != 0)
				return i;
			if (first.Protocol == Protocol.TCP &&
				second.Protocol == Protocol.TCP)
			{
				i = Utils.CompareIPEndPoints(first.Remote, second.Remote);
				if (i != 0)
					return i;
			}
			i = first.PID - second.PID;
			if (i != 0)
				return i;
			if (first.Protocol == second.Protocol)
				return 0;
			if (first.Protocol == Protocol.TCP)
				return -1;
			else
				return 1;
		}

		#region IComparer<TCPUDPConnection> Members

		public int Compare(TCPUDPConnection x, TCPUDPConnection y)
		{
			return this.CompareConnections(x, y);
		}

		#endregion
	}
}
