using System;
using System.Diagnostics;
using System.Net;

namespace IpHlpApidotnet
{
	public static class Utils
	{
		public const int NO_ERROR = 0;
		public const int MIB_TCP_STATE_CLOSED = 1;
		public const int MIB_TCP_STATE_LISTEN = 2;
		public const int MIB_TCP_STATE_SYN_SENT = 3;
		public const int MIB_TCP_STATE_SYN_RCVD = 4;
		public const int MIB_TCP_STATE_ESTAB = 5;
		public const int MIB_TCP_STATE_FIN_WAIT1 = 6;
		public const int MIB_TCP_STATE_FIN_WAIT2 = 7;
		public const int MIB_TCP_STATE_CLOSE_WAIT = 8;
		public const int MIB_TCP_STATE_CLOSING = 9;
		public const int MIB_TCP_STATE_LAST_ACK = 10;
		public const int MIB_TCP_STATE_TIME_WAIT = 11;
		public const int MIB_TCP_STATE_DELETE_TCB = 12;

		#region helper function

		public static UInt16 ConvertPort(UInt32 dwPort)
		{
			byte[] b = new Byte[2];
			// high weight byte
			b[0] = byte.Parse((dwPort >> 8).ToString());
			// low weight byte
			b[1] = byte.Parse((dwPort & 0xFF).ToString());
			return BitConverter.ToUInt16(b, 0);
		}

		public static int BufferToInt(byte[] buffer, ref int nOffset)
		{
			int res = (((int)buffer[nOffset])) + (((int)buffer[nOffset + 1]) << 8) +
				(((int)buffer[nOffset + 2]) << 16) + (((int)buffer[nOffset + 3]) << 24);
			nOffset += 4;
			return res;
		}

		public static string StateToStr(int state)
		{
			string strg_state = "";
			switch (state)
			{
				case MIB_TCP_STATE_CLOSED: strg_state = "CLOSED"; break;
				case MIB_TCP_STATE_LISTEN: strg_state = "LISTEN"; break;
				case MIB_TCP_STATE_SYN_SENT: strg_state = "SYN_SENT"; break;
				case MIB_TCP_STATE_SYN_RCVD: strg_state = "SYN_RCVD"; break;
				case MIB_TCP_STATE_ESTAB: strg_state = "ESTAB"; break;
				case MIB_TCP_STATE_FIN_WAIT1: strg_state = "FIN_WAIT1"; break;
				case MIB_TCP_STATE_FIN_WAIT2: strg_state = "FIN_WAIT2"; break;
				case MIB_TCP_STATE_CLOSE_WAIT: strg_state = "CLOSE_WAIT"; break;
				case MIB_TCP_STATE_CLOSING: strg_state = "CLOSING"; break;
				case MIB_TCP_STATE_LAST_ACK: strg_state = "LAST_ACK"; break;
				case MIB_TCP_STATE_TIME_WAIT: strg_state = "TIME_WAIT"; break;
				case MIB_TCP_STATE_DELETE_TCB: strg_state = "DELETE_TCB"; break;
			}
			return strg_state;
		}

		public static IPEndPoint BufferToIPEndPoint(byte[] buffer, ref int nOffset, bool IsRemote)
		{
			//address
			Int64 m_Address = ((((buffer[nOffset + 3] << 0x18) | (buffer[nOffset + 2] << 0x10)) | (buffer[nOffset + 1] << 8)) | buffer[nOffset]) & ((long)0xffffffff);
			nOffset += 4;
			int m_Port = 0;
			m_Port = (IsRemote && (m_Address == 0)) ? 0 :
						(((int)buffer[nOffset]) << 8) + (((int)buffer[nOffset + 1])) + (((int)buffer[nOffset + 2]) << 24) + (((int)buffer[nOffset + 3]) << 16);
			nOffset += 4;

			// store the remote endpoint
			IPEndPoint temp = new IPEndPoint(m_Address, m_Port);
			if (temp == null)
				Debug.WriteLine("Parsed address is null. Addr=" + m_Address.ToString() + " Port=" + m_Port + " IsRemote=" + IsRemote.ToString());
			return temp;
		}

		public static string GetHostName(IPEndPoint HostAddress, string LocalHostName)
		{
			try
			{
				if (HostAddress.Address.Equals(IPAddress.Any))
				{
					if (HostAddress.Port > 0)
						return LocalHostName + ":" + HostAddress.Port.ToString();
					else
						return "Anyone";
				}
				return Dns.GetHostEntry(HostAddress.Address).HostName + ":" + HostAddress.Port.ToString();
			}
			catch
			{
				return HostAddress.ToString();
			}
		}

		public static string GetLocalHostName()
		{
			//IPGlobalProperties.GetIPGlobalProperties().DomainName +"." + IPGlobalProperties.GetIPGlobalProperties().HostName
			return Dns.GetHostEntry("localhost").HostName;
		}

		public static int CompareIPEndPoints(IPEndPoint first, IPEndPoint second)
		{
			int i;
			byte[] _first = first.Address.GetAddressBytes();
			byte[] _second = second.Address.GetAddressBytes();
			for (int j = 0; j < _first.Length; j++)
			{
				i = _first[j] - _second[j];
				if (i != 0)
					return i;
			}
			i = first.Port - second.Port;
			if (i != 0)
				return i;
			return 0;
		}

		public static string GetProcessNameByPID(int processID)
		{
			//could be an error here if the process die before we can get his name
			try
			{
				Process p = Process.GetProcessById((int)processID);
				return p.ProcessName;
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
				return "Unknown";
			}
		}
		#endregion
	}
}
