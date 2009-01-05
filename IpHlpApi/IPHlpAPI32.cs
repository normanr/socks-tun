using System;
using System.Net;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace IpHlpApidotnet
{
	#region UDP

	[StructLayout(LayoutKind.Sequential)]
	public struct MIB_UDPROW_OWNER_PID
	{
		public IPEndPoint Local;
		public int dwOwningPid;
		public string ProcessName;
	}

	public struct MIB_UDPROW_OWNER_MODULE
	{
		public IPEndPoint Local;
		public uint dwOwningPid;
		public long liCreateTimestamp; //LARGE_INTEGER
		/*union {
			struct {
				DWORD   SpecificPortBind : 1;
			};
			DWORD       dwFlags;
		};*/
		public ulong[] OwningModuleInfo; //size TCPIP_OWNING_MODULE_SIZE
	}

	public struct MIB_UDPTABLE_OWNER_PID
	{
		public int dwNumEntries;
		public MIB_UDPROW_OWNER_PID[] table;
	}

	public struct _MIB_UDPTABLE_OWNER_MODULE
	{
		public uint dwNumEntries;
		public MIB_UDPROW_OWNER_MODULE[] table;
	}

	public enum UDP_TABLE_CLASS
	{
		UDP_TABLE_BASIC, //A MIB_UDPTABLE table that contains all UDP endpoints on the machine is returned to the caller.
		UDP_TABLE_OWNER_PID, //A MIB_UDPTABLE_OWNER_PID or MIB_UDP6TABLE_OWNER_PID that contains all UDP endpoints on the machine is returned to the caller.
		UDP_TABLE_OWNER_MODULE //A MIB_UDPTABLE_OWNER_MODULE or MIB_UDP6TABLE_OWNER_MODULE that contains all UDP endpoints on the machine is returned to the caller.
	}

	public struct MIB_UDPSTATS
	{
		public int dwInDatagrams;
		public int dwNoPorts;
		public int dwInErrors;
		public int dwOutDatagrams;
		public int dwNumAddrs;
	}

	public struct MIB_UDPTABLE
	{
		public int dwNumEntries;
		public MIB_UDPROW[] table;
	}

	public struct MIB_UDPROW
	{
		public IPEndPoint Local;
	}

	public struct MIB_EXUDPTABLE
	{
		public int dwNumEntries;
		public MIB_EXUDPROW[] table;

	}

	public struct MIB_EXUDPROW
	{
		public IPEndPoint Local;
		public int dwProcessId;
		public string ProcessName;
	}

	#endregion

	#region TCP
	[StructLayout(LayoutKind.Sequential)]
	public struct MIB_TCPTABLE_OWNER_MODULE
	{
		public uint dwNumEntries;
		public MIB_TCPROW_OWNER_MODULE[] table;
	}

	public struct MIB_TCPROW_OWNER_MODULE
	{
		public const int TCPIP_OWNING_MODULE_SIZE = 16;
		public uint dwState;
		public IPEndPoint Local; //LocalAddress
		public IPEndPoint Remote; //RemoteAddress
		public uint dwOwningPid;
		public uint liCreateTimestamp; //LARGE_INTEGER
		public ulong[] OwningModuleInfo; //Look how to define array size in structure ULONGLONG   = new ulong[TCPIP_OWNING_MODULE_SIZE]     
	}

	public struct MIB_TCPTABLE_OWNER_PID
	{
		public int dwNumEntries;
		public MIB_TCPROW_OWNER_PID[] table;
	}

	public struct MIB_TCPROW_OWNER_PID
	{
		public int dwState;
		public IPEndPoint Local; //LocalAddress
		public IPEndPoint Remote; //RemoteAddress
		public int dwOwningPid;
		public string State;
		public string ProcessName;
	}

	public enum TCP_TABLE_CLASS
	{
		TCP_TABLE_BASIC_LISTENER,
		TCP_TABLE_BASIC_CONNECTIONS,
		TCP_TABLE_BASIC_ALL,
		TCP_TABLE_OWNER_PID_LISTENER,
		TCP_TABLE_OWNER_PID_CONNECTIONS,
		TCP_TABLE_OWNER_PID_ALL,
		TCP_TABLE_OWNER_MODULE_LISTENER,
		TCP_TABLE_OWNER_MODULE_CONNECTIONS,
		TCP_TABLE_OWNER_MODULE_ALL,
	}

	public struct MIB_TCPSTATS
	{
		public int dwRtoAlgorithm;
		public int dwRtoMin;
		public int dwRtoMax;
		public int dwMaxConn;
		public int dwActiveOpens;
		public int dwPassiveOpens;
		public int dwAttemptFails;
		public int dwEstabResets;
		public int dwCurrEstab;
		public int dwInSegs;
		public int dwOutSegs;
		public int dwRetransSegs;
		public int dwInErrs;
		public int dwOutRsts;
		public int dwNumConns;
	}

	public struct MIB_TCPTABLE
	{
		public int dwNumEntries;
		public MIB_TCPROW[] table;
	}

	public struct MIB_TCPROW
	{
		public string StrgState;
		public int iState;
		public IPEndPoint Local;
		public IPEndPoint Remote;
	}

	public struct MIB_EXTCPTABLE
	{
		public int dwNumEntries;
		public MIB_EXTCPROW[] table;

	}

	public struct MIB_EXTCPROW
	{
		public string StrgState;
		public int iState;
		public IPEndPoint Local;
		public IPEndPoint Remote;
		public int dwProcessId;
		public string ProcessName;
	}
	#endregion

	public static class IPHlpAPI32Wrapper
	{
		public const byte NO_ERROR = 0;
		public const int FORMAT_MESSAGE_ALLOCATE_BUFFER = 0x00000100;
		public const int FORMAT_MESSAGE_IGNORE_INSERTS = 0x00000200;
		public const int FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;
		public static int dwFlags = FORMAT_MESSAGE_ALLOCATE_BUFFER |
			FORMAT_MESSAGE_FROM_SYSTEM |
			FORMAT_MESSAGE_IGNORE_INSERTS;

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public extern static int GetUdpStatistics(ref MIB_UDPSTATS pStats);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public static extern int GetUdpTable(byte[] UcpTable, out int pdwSize, bool bOrder);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public extern static int GetTcpStatistics(ref MIB_TCPSTATS pStats);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public static extern int GetTcpTable(byte[] pTcpTable, out int pdwSize, bool bOrder);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public extern static int AllocateAndGetTcpExTableFromStack(ref IntPtr pTable, bool bOrder, IntPtr heap, int zero, int flags);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public extern static int AllocateAndGetUdpExTableFromStack(ref IntPtr pTable, bool bOrder, IntPtr heap, int zero, int flags);

		[DllImport("kernel32", SetLastError = true)]
		public static extern IntPtr GetProcessHeap();

		[DllImport("kernel32", SetLastError = true)]
		private static extern int FormatMessage(int flags, IntPtr source, int messageId,
			int languageId, StringBuilder buffer, int size, IntPtr arguments);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public static extern int GetExtendedTcpTable(byte[] pTcpTable, out int dwOutBufLen, bool sort,
			int ipVersion, TCP_TABLE_CLASS tblClass, int reserved);

		[DllImport("iphlpapi.dll", SetLastError = true)]
		public static extern int GetExtendedUdpTable(byte[] pUdpTable, out int dwOutBufLen, bool sort,
			int ipVersion, UDP_TABLE_CLASS tblClass, int reserved);

		public static string GetAPIErrorMessageDescription(int ApiErrNumber)
		{
			System.Text.StringBuilder sError = new System.Text.StringBuilder(512);
			int lErrorMessageLength;
			lErrorMessageLength = FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, (IntPtr)0, ApiErrNumber, 0, sError, sError.Capacity, (IntPtr)0);

			if (lErrorMessageLength > 0)
			{
				string strgError = sError.ToString();
				strgError = strgError.Substring(0, strgError.Length - 2);
				return strgError + " (" + ApiErrNumber.ToString() + ")";
			}
			return "none";
		}
	}
}

