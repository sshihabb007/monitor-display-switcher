using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DisplayInputSwitcher.Core;
using DisplayInputSwitcher.Native;
using Microsoft.Win32;

namespace DisplayInputSwitcher.Services
{
    /// <summary>
    /// Discovers active GDI monitors, acquires Dxva2 PHYSICAL_MONITOR handles,
    /// correlates EnumDisplayDevicesW adapter/monitor paths, and reads Registry EDID
    /// (MonitorName + SerialNumber) using the same correlation chain as ControlMyMonitor.exe.
    /// </summary>
    public sealed class MonitorDiscoveryService : IDisposable
    {
        private readonly object _gate = new object();
        private List<MonitorDeviceRecord> _ownedMonitors = new List<MonitorDeviceRecord>();
        private bool _disposed;

        public IReadOnlyList<MonitorDeviceRecord> RefreshMonitors(bool probeCapabilities)
        {
            List<MonitorDeviceRecord> discovered = EnumerateMonitorsCore(probeCapabilities);
            List<MonitorDeviceRecord> previous;
            lock (_gate)
            {
                if (_disposed)
                {
                    DisposeList(discovered);
                    return new List<MonitorDeviceRecord>();
                }
                previous = _ownedMonitors;
                _ownedMonitors = discovered;
            }

            DisposeList(previous);
            return discovered;
        }

        public IReadOnlyList<MonitorDeviceRecord> GetCurrentMonitors()
        {
            lock (_gate)
            {
                return new List<MonitorDeviceRecord>(_ownedMonitors);
            }
        }

        private static List<MonitorDeviceRecord> EnumerateMonitorsCore(bool probeCapabilities)
        {
            List<IntPtr> hMonitors = new List<IntPtr>();
            NativeMethods.MonitorEnumProc callback = delegate(IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
            {
                if (hMon != IntPtr.Zero)
                {
                    hMonitors.Add(hMon);
                }
                return true;
            };

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

            Dictionary<string, string> adapterNamesByGdi = BuildAdapterNameMap();
            List<MonitorDeviceRecord> results = new List<MonitorDeviceRecord>();

            for (int mIdx = 0; mIdx < hMonitors.Count; mIdx++)
            {
                IntPtr hMon = hMonitors[mIdx];
                NativeMethods.MONITORINFOEXW mi = new NativeMethods.MONITORINFOEXW();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEXW));
                if (!NativeMethods.GetMonitorInfoW(hMon, ref mi))
                {
                    continue;
                }

                string gdiDevice = mi.szDevice ?? string.Empty;
                bool isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;

                string adapterName = string.Empty;
                adapterNamesByGdi.TryGetValue(gdiDevice.ToUpperInvariant(), out adapterName);

                uint physCount = 0;
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out physCount) || physCount == 0)
                {
                    continue;
                }

