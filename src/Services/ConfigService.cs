using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using DisplayInputSwitcher.Core;
using Microsoft.Win32;

namespace DisplayInputSwitcher.Services
{
    public sealed class ConfigService
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "DisplayInputSwitcher";

        private readonly string _configFilePath;
        private readonly JavaScriptSerializer _serializer;

        public ConfigService()
        {
            string exePath = Process.GetCurrentProcess().MainModule != null
                ? Process.GetCurrentProcess().MainModule.FileName
                : AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string baseName = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "DisplayInputSwitcher";
            }

            string candidatePath = Path.Combine(dir, baseName + ".config.json");
            if (IsDirectoryWritable(dir))
            {
                _configFilePath = candidatePath;
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string fallbackDir = Path.Combine(localAppData, "MonitorDisplaySwitcher");
                try { Directory.CreateDirectory(fallbackDir); } catch { }
                _configFilePath = Path.Combine(fallbackDir, baseName + ".config.json");
            }
            _serializer = new JavaScriptSerializer();
        }

        private static bool IsDirectoryWritable(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) return false;
            try
            {
                string probe = Path.Combine(dirPath, ".dis_write_probe_" + Process.GetCurrentProcess().Id + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string ConfigFilePath
        {
            get { return _configFilePath; }
        }

        public AppConfig Load()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    return Sanitize(new AppConfig());
                }

                string json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Sanitize(new AppConfig());
                }

                AppConfig loaded = _serializer.Deserialize<AppConfig>(json);
                return Sanitize(loaded ?? new AppConfig());
            }
            catch
            {
                return Sanitize(new AppConfig());
            }
        }

        public bool Save(AppConfig config)
        {
            if (config == null) return false;
            try
            {
                AppConfig clean = Sanitize(config);
                string rawJson = FormatPrettyJson(_serializer.Serialize(clean));
                string tempPath = _configFilePath + ".tmp";
                File.WriteAllText(tempPath, rawJson, new UTF8Encoding(false));
                if (File.Exists(_configFilePath))
                {
                    File.Delete(_configFilePath);
                }
                File.Move(tempPath, _configFilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static MonitorInputProfile GetProfileForMonitor(AppConfig cfg, string monitorKey)
        {
            if (cfg == null || cfg.MonitorProfiles == null || string.IsNullOrWhiteSpace(monitorKey))
            {
                return null;
            }
            MonitorInputProfile prof;
            if (cfg.MonitorProfiles.TryGetValue(monitorKey, out prof))
            {
                return prof;
            }
            return null;
        }

        public static void SaveCustomProfileForMonitor(
            AppConfig cfg,
            string monitorKey,
            byte inputVcpCode,
            uint hdmiValue,
            uint dpValue,
            string hdmiLabel,
            string dpLabel)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(monitorKey)) return;
            if (cfg.MonitorProfiles == null)
            {
                cfg.MonitorProfiles = new Dictionary<string, MonitorInputProfile>(StringComparer.OrdinalIgnoreCase);
            }

            MonitorInputProfile prof = new MonitorInputProfile();
            prof.InputVcpCode = inputVcpCode != 0 ? inputVcpCode : (byte)0x60;
            prof.HdmiValue = hdmiValue;
            prof.DpValue = dpValue;
            prof.HdmiLabel = string.IsNullOrWhiteSpace(hdmiLabel) ? "HDMI" : hdmiLabel.Trim();
            prof.DpLabel = string.IsNullOrWhiteSpace(dpLabel) ? "DisplayPort" : dpLabel.Trim();
            prof.IsUserCustomized = true;
            cfg.MonitorProfiles[monitorKey] = prof;
        }

        public static void ClearCustomProfileForMonitor(AppConfig cfg, string monitorKey)
        {
            if (cfg == null || cfg.MonitorProfiles == null || string.IsNullOrWhiteSpace(monitorKey)) return;
            cfg.MonitorProfiles.Remove(monitorKey);
        }

        public bool IsLaunchAtStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                {
                    if (key == null) return false;
                    string val = key.GetValue(StartupValueName) as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch
            {
                return false;
            }
        }

        public bool SetLaunchAtStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        string exePath = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(StartupValueName, "\"" + exePath + "\" --minimized");
                    }
                    else
                    {
                        if (key.GetValue(StartupValueName) != null)
                        {
                            key.DeleteValue(StartupValueName, false);
                        }
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static AppConfig Sanitize(AppConfig cfg)
        {
            if (cfg.SelectedMonitorKey == null) cfg.SelectedMonitorKey = string.Empty;
            if (cfg.MonitorProfiles == null)
            {
                cfg.MonitorProfiles = new Dictionary<string, MonitorInputProfile>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Dictionary<string, MonitorInputProfile> ci = new Dictionary<string, MonitorInputProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, MonitorInputProfile> kv in cfg.MonitorProfiles)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    {
                        if (kv.Value.InputVcpCode == 0) kv.Value.InputVcpCode = 0x60;
                        if (string.IsNullOrWhiteSpace(kv.Value.HdmiLabel)) kv.Value.HdmiLabel = "HDMI";
                        if (string.IsNullOrWhiteSpace(kv.Value.DpLabel)) kv.Value.DpLabel = "DisplayPort";
                        ci[kv.Key] = kv.Value;
                    }
                }
                cfg.MonitorProfiles = ci;
            }

            if (cfg.HdmiHotkey == null) cfg.HdmiHotkey = "Ctrl+Alt+1";
            if (cfg.DpHotkey == null) cfg.DpHotkey = "Ctrl+Alt+2";
            if (cfg.ToggleHotkey == null) cfg.ToggleHotkey = "Ctrl+Alt+S";
            return cfg;
        }

        private static string FormatPrettyJson(string compact)
        {
            if (string.IsNullOrEmpty(compact)) return "{}";
            StringBuilder sb = new StringBuilder(compact.Length * 2);
            int indent = 0;
            bool inQuotes = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char ch = compact[i];
                if (ch == '"' && (i == 0 || compact[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                }
                else if (!inQuotes)
                {
                    if (ch == '{' || ch == '[')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (ch == '}' || ch == ']')
                    {
                        sb.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(ch);
                    }
                    else if (ch == ',')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (ch == ':')
                    {
                        sb.Append(": ");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
