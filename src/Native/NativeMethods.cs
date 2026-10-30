using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DisplayInputSwitcher.Native
{
    public static class NativeMethods
    {
        public const int MONITORINFOF_PRIMARY = 0x00000001;
        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
        public const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;

        public const int WM_HOTKEY = 0x0312;
        public const int WM_DISPLAYCHANGE = 0x007E;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Well-known Windows Graphics DDC/CI I2C Win32/NTSTATUS error codes
        public const int ERROR_GRAPHICS_I2C_NOT_SUPPORTED = unchecked((int)0xC0262580);
        public const int ERROR_GRAPHICS_I2C_DEVICE_DOES_NOT_EXIST = unchecked((int)0xC0262581);
        public const int ERROR_GRAPHICS_I2C_ERROR_TRANSMITTING_DATA = unchecked((int)0xC0262582);
        public const int ERROR_GRAPHICS_I2C_ERROR_RECEIVING_DATA = unchecked((int)0xC0262583);
        public const int ERROR_GRAPHICS_DDCCI_VCP_NOT_SUPPORTED = unchecked((int)0xC0262584);
        public const int ERROR_GRAPHICS_DDCCI_INVALID_DATA = unchecked((int)0xC0262585);
        public const int ERROR_GRAPHICS_DDCCI_MONITOR_RETURNED_INVALID_TIMING_STATUS_BYTE = unchecked((int)0xC0262586);
        public const int ERROR_GRAPHICS_MCA_INTERNAL_ERROR = unchecked((int)0xC026258A);
        public const int ERROR_GRAPHICS_INVALID_PHYSICAL_MONITOR_HANDLE = unchecked((int)0xC026258C);

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEXW
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICEW
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevicesW(
            string lpDevice,
            uint iDevNum,
            ref DISPLAY_DEVICEW lpDisplayDevice,
            uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            out uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            uint dwPhysicalMonitorArraySize,
            [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(
            uint dwPhysicalMonitorArraySize,
            [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr hMonitor,
            byte bVCPCode,
            out uint pvct,
            out uint pdwCurrentValue,
            out uint pdwMaximumValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetVCPFeature(
            IntPtr hMonitor,
            byte bVCPCode,
            uint dwNewValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCapabilitiesStringLength(
            IntPtr hMonitor,
            out uint pdwCapabilitiesStringLengthInCharacters);

        [DllImport("dxva2.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CapabilitiesRequestAndCapabilitiesReply(
            IntPtr hMonitor,
            [Out] byte[] pszASCIICapabilitiesString,
            uint dwCapabilitiesStringLengthInCharacters);

        public static bool IsTransientI2cError(int err)
        {
            return err == ERROR_GRAPHICS_I2C_ERROR_TRANSMITTING_DATA ||
                   err == ERROR_GRAPHICS_I2C_ERROR_RECEIVING_DATA ||
                   err == ERROR_GRAPHICS_DDCCI_INVALID_DATA ||
                   err == ERROR_GRAPHICS_DDCCI_MONITOR_RETURNED_INVALID_TIMING_STATUS_BYTE;
        }

        public static bool IsStaleMonitorHandleError(int err)
        {
            // 6 == ERROR_INVALID_HANDLE
            return err == 6 || err == ERROR_GRAPHICS_INVALID_PHYSICAL_MONITOR_HANDLE;
        }

        public static string DescribeDdcError(int win32Error)
        {
            switch (win32Error)
            {
                case ERROR_GRAPHICS_I2C_NOT_SUPPORTED:
                    return "Display does not support DDC/CI over I2C (e.g., internal laptop panel).";
                case ERROR_GRAPHICS_I2C_DEVICE_DOES_NOT_EXIST:
                    return "Monitor DDC/CI I2C address not responding (check if DDC/CI is enabled in monitor OSD).";
                case ERROR_GRAPHICS_I2C_ERROR_TRANSMITTING_DATA:
                    return "I2C transmit error while talking to monitor (display may be switching inputs or sleeping).";
                case ERROR_GRAPHICS_I2C_ERROR_RECEIVING_DATA:
                    return "I2C receive timeout from monitor (display may have moved to another input).";
                case ERROR_GRAPHICS_DDCCI_VCP_NOT_SUPPORTED:
                    return "Monitor reported that this VCP feature code is unsupported.";
                case ERROR_GRAPHICS_DDCCI_INVALID_DATA:
                    return "Monitor returned malformed DDC/CI reply data.";
                case ERROR_GRAPHICS_INVALID_PHYSICAL_MONITOR_HANDLE:
                case 6:
                    return "Physical monitor handle became stale after display change; refreshing.";
                default:
                    try
                    {
                        return string.Format("{0} (0x{1:X8})", new Win32Exception(win32Error).Message, win32Error);
                    }
                    catch
                    {
                        return string.Format("Win32 DDC/CI error 0x{0:X8}", win32Error);
                    }
            }
        }
    }
}

