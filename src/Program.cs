using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using DisplayInputSwitcher.Core;
using DisplayInputSwitcher.Services;
using DisplayInputSwitcher.UI;

[assembly: AssemblyTitle("Display Input Switcher")]
[assembly: AssemblyDescription("Modern Portable DDC/CI Monitor HDMI & DisplayPort Switcher")]
[assembly: AssemblyCompany("Shihab")]
[assembly: AssemblyProduct("Display Input Switcher")]
[assembly: AssemblyCopyright("Copyright © 2026 Shihab")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace DisplayInputSwitcher
{
    public static class Program
    {
        private const string SingleInstanceMutexName = @"Local\Shihab_DisplayInputSwitcher_SingleInstance_v1";
        private const string ActivateWindowEventName = @"Local\Shihab_DisplayInputSwitcher_ActivateEvent_v1";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                int? cliExit = TryHandleCliMode(args);
                if (cliExit.HasValue)
                {
                    return cliExit.Value;
                }
            }

            bool startMinimized = HasArg(args, "--minimized") || HasArg(args, "-minimized");

            bool createdNew = false;
            using (Mutex singleMutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Signal existing tray instance to pop open its window
                    try
                    {
                        using (EventWaitHandle evt = EventWaitHandle.OpenExisting(ActivateWindowEventName))
                        {
                            evt.Set();
                        }
                    }
                    catch
                    {
                    }
                    return 0;
                }

                EventWaitHandle activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateWindowEventName);
                RegisteredWaitHandle waitReg = null;

                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    ConfigService configService = new ConfigService();
                    MonitorDiscoveryService discovery = new MonitorDiscoveryService();
                    DdcCiService ddcService = new DdcCiService(discovery);
                    GlobalHotkeyManager hotkeyManager = new GlobalHotkeyManager();
                    TrayIconManager trayManager = new TrayIconManager();

                    MainWindow window = new MainWindow(configService, ddcService, hotkeyManager, trayManager);
                    app.MainWindow = window;

                    waitReg = ThreadPool.RegisterWaitForSingleObject(
                        activateEvent,
                        delegate(object state, bool timedOut)
                        {
                            if (!timedOut)
                            {
                                window.Dispatcher.BeginInvoke(new Action(window.RestoreAndActivateWindow));
                            }
                        },
                        null,
                        -1,
                        false);

                    window.InitializeHardwareAndHotkeys();

                    string captureDir = GetArgValue(args, "--capture-ui");
                    if (!string.IsNullOrWhiteSpace(captureDir))
                    {
                        window.Show();
                        window.RunUiPreviewCaptureAsync(captureDir);
                    }
                    else if (!startMinimized)
                    {
                        window.Show();
                    }

                    app.Run();
                    return 0;
                }
                finally
                {
                    if (waitReg != null)
                    {
                        waitReg.Unregister(null);
                    }
                    activateEvent.Dispose();
                }
            }
        }

        private static int? TryHandleCliMode(string[] args)
        {
            string outFile = GetArgValue(args, "--out");

            if (HasArg(args, "--self-test"))
            {
                return RunSelfTestAndReadOnlyHardwareProbe(outFile);
            }

            bool doHdmi = HasArg(args, "--hdmi");
            bool doDp = HasArg(args, "--dp");
            bool doToggle = HasArg(args, "--toggle");
            bool doStatus = HasArg(args, "--status");

            if (!doHdmi && !doDp && !doToggle && !doStatus)
            {
                return null;
            }

            ConfigService cfgSvc = new ConfigService();
            AppConfig cfg = cfgSvc.Load();
            using (MonitorDiscoveryService disc = new MonitorDiscoveryService())
            using (DdcCiService ddc = new DdcCiService(disc))
            {
                disc.RefreshMonitors(doStatus);
                MonitorDeviceRecord target = ddc.ResolveTargetMonitor(cfg.SelectedMonitorKey);
                if (target == null)
                {
                    WriteReport(outFile, "ERROR: No DDC/CI monitor found.");
                    return 2;
                }

                if (doStatus)
                {
                    DdcResult<MonitorLiveSnapshot> snap = ddc.ReadLiveSnapshotAsync(
                        target.StableSelectionKey,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure,
                        true).GetAwaiter().GetResult();

                    if (!snap.IsSuccess)
                    {
                        WriteReport(outFile, "ERROR: " + snap.ErrorMessage);
                        return 1;
                    }
                    WriteReport(
                        outFile,
                        string.Format(
                            "Monitor={0} | Device={1} | GPU={2} | Input={3} (Raw=0x{4:X4}) | Brightness={5} | Volume={6} | Latency={7}ms",
                            target.FriendlyTitle,
                            target.MonitorDeviceName,
                            target.AdapterName,
                            snap.Value.CurrentInputValue,
                            snap.Value.RawInputValue ?? 0u,
                            snap.Value.Brightness.HasValue ? snap.Value.Brightness.Value.ToString() : "N/A",
                            snap.Value.Volume.HasValue ? snap.Value.Volume.Value.ToString() : "N/A",
                            snap.Value.ReadLatencyMs));
                    return 0;
                }

                DdcResult<uint> swRes;
                if (doHdmi)
                {
                    swRes = ddc.SetInputSourceAsync(target.StableSelectionKey, cfg.HdmiValue, cfg.UseOneByteInputMask, cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }
                else if (doDp)
                {
                    swRes = ddc.SetInputSourceAsync(target.StableSelectionKey, cfg.DpValue, cfg.UseOneByteInputMask, cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }
                else
                {
                    swRes = ddc.ToggleInputSourceAsync(target.StableSelectionKey, cfg.HdmiValue, cfg.DpValue, null, cfg.UseOneByteInputMask, cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }

                if (!swRes.IsSuccess)
                {
                    WriteReport(outFile, "ERROR: " + swRes.ErrorMessage);
                    return 1;
                }

                WriteReport(outFile, string.Format("OK: Input={0} ({1}ms)", swRes.Value, swRes.ElapsedMs));
                return 0;
            }
        }

        private static int RunSelfTestAndReadOnlyHardwareProbe(string outFile)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            Action<string, bool> check = delegate(string name, bool cond)
            {
                if (cond)
                {
                    log.AppendLine("[PASS] " + name);
                }
                else
                {
                    failures++;
                    log.AppendLine("[FAIL] " + name);
                }
            };

            // 1. VESA EDID parser test (synthetic LG ULTRAGEAR 303NTC26U243 128-byte block)
            byte[] edid = new byte[128];
            edid[0] = 0x00; edid[1] = 0xFF; edid[2] = 0xFF; edid[3] = 0xFF;
            edid[4] = 0xFF; edid[5] = 0xFF; edid[6] = 0xFF; edid[7] = 0x00;
            // GSM = ('G'-'@'=7, 'S'-'@'=19, 'M'-'@'=13) => (7<<10)|(19<<5)|13 = 0x1E6D
            edid[8] = 0x1E; edid[9] = 0x6D;
            // Descriptor 1 at 54: 0xFC Monitor Name "LG ULTRAGEAR\n"
            edid[54] = 0; edid[55] = 0; edid[56] = 0; edid[57] = 0xFC; edid[58] = 0;
            byte[] nameBytes = Encoding.ASCII.GetBytes("LG ULTRAGEAR\n");
            Array.Copy(nameBytes, 0, edid, 59, nameBytes.Length);
            // Descriptor 2 at 72: 0xFF Serial "303NTC26U243\n"
            edid[72] = 0; edid[73] = 0; edid[74] = 0; edid[75] = 0xFF; edid[76] = 0;
            byte[] serBytes = Encoding.ASCII.GetBytes("303NTC26U243\n");
            Array.Copy(serBytes, 0, edid, 77, serBytes.Length);

            EdidMetadata parsedEdid = EdidParser.Parse(edid);
            check("EDID header + GSM manufacturer parse", parsedEdid.IsValid && parsedEdid.ManufacturerCode == "GSM");
            check("EDID 0xFC MonitorName == LG ULTRAGEAR", parsedEdid.MonitorName == "LG ULTRAGEAR");
            check("EDID 0xFF SerialNumber == 303NTC26U243", parsedEdid.SerialNumber == "303NTC26U243");

            // 2. MCCS Capabilities vcp(...) discrete parser test
            string sampleMccs = "(prot(monitor)type(lcd)model(LG ULTRAGEAR)vcp(02 10 12 14(01 04 05 06 07 08 0A 0B) 60(01 03) 62 8D D6(01 04) DF)mccs_ver(2.1))";
            MonitorCapabilities caps = MccsCapabilitiesParser.Parse(sampleMccs);
            List<byte> inVals = caps.GetInputSelectValues();
            check("MCCS capabilities model & version", caps.ModelFromCapabilities == "LG ULTRAGEAR" && caps.MccsVersion == "2.1");
            check("MCCS VCP 60 discrete values [1, 3]", inVals.Count == 2 && inVals[0] == 1 && inVals[1] == 3);
            check("MCCS VCP 10/62/8D presence", caps.SupportedVcpCodes.Contains(0x10) && caps.SupportedVcpCodes.Contains(0x62) && caps.SupportedVcpCodes.Contains(0x8D));

            // 3. VCP 0x60 1-byte low-byte normalization regression test (fixes ControlMyMonitor /SwitchValue 0x0040c3ef high-byte bug)
            VcpFeatureReply masked = DdcCiService.NormalizeVcpReply(0x60, 0, 0x0F01u, 0x0F03u, true);
            check("VCP 0x60 high-byte status 0x0F01 normalized to 1 while preserving raw 0x0F01",
                masked.NormalizedCurrentValue == 1u &&
                masked.NormalizedMaxValue == 3u &&
                masked.RawCurrentValue == 0x0F01u &&
                masked.WasOneByteMasked);

            VcpFeatureReply unmaskedOther = DdcCiService.NormalizeVcpReply(0xC0, 0, 3096u, 65535u, true);
            check("Non-0x60 VCP codes (e.g. 0xC0 Usage Time 3096) are never 1-byte truncated",
                unmaskedOther.NormalizedCurrentValue == 3096u && !unmaskedOther.WasOneByteMasked);

            // 4. Global hotkey parser test
            ParsedHotkey hk1, hk2, hk3;
            check("Hotkey parser Ctrl+Alt+1", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+1", out hk1) && hk1.CanonicalText == "Ctrl+Alt+1");
            check("Hotkey parser Ctrl+Alt+2", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+2", out hk2) && hk2.CanonicalText == "Ctrl+Alt+2");
            check("Hotkey parser Ctrl+Alt+S", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+S", out hk3) && hk3.CanonicalText == "Ctrl+Alt+S");

            // 5. Read-only live hardware discovery & VCP 0x60 probe (NO SetVCPFeature writes!)
            log.AppendLine("--- LIVE READ-ONLY HARDWARE PROBE ---");
            using (MonitorDiscoveryService disc = new MonitorDiscoveryService())
            using (DdcCiService ddc = new DdcCiService(disc))
            {
                IReadOnlyList<MonitorDeviceRecord> liveMons = disc.RefreshMonitors(true);
                log.AppendLine("DiscoveredPhysicalMonitors=" + liveMons.Count);
                for (int i = 0; i < liveMons.Count; i++)
                {
                    MonitorDeviceRecord m = liveMons[i];
                    List<byte> p60 = (m.Capabilities != null) ? m.Capabilities.GetInputSelectValues() : new List<byte>();
                    log.AppendLine(string.Format(
                        "  [{0}] Title='{1}' | Device='{2}' | GPU='{3}' | ShortID='{4}' | SupportsInputSelect={5} | Possible60=[{6}]",
                        i,
                        m.FriendlyTitle,
                        m.MonitorDeviceName,
                        m.AdapterName,
                        m.ShortMonitorId,
                        m.SupportsInputSelect,
                        string.Join(",", p60)));
                }

                MonitorDeviceRecord target = ddc.ResolveTargetMonitor(string.Empty);
                if (target != null && target.SupportsInputSelect)
                {
                    DdcResult<MonitorLiveSnapshot> snap = ddc.ReadLiveSnapshotAsync(target.StableSelectionKey, true, true, true).GetAwaiter().GetResult();
                    check("Live DDC/CI read on resolved external monitor succeeded", snap.IsSuccess);
                    if (snap.IsSuccess)
                    {
                        log.AppendLine(string.Format(
                            "  [LIVE SNAPSHOT] Target='{0}' | VCP60_Current={1} (Raw=0x{2:X4}) | Brightness={3}/{4} | Volume={5}/{6} | Muted={7} | Latency={8}ms",
                            target.FriendlyTitle,
                            snap.Value.CurrentInputValue,
                            snap.Value.RawInputValue ?? 0u,
                            snap.Value.Brightness,
                            snap.Value.BrightnessMax,
                            snap.Value.Volume,
                            snap.Value.VolumeMax,
                            snap.Value.IsAudioMuted,
                            snap.Value.ReadLatencyMs));
                    }
                    else
                    {
                        log.AppendLine("  [LIVE SNAPSHOT WARN] " + snap.ErrorMessage);
                    }
                }
            }

            WriteReport(outFile, log.ToString());
            return failures == 0 ? 0 : 1;
        }

        private static void WriteReport(string outFile, string content)
        {
            if (!string.IsNullOrWhiteSpace(outFile))
            {
                string dir = Path.GetDirectoryName(outFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(outFile, content, new UTF8Encoding(false));
            }
            else
            {
                try { Console.WriteLine(content); } catch { }
            }
        }

        private static bool HasArg(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetArgValue(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
