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
using System.Collections.Generic;
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

		public static readonly uint TAP_IOCTL_GET_MAC = TAP_CONTROL_CODE(1, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_GET_VERSION = TAP_CONTROL_CODE(2, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_GET_MTU = TAP_CONTROL_CODE(3, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_GET_INFO = TAP_CONTROL_CODE(4, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_CONFIG_POINT_TO_POINT = TAP_CONTROL_CODE(5, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_SET_MEDIA_STATUS = TAP_CONTROL_CODE(6, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_CONFIG_DHCP_MASQ = TAP_CONTROL_CODE(7, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_GET_LOG_LINE = TAP_CONTROL_CODE(8, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_CONFIG_DHCP_SET_OPT = TAP_CONTROL_CODE(9, Win32Api.METHOD_BUFFERED);
		public static readonly uint TAP_IOCTL_CONFIG_TUN = TAP_CONTROL_CODE(10, Win32Api.METHOD_BUFFERED);

		//=================
		// Registry keys
		//=================

		public const string ADAPTER_KEY = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}";
		public const string NETWORK_CONNECTIONS_KEY = @"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}";

		//======================
		// Filesystem prefixes
		//======================

		public const string USERMODEDEVICEDIR = @"\\.\Global\";
		public const string SYSDEVICEDIR = @"\Device\";
		public const string USERDEVICEDIR = @"\DosDevices\Global\";
		public const string TAPSUFFIX = ".tap";

		//=========================================================
		// TAP_COMPONENT_ID -- This string defines the TAP driver
		// type -- different component IDs can reside in the system
		// simultaneously.
		//=========================================================

		public const string TAP_COMPONENT_ID = "tap0801";

		//=========================================================
		// .Net implementation - based on code from
		// http://www.varsanofiev.com/inside/using_tuntap_under_windows.htm
		//=========================================================

		private readonly string devGuid;
		public SafeFileHandle Handle { get; private set; }

		public string HumanName
		{
			get
			{
				return GetHumanName(devGuid);
			}
		}

		public TunTapDevice(string deviceName)
		{
			devGuid = GetDeviceGuid(deviceName);
			Handle = new SafeFileHandle(Win32Api.CreateFile(USERMODEDEVICEDIR + devGuid + TAPSUFFIX, FileAccess.ReadWrite,
				FileShare.ReadWrite, 0, FileMode.Open, Win32Api.FILE_ATTRIBUTE_SYSTEM | Win32Api.FILE_FLAG_OVERLAPPED, IntPtr.Zero), true);
		}

		public bool GetMtu(ref int mtu)
		{
			return DeviceIoControl(TAP_IOCTL_GET_MTU, ref mtu);
		}

		public struct GetInfoData
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string info;
		}

		public bool GetInfo(ref string info)
		{
			var data = new GetInfoData();
			try
			{
				return DeviceIoControl(TAP_IOCTL_GET_INFO, ref data);
			}
			finally
			{
				info = data.info;
			}
		}

		public bool SetMediaStatus(bool state)
		{
			return DeviceIoControl(TAP_IOCTL_SET_MEDIA_STATUS, ref state);
		}

		public struct ConfigTunData
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] localIP;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] remoteNetwork;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] remoteNetmask;
		}

		public bool ConfigTun(IPAddress localIP, IPAddress remoteNetwork, IPAddress remoteNetmask)
		{
			Debug.Assert(localIP.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(remoteNetwork.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(remoteNetmask.AddressFamily == AddressFamily.InterNetwork);
			var data = new ConfigTunData
			{
				localIP = localIP.GetAddressBytes(),
				remoteNetwork = remoteNetwork.GetAddressBytes(),
				remoteNetmask = remoteNetmask.GetAddressBytes(),
			};
			return DeviceIoControl(TAP_IOCTL_CONFIG_TUN, ref data);
		}

		public struct ConfigDhcpMasqData
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] localIP;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] adapterNetmask;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public byte[] dhcpServerAddr;
			public int dhcpLeaseTime;
		}

		public bool ConfigDhcpMasq(IPAddress localIP, IPAddress adapterNetmask, IPAddress dhcpServerAddr, int dhcpLeaseTime)
		{
			Debug.Assert(localIP.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(adapterNetmask.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(dhcpServerAddr.AddressFamily == AddressFamily.InterNetwork);
			Debug.Assert(dhcpLeaseTime > 0);
			var data = new ConfigDhcpMasqData
			{
				localIP = localIP.GetAddressBytes(),
				adapterNetmask = adapterNetmask.GetAddressBytes(),
				dhcpServerAddr = dhcpServerAddr.GetAddressBytes(),
				dhcpLeaseTime = dhcpLeaseTime,
			};
			return DeviceIoControl(TAP_IOCTL_CONFIG_DHCP_MASQ, ref data);
		}

		private bool DeviceIoControl<T>(uint ioctl, ref T data)
		{
			var cbdata = Marshal.SizeOf(data);
			var pdata = Marshal.AllocHGlobal(cbdata);
			try
			{
				Marshal.StructureToPtr(data, pdata, true);
				try
				{
					int len;
					return Win32Api.DeviceIoControl(Handle, ioctl, pdata, (uint) cbdata, pdata, (uint) cbdata, out len, IntPtr.Zero);
				}
				finally
				{
					data = (T)Marshal.PtrToStructure(pdata, typeof(T));
				}
			}
			finally
			{
				Marshal.FreeHGlobal(pdata);
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
}
