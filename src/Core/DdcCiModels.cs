using System;
using System.Collections.Generic;

namespace DisplayInputSwitcher.Core
{
    public enum DdcErrorKind
    {
        None = 0,
        MonitorNotFound,
        HandleInvalid,
        I2cBusError,
        UnsupportedVcpCode,
        Win32Failure,
        Disposed
    }

    public sealed class DdcResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T Value { get; private set; }
        public DdcErrorKind ErrorKind { get; private set; }
        public int Win32ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public long ElapsedMs { get; private set; }

        private DdcResult() { }

        public static DdcResult<T> Ok(T value, long elapsedMs)
        {
            DdcResult<T> r = new DdcResult<T>();
            r.IsSuccess = true;
            r.Value = value;
            r.ErrorKind = DdcErrorKind.None;
            r.Win32ErrorCode = 0;
            r.ErrorMessage = string.Empty;
            r.ElapsedMs = elapsedMs;
            return r;
        }

        public static DdcResult<T> Fail(DdcErrorKind kind, int win32Code, string message, long elapsedMs)
        {
            DdcResult<T> r = new DdcResult<T>();
            r.IsSuccess = false;
            r.Value = default(T);
            r.ErrorKind = kind;
            r.Win32ErrorCode = win32Code;
            r.ErrorMessage = message ?? "Unknown DDC/CI error.";
            r.ElapsedMs = elapsedMs;
            return r;
        }
    }

    public sealed class EdidMetadata
    {
        public string ManufacturerCode { get; set; }
        public ushort ProductCode { get; set; }
        public string MonitorName { get; set; }
        public string SerialNumber { get; set; }
        public bool IsValid { get; set; }

        public EdidMetadata()
        {
            ManufacturerCode = string.Empty;
            MonitorName = string.Empty;
            SerialNumber = string.Empty;
        }
    }

    public sealed class MonitorCapabilities
    {
        public string RawMccsString { get; set; }
        public string ModelFromCapabilities { get; set; }
        public string MccsVersion { get; set; }
        public HashSet<byte> SupportedVcpCodes { get; private set; }
        public Dictionary<byte, List<byte>> DiscreteValuesByVcp { get; private set; }

        public MonitorCapabilities()
        {
            RawMccsString = string.Empty;
            ModelFromCapabilities = string.Empty;
            MccsVersion = string.Empty;
            SupportedVcpCodes = new HashSet<byte>();
            DiscreteValuesByVcp = new Dictionary<byte, List<byte>>();
        }

        public List<byte> GetValuesForVcp(byte vcpCode)
        {
            List<byte> vals;
            if (DiscreteValuesByVcp.TryGetValue(vcpCode, out vals) && vals != null)
            {
                return new List<byte>(vals);
            }
            return new List<byte>();
        }

        public List<byte> GetInputSelectValues()
        {
            List<byte> v60 = GetValuesForVcp(0x60);
            if (v60.Count > 0) return v60;
            return GetValuesForVcp(0xF4);
        }
    }

    public sealed class VcpFeatureReply
    {
        public byte VcpCode { get; set; }
        public uint RawCurrentValue { get; set; }
        public uint RawMaxValue { get; set; }
        public uint NormalizedCurrentValue { get; set; }
        public uint NormalizedMaxValue { get; set; }
        public uint VcpType { get; set; }
        public bool WasOneByteMasked { get; set; }
    }

    public sealed class DetectedInputOption
    {
        public uint Code { get; set; }
        public string FriendlyName { get; set; }
        public string ShortChipLabel { get; set; }
        public bool IsHdmiFamily { get; set; }
        public bool IsDpFamily { get; set; }

        public DetectedInputOption()
        {
            FriendlyName = string.Empty;
            ShortChipLabel = string.Empty;
        }
    }

    public sealed class MonitorInputProfile
    {
        public byte InputVcpCode { get; set; }
        public uint HdmiValue { get; set; }
        public uint DpValue { get; set; }
        public string HdmiLabel { get; set; }
        public string DpLabel { get; set; }
        public bool IsUserCustomized { get; set; }

        public MonitorInputProfile()
        {
            InputVcpCode = 0x60;
            HdmiValue = 1;
            DpValue = 3;
            HdmiLabel = "HDMI";
            DpLabel = "DisplayPort";
            IsUserCustomized = false;
        }
    }

    public sealed class ResolvedMonitorInputMap
    {
        public byte InputVcpCode { get; set; }
        public uint HdmiValue { get; set; }
        public uint DpValue { get; set; }
        public string HdmiLabel { get; set; }
        public string DpLabel { get; set; }
        public bool IsUserCustomized { get; set; }
        public string DetectionSummary { get; set; }
        public List<DetectedInputOption> DetectedOptions { get; private set; }

        public ResolvedMonitorInputMap()
        {
            InputVcpCode = 0x60;
            HdmiValue = 1;
            DpValue = 3;
            HdmiLabel = "HDMI";
            DpLabel = "DisplayPort";
            DetectionSummary = "Auto-Detect";
            DetectedOptions = new List<DetectedInputOption>();
        }
    }

    public sealed class MonitorDeviceRecord : IDisposable
    {
        public IntPtr HMonitor { get; set; }
        public IntPtr PhysicalMonitorHandle { get; set; }
        public bool HasPhysicalHandle { get; set; }
        public string GdiDisplayName { get; set; }
        public string MonitorDeviceName { get; set; }
        public string AdapterName { get; set; }
        public string MonitorId { get; set; }
        public string ShortMonitorId { get; set; }
        public string PhysicalDescription { get; set; }
        public string EdidMonitorName { get; set; }
        public string EdidSerialNumber { get; set; }
        public string ManufacturerCode { get; set; }
        public bool IsPrimary { get; set; }

        public MonitorCapabilities Capabilities { get; set; }
        public byte PreferredInputVcpCode { get; set; }
        public bool SupportsInputSelect { get; set; }
        public bool SupportsBrightness { get; set; }
        public bool SupportsVolume { get; set; }
        public bool SupportsAudioMute { get; set; }

        private Action<IntPtr> _destroyAction;
        private bool _disposed;

        public MonitorDeviceRecord(Action<IntPtr> destroyAction)
        {
            _destroyAction = destroyAction;
            PhysicalMonitorHandle = new IntPtr(-1);
            HasPhysicalHandle = false;
            GdiDisplayName = string.Empty;
            MonitorDeviceName = string.Empty;
            AdapterName = string.Empty;
            MonitorId = string.Empty;
            ShortMonitorId = string.Empty;
            PhysicalDescription = string.Empty;
            EdidMonitorName = string.Empty;
            EdidSerialNumber = string.Empty;
            ManufacturerCode = string.Empty;
            PreferredInputVcpCode = 0x60;
        }

        public string FriendlyTitle
        {
            get
            {
                string baseName = !string.IsNullOrEmpty(EdidMonitorName)
                    ? EdidMonitorName
                    : (!string.IsNullOrEmpty(PhysicalDescription) ? PhysicalDescription : ShortMonitorId);
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = !string.IsNullOrEmpty(MonitorDeviceName) ? MonitorDeviceName : "External Display";
                }
                if (!string.IsNullOrEmpty(EdidSerialNumber))
                {
                    return string.Format("{0} ({1})", baseName, EdidSerialNumber);
                }
                return baseName;
            }
        }

        public string DropdownLabel
        {
            get
            {
                string disp = !string.IsNullOrEmpty(GdiDisplayName) ? GdiDisplayName.Replace("\\\\.\\", "") : "";
                if (!string.IsNullOrEmpty(disp))
                {
                    return string.Format("{0}  •  {1}", FriendlyTitle, disp);
                }
                return FriendlyTitle;
            }
        }

        public string StableSelectionKey
        {
            get
            {
                if (!string.IsNullOrEmpty(EdidSerialNumber))
                {
                    return "SN:" + EdidSerialNumber;
                }
                if (!string.IsNullOrEmpty(MonitorDeviceName) && !string.IsNullOrEmpty(ShortMonitorId))
                {
                    return "ID:" + ShortMonitorId + "@" + MonitorDeviceName;
                }
                if (!string.IsNullOrEmpty(MonitorDeviceName))
                {
                    return "DEV:" + MonitorDeviceName;
                }
                if (!string.IsNullOrEmpty(ShortMonitorId))
                {
                    return "ID:" + ShortMonitorId;
                }
                return "HMON:" + HMonitor.ToInt64().ToString();
            }
        }

        public bool MatchesQuery(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }
            string s = selector.Trim();
            if (string.Equals(s, StableSelectionKey, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "Primary", StringComparison.OrdinalIgnoreCase)) return IsPrimary;
            if (string.Equals(s, "Secondary", StringComparison.OrdinalIgnoreCase)) return !IsPrimary;
            if (!string.IsNullOrEmpty(MonitorDeviceName) && string.Equals(s, MonitorDeviceName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(EdidMonitorName) && string.Equals(s, EdidMonitorName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(EdidSerialNumber) && string.Equals(s, EdidSerialNumber, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(AdapterName) && string.Equals(s, AdapterName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(MonitorId) && string.Equals(s, MonitorId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(ShortMonitorId) && string.Equals(s, ShortMonitorId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(GdiDisplayName) && string.Equals(s, GdiDisplayName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Note: Windows Dxva2.dll uses 0-based table indices (0x0, 0x1, ...) as PHYSICAL_MONITOR handles,
            // so IntPtr.Zero IS a valid handle when HasPhysicalHandle is true.
            if (HasPhysicalHandle)
            {
                HasPhysicalHandle = false;
                IntPtr h = PhysicalMonitorHandle;
                PhysicalMonitorHandle = new IntPtr(-1);
                if (_destroyAction != null)
                {
                    try
                    {
                        _destroyAction(h);
                    }
                    catch
                    {
                        // Never throw during physical monitor cleanup.
                    }
                }
            }
        }
    }

    public sealed class MonitorLiveSnapshot
    {
        public byte ActiveVcpCode { get; set; }
        public uint? CurrentInputValue { get; set; }
        public uint? RawInputValue { get; set; }
        public uint? MaxInputValue { get; set; }
        public uint? Brightness { get; set; }
        public uint? BrightnessMax { get; set; }
        public uint? Volume { get; set; }
        public uint? VolumeMax { get; set; }
        public bool? IsAudioMuted { get; set; }
        public List<byte> PossibleInputs { get; set; }
        public long ReadLatencyMs { get; set; }
        public string StatusSummary { get; set; }

        public MonitorLiveSnapshot()
        {
            ActiveVcpCode = 0x60;
            PossibleInputs = new List<byte>();
            StatusSummary = string.Empty;
        }
    }

    public sealed class AppConfig
    {
        public string SelectedMonitorKey { get; set; }
        public Dictionary<string, MonitorInputProfile> MonitorProfiles { get; set; }
        public bool UseOneByteInputMask { get; set; }
        public bool RetryOnI2cFailure { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool QuickDrawerExpanded { get; set; }
        public bool HotkeysEnabled { get; set; }
        public string HdmiHotkey { get; set; }
        public string DpHotkey { get; set; }
        public string ToggleHotkey { get; set; }

        public AppConfig()
        {
            SelectedMonitorKey = string.Empty;
            MonitorProfiles = new Dictionary<string, MonitorInputProfile>(StringComparer.OrdinalIgnoreCase);
            UseOneByteInputMask = true;
            RetryOnI2cFailure = true;
            AlwaysOnTop = false;
            MinimizeToTrayOnClose = true;
            QuickDrawerExpanded = false;
            HotkeysEnabled = true;
            HdmiHotkey = "Ctrl+Alt+1";
            DpHotkey = "Ctrl+Alt+2";
            ToggleHotkey = "Ctrl+Alt+S";
        }
    }
}