                NativeMethods.PHYSICAL_MONITOR[] physArray = new NativeMethods.PHYSICAL_MONITOR[physCount];
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMon, physCount, physArray))
                {
                    continue;
                }

                List<ChildDisplayDeviceInfo> childDevices = GetAttachedMonitorDevicesForGdi(gdiDevice);

                for (int pIdx = 0; pIdx < physArray.Length; pIdx++)
                {
                    IntPtr physHandle = physArray[pIdx].hPhysicalMonitor;
                    string physDesc = physArray[pIdx].szPhysicalMonitorDescription ?? string.Empty;

                    MonitorDeviceRecord rec = new MonitorDeviceRecord(DestroySingleHandle);
                    rec.HMonitor = hMon;
                    rec.PhysicalMonitorHandle = physHandle;
                    rec.GdiDisplayName = gdiDevice;
                    rec.AdapterName = adapterName ?? string.Empty;
                    rec.PhysicalDescription = physDesc.Trim();
                    rec.IsPrimary = isPrimary;

                    if (pIdx < childDevices.Count)
                    {
                        PopulateFromChildDevice(rec, childDevices[pIdx]);
                    }
                    else if (childDevices.Count > 0)
                    {
                        PopulateFromChildDevice(rec, childDevices[0]);
                    }

                    if (probeCapabilities && physHandle != IntPtr.Zero)
                    {
                        ProbeMonitorSupport(rec);
                    }

                    results.Add(rec);
                }
            }

            // Order external DDC/CI Input-Select capable monitors first so LG ULTRAGEAR is selected ahead of laptop internal eDP panels!
            results.Sort(CompareMonitorPriority);
            return results;
        }

        private static int CompareMonitorPriority(MonitorDeviceRecord a, MonitorDeviceRecord b)
        {
            if (a.SupportsInputSelect != b.SupportsInputSelect)
            {
                return a.SupportsInputSelect ? -1 : 1;
            }
            bool aHasEdidName = !string.IsNullOrEmpty(a.EdidMonitorName);
            bool bHasEdidName = !string.IsNullOrEmpty(b.EdidMonitorName);
            if (aHasEdidName != bHasEdidName)
            {
                return aHasEdidName ? -1 : 1;
            }
            return string.Compare(a.MonitorDeviceName, b.MonitorDeviceName, StringComparison.OrdinalIgnoreCase);
        }

        private static void ProbeMonitorSupport(MonitorDeviceRecord rec)
        {
            uint capLen = 0;
            bool parsedCapsOk = false;
            if (NativeMethods.GetCapabilitiesStringLength(rec.PhysicalMonitorHandle, out capLen) && capLen > 0 && capLen < 16384)
            {
                byte[] buf = new byte[capLen + 8];
                if (NativeMethods.CapabilitiesRequestAndCapabilitiesReply(rec.PhysicalMonitorHandle, buf, capLen))
                {
                    int actualLen = 0;
                    while (actualLen < buf.Length && buf[actualLen] != 0)
                    {
                        actualLen++;
                    }
                    string ascii = Encoding.ASCII.GetString(buf, 0, actualLen);
                    MonitorCapabilities caps = MccsCapabilitiesParser.Parse(ascii);
                    rec.Capabilities = caps;
                    parsedCapsOk = caps.SupportedVcpCodes.Count > 0;

                    if (string.IsNullOrEmpty(rec.EdidMonitorName) && !string.IsNullOrEmpty(caps.ModelFromCapabilities))
                    {
                        rec.EdidMonitorName = caps.ModelFromCapabilities;
                    }

                    rec.SupportsInputSelect = caps.SupportedVcpCodes.Contains(0x60);
                    rec.SupportsBrightness = caps.SupportedVcpCodes.Contains(0x10);
                    rec.SupportsVolume = caps.SupportedVcpCodes.Contains(0x62);
                    rec.SupportsAudioMute = caps.SupportedVcpCodes.Contains(0x8D);
                }
            }
            else
            {
                int capErr = Marshal.GetLastWin32Error();
                if (capErr == NativeMethods.ERROR_GRAPHICS_I2C_NOT_SUPPORTED ||
                    capErr == NativeMethods.ERROR_GRAPHICS_I2C_DEVICE_DOES_NOT_EXIST)
                {
                    // Internal laptop eDP panel with no DDC/CI bus: skip further I2C timeouts immediately.
                    return;
                }
            }

            if (parsedCapsOk)
            {
                return;
            }

            // Fallback direct VCP 0x60 probe when capabilities string is unavailable (ControlMyMonitor LoadingMode 1 -> 2 fallback)
            uint vcpType, cur, max;
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(rec.PhysicalMonitorHandle, 0x60, out vcpType, out cur, out max))
            {
                rec.SupportsInputSelect = true;
            }
            else
            {
                int err60 = Marshal.GetLastWin32Error();
                if (err60 == NativeMethods.ERROR_GRAPHICS_I2C_NOT_SUPPORTED ||
                    err60 == NativeMethods.ERROR_GRAPHICS_I2C_DEVICE_DOES_NOT_EXIST ||
                    err60 == NativeMethods.ERROR_GRAPHICS_I2C_ERROR_RECEIVING_DATA ||
                    err60 == NativeMethods.ERROR_GRAPHICS_I2C_ERROR_TRANSMITTING_DATA)
                {
                    return;
                }
            }

            uint vt, bCur, bMax;
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(rec.PhysicalMonitorHandle, 0x10, out vt, out bCur, out bMax) && bMax > 0)
            {
                rec.SupportsBrightness = true;
            }

            uint vCur, vMax;
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(rec.PhysicalMonitorHandle, 0x62, out vt, out vCur, out vMax) && vMax > 0)
            {
                rec.SupportsVolume = true;
            }
        }

        private sealed class ChildDisplayDeviceInfo
        {
            public string MonitorDeviceName;
            public string MonitorId;
            public string DeviceInterfaceId;
        }

        private static Dictionary<string, string> BuildAdapterNameMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < 64; i++)
            {
                NativeMethods.DISPLAY_DEVICEW dd = new NativeMethods.DISPLAY_DEVICEW();
                dd.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICEW));
                if (!NativeMethods.EnumDisplayDevicesW(null, i, ref dd, 0))
                {
                    break;
                }
                if (!string.IsNullOrEmpty(dd.DeviceName))
                {
                    map[dd.DeviceName.Trim()] = (dd.DeviceString ?? string.Empty).Trim();
                }
            }
            return map;
        }

        private static List<ChildDisplayDeviceInfo> GetAttachedMonitorDevicesForGdi(string gdiDevice)
        {
            List<ChildDisplayDeviceInfo> list = new List<ChildDisplayDeviceInfo>();
            if (string.IsNullOrEmpty(gdiDevice)) return list;

            for (uint i = 0; i < 16; i++)
            {
                NativeMethods.DISPLAY_DEVICEW dd = new NativeMethods.DISPLAY_DEVICEW();
                dd.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICEW));
                if (!NativeMethods.EnumDisplayDevicesW(gdiDevice, i, ref dd, 0))
                {
                    break;
                }

                if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                {
                    continue;
                }

                NativeMethods.DISPLAY_DEVICEW ddIf = new NativeMethods.DISPLAY_DEVICEW();
                ddIf.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICEW));
                string ifaceId = string.Empty;
                if (NativeMethods.EnumDisplayDevicesW(gdiDevice, i, ref ddIf, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    ifaceId = ddIf.DeviceID ?? string.Empty;
                }

                ChildDisplayDeviceInfo info = new ChildDisplayDeviceInfo();
                info.MonitorDeviceName = (dd.DeviceName ?? string.Empty).Trim();
                info.MonitorId = (dd.DeviceID ?? string.Empty).Trim();
                info.DeviceInterfaceId = ifaceId.Trim();
                list.Add(info);
            }
            return list;
        }

        private static void PopulateFromChildDevice(MonitorDeviceRecord rec, ChildDisplayDeviceInfo info)
        {
            rec.MonitorDeviceName = info.MonitorDeviceName;
            rec.MonitorId = info.MonitorId;

            // Extract ShortMonitorID (2nd backslash token in MONITOR\<HardwareID>\{guid}\000x)
            string hardwareId = string.Empty;
            string driverSuffix = string.Empty;
            if (!string.IsNullOrEmpty(info.MonitorId))
            {
                string[] parts = info.MonitorId.Split('\\');
                if (parts.Length >= 2)
                {
                    hardwareId = parts[1];
                    rec.ShortMonitorId = hardwareId;
                }
                if (parts.Length >= 4)
                {
                    driverSuffix = parts[2] + "\\" + parts[3];
                }
            }

            // Extract exact PnP instance ID from \\?\DISPLAY#<HardwareID>#<InstanceId>#{guid} when available
            string instanceId = string.Empty;
            if (!string.IsNullOrEmpty(info.DeviceInterfaceId))
            {
                string[] hashParts = info.DeviceInterfaceId.Split('#');
                if (hashParts.Length >= 3)
                {
                    if (string.IsNullOrEmpty(hardwareId))
                    {
                        hardwareId = hashParts[1];
                        rec.ShortMonitorId = hardwareId;
                    }
                    instanceId = hashParts[2];
                }
            }

            byte[] edidBytes = ReadEdidFromRegistry(hardwareId, instanceId, driverSuffix);
            if (edidBytes != null)
            {
                EdidMetadata edid = EdidParser.Parse(edidBytes);
                if (edid.IsValid)
                {
                    rec.EdidMonitorName = edid.MonitorName;
                    rec.EdidSerialNumber = edid.SerialNumber;
                    rec.ManufacturerCode = edid.ManufacturerCode;
                }
            }
        }

        private static byte[] ReadEdidFromRegistry(string hardwareId, string instanceId, string driverSuffix)
        {
            if (string.IsNullOrEmpty(hardwareId))
            {
                return null;
            }

            try
            {
                // 1. Direct lookup via exact PnP InstanceId from EDD_GET_DEVICE_INTERFACE_NAME
                if (!string.IsNullOrEmpty(instanceId))
                {
                    string exactPath = string.Format(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{0}\{1}\Device Parameters", hardwareId, instanceId);
                    using (RegistryKey dpKey = Registry.LocalMachine.OpenSubKey(exactPath, false))
                    {
                        if (dpKey != null)
                        {
                            byte[] directEdid = dpKey.GetValue("EDID") as byte[];
                            if (directEdid != null && directEdid.Length >= 128)
                            {
                                return directEdid;
                            }
                        }
                    }
                }

                // 2. Fallback matching Driver == "{classGuid}\000x" under Enum\DISPLAY\<HardwareID>\*
                string parentPath = string.Format(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{0}", hardwareId);
                using (RegistryKey hwKey = Registry.LocalMachine.OpenSubKey(parentPath, false))
                {
                    if (hwKey == null) return null;
                    string[] subNames = hwKey.GetSubKeyNames();
                    byte[] firstValid = null;
                    for (int i = 0; i < subNames.Length; i++)
                    {
                        using (RegistryKey instKey = hwKey.OpenSubKey(subNames[i], false))
                        {
                            if (instKey == null) continue;
                            string drv = instKey.GetValue("Driver") as string;
                            using (RegistryKey dpKey = instKey.OpenSubKey("Device Parameters", false))
                            {
                                if (dpKey == null) continue;
                                byte[] edid = dpKey.GetValue("EDID") as byte[];
                                if (edid != null && edid.Length >= 128)
                                {
                                    if (!string.IsNullOrEmpty(driverSuffix) &&
                                        !string.IsNullOrEmpty(drv) &&
                                        string.Equals(drv, driverSuffix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return edid;
                                    }
                                    if (firstValid == null)
                                    {
                                        firstValid = edid;
                                    }
                                }
                            }
                        }
                    }
                    return firstValid;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void DestroySingleHandle(IntPtr hPhysicalMonitor)
        {
            if (hPhysicalMonitor == IntPtr.Zero) return;
            NativeMethods.DestroyPhysicalMonitor(hPhysicalMonitor);
        }

        private static void DisposeList(List<MonitorDeviceRecord> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    list[i].Dispose();
                }
            }
        }

        public void Dispose()
        {
            List<MonitorDeviceRecord> toDispose;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toDispose = _ownedMonitors;
                _ownedMonitors = new List<MonitorDeviceRecord>();
            }
            DisposeList(toDispose);
        }
    }
}
