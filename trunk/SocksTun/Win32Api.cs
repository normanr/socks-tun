using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SocksTun
{
	public static class Win32Api
	{
		public static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
		{
			return ((DeviceType << 16) | (Access << 14) | (Function << 2) | Method);
		}

		public const uint METHOD_BUFFERED = 0;
		public const uint FILE_ANY_ACCESS = 0;
		public const uint FILE_DEVICE_UNKNOWN = 0x00000022;

		[DllImport("Kernel32.dll", /* ExactSpelling = true, */ SetLastError = true, CharSet = CharSet.Auto)]
		public static extern IntPtr CreateFile(
			string filename,
			[MarshalAs(UnmanagedType.U4)]FileAccess fileaccess,
			[MarshalAs(UnmanagedType.U4)]FileShare fileshare,
			int securityattributes,
			[MarshalAs(UnmanagedType.U4)]FileMode creationdisposition,
			int flags,
			IntPtr template);

		public const int FILE_ATTRIBUTE_SYSTEM = 0x4;
		public const int FILE_FLAG_OVERLAPPED = 0x40000000;

		[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
		public static extern bool DeviceIoControl(SafeHandle hDevice, uint dwIoControlCode,
			IntPtr lpInBuffer, uint nInBufferSize,
			IntPtr lpOutBuffer, uint nOutBufferSize,
			out int lpBytesReturned, IntPtr lpOverlapped);

	}
}
