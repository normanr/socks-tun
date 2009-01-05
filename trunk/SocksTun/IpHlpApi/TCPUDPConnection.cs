using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace IpHlpApidotnet
{
	public enum Protocol { TCP, UDP, None };
	/// <summary>
	/// Store information concerning single TCP/UDP connection
	/// </summary>
	public class TCPUDPConnection
	{
		private int _dwState;
		public int iState
		{
			get { return _dwState; }
			set
			{
				if (_dwState == value) return;
				_dwState = value;
				State = Utils.StateToStr(value);
			}
		}

		private bool _IsResolveIP = true;
		public bool IsResolveIP
		{
			get { return _IsResolveIP; }
			set { _IsResolveIP = value; }
		}

		public Protocol Protocol { get; set; }

		public string State { get; private set; }

		private IPEndPoint _Local;
		public IPEndPoint Local  //LocalAddress
		{
			get { return _Local; }
			set
			{
				if (_Local != value)
				{
					_Local = value;
				}
			}
		}

		private IPEndPoint _Remote;
		public IPEndPoint Remote //RemoteAddress
		{
			get { return _Remote; }
			set
			{
				if (_Remote != value)
				{
					_Remote = value;
				}
			}
		}

		private int _dwOwningPid;
		public int PID
		{
			get { return _dwOwningPid; }
			set
			{
				if (_dwOwningPid != value)
				{
					_dwOwningPid = value;
				}
			}
		}

		private void SaveProcessID()
		{
			_ProcessName = Utils.GetProcessNameByPID(_dwOwningPid);
			_OldProcessID = _dwOwningPid;
		}

		private int _OldProcessID = -1;
		private string _ProcessName = String.Empty;
		public string ProcessName
		{
			get
			{
				if (_OldProcessID == _dwOwningPid)
				{
					if (_ProcessName.Trim() == String.Empty)
					{
						SaveProcessID();
					}
				}
				else
				{
					SaveProcessID();
				}
				return _ProcessName;
			}
		}

		private DateTime _WasActiveAt = DateTime.MinValue;
		public DateTime WasActiveAt
		{
			get { return _WasActiveAt; }
			internal set { _WasActiveAt = value; }
		}

	}
}
