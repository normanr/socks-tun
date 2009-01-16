/*
 *  TAP-Win32 -- A kernel driver to provide virtual tap device functionality
 *               on Windows.  Originally derived from the CIPE-Win32
 *               project by Damion K. Wilson, with extensive modifications by
 *               James Yonan.
 *
 *  All source code which derives from the CIPE-Win32 project is
 *  Copyright (C) Damion K. Wilson, 2003, and is released under the
 *  GPL version 2 (see below).
 *
 *  All other source code is Copyright (C) James Yonan, 2003-2004,
 *  and is released under the GPL version 2 (see below).
 *
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program (see the file COPYING included with this
 *  distribution); if not, write to the Free Software Foundation, Inc.,
 *  59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */

//===============================================
// This file is included both by OpenVPN and
// the TAP-Win32 driver and contains definitions
// common to both.
//===============================================

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SocksTun
{
	public class TunTapDevice
	{
		//=============
		// TAP IOCTLs
		//=============

		private static uint TAP_CONTROL_CODE(uint request, uint method)
		{
			return Win32Api.CTL_CODE(Win32Api.FILE_DEVICE_UNKNOWN, request, method, Win32Api.FILE_ANY_ACCESS);
		}

		// Present in 8.1

		private static readonly uint TAP_IOCTL_GET_MAC = TAP_CONTROL_CODE(1, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_GET_VERSION = TAP_CONTROL_CODE(2, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_GET_MTU = TAP_CONTROL_CODE(3, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_GET_INFO = TAP_CONTROL_CODE(4, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_CONFIG_POINT_TO_POINT = TAP_CONTROL_CODE(5, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_SET_MEDIA_STATUS = TAP_CONTROL_CODE(6, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_CONFIG_DHCP_MASQ = TAP_CONTROL_CODE(7, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_GET_LOG_LINE = TAP_CONTROL_CODE(8, Win32Api.METHOD_BUFFERED);
		private static readonly uint TAP_IOCTL_CONFIG_DHCP_SET_OPT = TAP_CONTROL_CODE(9, Win32Api.METHOD_BUFFERED);

		// Added in 8.2

		/* obsoletes TAP_IOCTL_CONFIG_POINT_TO_POINT */
		private static readonly uint TAP_IOCTL_CONFIG_TUN = TAP_CONTROL_CODE(10, Win32Api.METHOD_BUFFERED);

		//=================
		// Registry keys
		//=================

		private const string ADAPTER_KEY = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";
		private const string NETWORK_CONNECTIONS_KEY = @"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}";

		//======================
		// Filesystem prefixes
		//======================

		private const string USERMODEDEVICEDIR = @"\\.\Global\";
		private const string SYSDEVICEDIR = @"\Device\";
		private const string USERDEVICEDIR = @"\DosDevices\Global\";
		private const string TAPSUFFIX = ".tap";

		//=========================================================
		// TAP_COMPONENT_ID -- This string defines the TAP driver
		// type -- different component IDs can reside in the system
		// simultaneously.
		//=========================================================

		private const string TAP_COMPONENT_ID = "tap0801";

		//=========================================================
		// .Net implementation - based on code from
		// http://www.varsanofiev.com/inside/using_tuntap_under_windows.htm
		//=========================================================

		private readonly string devGuid;
		public FileStream Stream { get; private set; }

		public Guid Guid
		{
			get
			{
				return new Guid(devGuid);
			}
		}

		public string Name
		{
			get
			{
				return GetHumanName(devGuid);
			}
		}

		public TunTapDevice(string deviceName)
		{
			devGuid = GetDeviceGuid(deviceName);
			Stream = new FileStream(
				new SafeFileHandle(
					Win32Api.CreateFile(
						USERMODEDEVICEDIR + devGuid + TAPSUFFIX,
						FileAccess.ReadWrite,
						FileShare.ReadWrite,
						0 /* securityAttributes */,
						FileMode.Open,
						Win32Api.FILE_ATTRIBUTE_SYSTEM | Win32Api.FILE_FLAG_OVERLAPPED,
						IntPtr.Zero),
					true),
				FileAccess.ReadWrite,
				0x1000,
				true);
		}

		private struct GetMacResult
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
			public byte[] mac;
		}

		public string GetMac()
		{
			return DeviceIoControl<GetMacResult>(TAP_IOCTL_GET_MAC, null).mac.Select(b => b.ToString("X2")).Aggregate((s1, s2) => s1 + "-" + s2);
		}

		private struct GetVersionResult
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
			public int[] version;
		}

		public Version GetVersion()
		{
			var data = DeviceIoControl<GetVersionResult>(TAP_IOCTL_GET_VERSION, null);
			return new Version(data.version[0], data.version[1], data.version[2]);
		}

		public int GetMtu()
		{
			return DeviceIoControl<int>(TAP_IOCTL_GET_MTU, null);
		}

		private struct GetInfoResult
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string info;
		}

		public string GetInfo()
		{
			return DeviceIoControl<GetInfoResult>(TAP_IOCTL_GET_INFO, null).info;
		}

		public byte SetMediaStatus(bool state)
		{
			return DeviceIoControl<byte>(TAP_IOCTL_SET_MEDIA_STATUS, state);
		}

		private struct ConfigTunRequest
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] localIP;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] remoteNetwork;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] remoteNetmask;
		}

		public byte ConfigPointToPoint(IPAddress localIP, IPAddress remoteNetwork)
		{
			Debug.Assert(localIP.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(remoteNetwork.AddressFamily == AddressFamily.InterNetwork);
			var data = new ConfigTunRequest
			{
				localIP = localIP.GetAddressBytes(),
				remoteNetwork = remoteNetwork.GetAddressBytes(),
			};
			return DeviceIoControl<byte>(TAP_IOCTL_CONFIG_POINT_TO_POINT, data);
		}

		public byte ConfigTun(IPAddress localIP, IPAddress remoteNetwork, IPAddress remoteNetmask)
		{
			Debug.Assert(localIP.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(remoteNetwork.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(remoteNetmask.AddressFamily == AddressFamily.InterNetwork);
			var data = new ConfigTunRequest
			{
				localIP = localIP.GetAddressBytes(),
				remoteNetwork = remoteNetwork.GetAddressBytes(),
				remoteNetmask = remoteNetmask.GetAddressBytes(),
			};
			return DeviceIoControl<byte>(TAP_IOCTL_CONFIG_TUN, data);
		}

		private struct ConfigDhcpMasqRequest
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] localIP;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] adapterNetmask;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] dhcpServerAddr;
			[MarshalAs(UnmanagedType.U4)]
			public int dhcpLeaseTime;
		}

		public byte ConfigDhcpMasq(IPAddress localIP, IPAddress adapterNetmask, IPAddress dhcpServerAddr, int dhcpLeaseTime)
		{
			Debug.Assert(localIP.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(adapterNetmask.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(dhcpServerAddr.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(dhcpLeaseTime > 0);
			var data = new ConfigDhcpMasqRequest
			{
				localIP = localIP.GetAddressBytes(),
				adapterNetmask = adapterNetmask.GetAddressBytes(),
				dhcpServerAddr = dhcpServerAddr.GetAddressBytes(),
				dhcpLeaseTime = dhcpLeaseTime,
			};
			return DeviceIoControl<byte>(TAP_IOCTL_CONFIG_DHCP_MASQ, data);
		}

		public byte ConfigDhcpSetOptions(params DhcpOption[] dhcpOptions)
		{
			using (var buffer = new MemoryStream())
			{
				using (var writer = new BinaryWriter(buffer))
				{
					foreach (var dhcpOption in dhcpOptions)
					{
						dhcpOption.Write(writer);
					}
				}
				return DeviceIoControl<byte>(TAP_IOCTL_CONFIG_DHCP_SET_OPT, buffer.ToArray());
			}
		}

		private struct GetLogLineResult
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string logLine;
		}

		public string GetLogLine()
		{
			var data = new GetLogLineResult();
			DeviceIoControl<GetLogLineResult>(TAP_IOCTL_GET_LOG_LINE, null);
			return data.logLine;
		}

		private T DeviceIoControl<T>(uint ioctl, object data)
		{
			var nInBufferSize = data != null ? data is byte[] ? ((byte[])data).Length : Marshal.SizeOf(data) : 0;
			var pInBuffer = Marshal.AllocHGlobal(nInBufferSize);
			var nOutBufferSize = Marshal.SizeOf(typeof(T));
			var pOutBuffer = Marshal.AllocHGlobal(nOutBufferSize);
			try
			{
				if (data != null)
					if (data is byte[])
						Marshal.Copy((byte[])data, 0, pInBuffer, nInBufferSize);
					else
						Marshal.StructureToPtr(data, pInBuffer, true);
				int len;
				if (!Win32Api.DeviceIoControl(Stream.SafeFileHandle, ioctl, pInBuffer, (uint)nInBufferSize, pOutBuffer, (uint)nOutBufferSize, out len, IntPtr.Zero))
					throw new Win32Exception(Marshal.GetLastWin32Error());
				Debug.Assert(nOutBufferSize == len);
				return (T)Marshal.PtrToStructure(pOutBuffer, typeof(T));
			}
			finally
			{
				Marshal.FreeHGlobal(pInBuffer);
				Marshal.FreeHGlobal(pOutBuffer);
			}
		}

		//
		// Pick up the tuntap device and return its node GUID
		//
		private static string GetDeviceGuid(string deviceName)
		{
			var regAdapters = Registry.LocalMachine.OpenSubKey(ADAPTER_KEY, false);
			if (regAdapters == null) return string.Empty;
			var devGuid = string.Empty;
			var keyNames = regAdapters.GetSubKeyNames();
			foreach (var x in keyNames)
			{
				int i;
				if (!int.TryParse(x, out i)) continue;
				var regAdapter = regAdapters.OpenSubKey(x);
				if (regAdapter == null) continue;
				var id = regAdapter.GetValue("ComponentId");
				if (id == null || id.ToString() != TAP_COMPONENT_ID) continue;
				devGuid = regAdapter.GetValue("NetCfgInstanceId").ToString();
				if (deviceName == null ||
					devGuid == deviceName ||
					GetHumanName(devGuid) == deviceName) break;
			}
			return devGuid;
		}

		//
		// Returns the device name from the Control panel based on GUID
		//
		private static string GetHumanName(string guid)
		{
			if (guid.Length == 0) return string.Empty;
			var regConnection = Registry.LocalMachine.OpenSubKey(NETWORK_CONNECTIONS_KEY + "\\" + guid + "\\Connection", false);
			if (regConnection == null) return string.Empty;
			var id = regConnection.GetValue("Name");
			return id != null ? id.ToString() : string.Empty;
		}
	}

	public class DhcpOption
	{
		public class DhcpOptionIPAddresses : DhcpOption { public DhcpOptionIPAddresses(byte type, IPAddress[] data) : base(type, data) { } }

		public class Routers : DhcpOptionIPAddresses { public Routers(params IPAddress[] data) : base(3, data) { } };
		public class DNSServers : DhcpOptionIPAddresses { public DNSServers(params IPAddress[] data) : base(6, data) { } };
		public class Domain : DhcpOption { public Domain(string data) : base(15, data) { } };
		public class NTPServers : DhcpOptionIPAddresses { public NTPServers(params IPAddress[] data) : base(42, data) { } };
		public class VendorOptions : DhcpOption { public VendorOptions(params DhcpVendorOption[] data) : base(43, data) { } };
		public class WINSServers : DhcpOptionIPAddresses { public WINSServers(params IPAddress[] data) : base(44, data) { } };
		public class NetBIOSNodeType : DhcpOption { public NetBIOSNodeType(byte data) : base(46, data) { } };
		public class NetBIOSScope : DhcpOption { public NetBIOSScope(string data) : base(47, data) { } };

		private readonly byte type;
		private readonly object data;

		public DhcpOption(byte type, object data)
		{
			this.type = type;
			this.data = data;
		}

		internal void Write(BinaryWriter writer)
		{
			WriteBuffer(type, data, writer);
		}

		internal static void WriteBuffer(byte type, object data, BinaryWriter writer)
		{
			byte[] buffer;
			if (data is string)
			{
				buffer = Encoding.ASCII.GetBytes((string)data);
			}
			else if (data is byte)
			{
				buffer = new[] { (byte)data };
			}
			else if (data is int)
			{
				buffer = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)data));
			}
			else if (data is byte[])
			{
				buffer = (byte[])data;
			}
			else if (data is IPAddress[])
			{
				var ipAddresses = (IPAddress[])data;
				buffer = new byte[ipAddresses.Length * 4];
				for (var i = 0; i < ipAddresses.Length; i++)
				{
					var bytes = ipAddresses[i].GetAddressBytes();
					if (bytes.Length != 4)
						throw new InvalidDataException(string.Format("IpAddress '{0}' must be 4 bytes", ipAddresses[i]));
					bytes.CopyTo(buffer, i * 4);
				}
			}
			else if (data is DhcpVendorOption[])
			{
				using (var vendorBuffer = new MemoryStream())
				{
					using (var vendorWriter = new BinaryWriter(vendorBuffer))
					{
						foreach (var dhcpVendorOption in (DhcpVendorOption[])data)
						{
							dhcpVendorOption.Write(vendorWriter);
						}
					}
					buffer = vendorBuffer.ToArray();
				}
			}
			else
			{
				throw new InvalidDataException("Unknown DHCP option type: " + data.GetType().Name);
			}
			if (buffer.Length < 1 || buffer.Length > 255)
				throw new InvalidDataException("DhcpOption must be > 0 bytes and <= 255 bytes");
			writer.Write(type);
			writer.Write((byte)buffer.Length);
			writer.Write(buffer);
		}
	}

	public class DhcpVendorOption
	{
		public class NetBIOSOverTCP : DhcpVendorOption { public NetBIOSOverTCP(int data) : base(1, data) { } };

		private readonly byte type;
		private readonly object data;

		public DhcpVendorOption(byte type, object data)
		{
			this.type = type;
			this.data = data;
		}

		internal void Write(BinaryWriter writer)
		{
			DhcpOption.WriteBuffer(type, data, writer);
		}
	}
}
