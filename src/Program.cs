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
[assembly: AssemblyDescription("Universal Portable DDC/CI Monitor HDMI & DisplayPort Switcher")]
[assembly: AssemblyCompany("Shihab")]
[assembly: AssemblyProduct("Display Input Switcher")]
[assembly: AssemblyCopyright("Copyright © 2026 Shihab")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
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
                disc.RefreshMonitors(true);
                MonitorDeviceRecord target = ddc.ResolveTargetMonitor(cfg.SelectedMonitorKey);
                if (target == null)
                {
                    WriteReport(outFile, "ERROR: No DDC/CI monitor found.");
                    return 2;
                }

                MonitorInputProfile saved = ConfigService.GetProfileForMonitor(cfg, target.StableSelectionKey);
                byte? customVcp = (saved != null && saved.IsUserCustomized) ? (byte?)saved.InputVcpCode : null;
                byte vcpQuery = customVcp.HasValue ? customVcp.Value : target.PreferredInputVcpCode;

                List<byte> discrete = new List<byte>();
                if (target.Capabilities != null)
                {
                    discrete = target.Capabilities.GetValuesForVcp(vcpQuery);
                    if (discrete.Count == 0)
                    {
                        discrete = target.Capabilities.GetInputSelectValues();
                    }
                }

                if (doStatus)
                {
                    DdcResult<MonitorLiveSnapshot> snap = ddc.ReadLiveSnapshotAsync(
                        target.StableSelectionKey,
                        customVcp,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure,
                        true).GetAwaiter().GetResult();

                    if (!snap.IsSuccess)
                    {
                        WriteReport(outFile, "ERROR: " + snap.ErrorMessage);
                        return 1;
                    }

                    ResolvedMonitorInputMap statusMap = InputProfileResolver.Resolve(
                        target.ManufacturerCode,
                        target.PreferredInputVcpCode,
                        discrete,
                        snap.Value.CurrentInputValue,
                        snap.Value.MaxInputValue,
                        saved);

                    WriteReport(
                        outFile,
                        string.Format(
                            "Monitor={0} | Device={1} | GPU={2} | Scheme={3} | VCP=0x{4:X2} | Input={5} (Raw=0x{6:X4}) | Mapped=[HDMI={7}, DP={8}] | Brightness={9} | Volume={10} | Latency={11}ms",
                            target.FriendlyTitle,
                            target.MonitorDeviceName,
                            target.AdapterName,
                            statusMap.DetectionSummary,
                            statusMap.InputVcpCode,
                            snap.Value.CurrentInputValue,
                            snap.Value.RawInputValue ?? 0u,
                            statusMap.HdmiValue,
                            statusMap.DpValue,
                            snap.Value.Brightness.HasValue ? snap.Value.Brightness.Value.ToString() : "N/A",
                            snap.Value.Volume.HasValue ? snap.Value.Volume.Value.ToString() : "N/A",
                            snap.Value.ReadLatencyMs));
                    return 0;
                }

                uint? liveCur = null;
                uint? liveMax = null;
                if (discrete.Count == 0 && (saved == null || !saved.IsUserCustomized))
                {
                    DdcResult<MonitorLiveSnapshot> preSnap = ddc.ReadLiveSnapshotAsync(
                        target.StableSelectionKey,
                        customVcp,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure,
                        false).GetAwaiter().GetResult();
                    if (preSnap.IsSuccess)
                    {
                        liveCur = preSnap.Value.CurrentInputValue;
                        liveMax = preSnap.Value.MaxInputValue;
                    }
                }

                ResolvedMonitorInputMap map = InputProfileResolver.Resolve(
                    target.ManufacturerCode,
                    target.PreferredInputVcpCode,
                    discrete,
                    liveCur,
                    liveMax,
                    saved);

                DdcResult<uint> swRes;
                if (doHdmi)
                {
                    swRes = ddc.SetInputSourceAsync(
                        target.StableSelectionKey,
                        map.InputVcpCode,
                        map.HdmiValue,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }
                else if (doDp)
                {
                    swRes = ddc.SetInputSourceAsync(
                        target.StableSelectionKey,
                        map.InputVcpCode,
                        map.DpValue,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }
                else
                {
                    swRes = ddc.ToggleInputSourceAsync(
                        target.StableSelectionKey,
                        map.InputVcpCode,
                        map.HdmiValue,
                        map.DpValue,
                        liveCur,
                        cfg.UseOneByteInputMask,
                        cfg.RetryOnI2cFailure).GetAwaiter().GetResult();
                }

                if (!swRes.IsSuccess)
                {
                    WriteReport(outFile, "ERROR: " + swRes.ErrorMessage);
                    return 1;
                }

                WriteReport(outFile, string.Format("OK: VCP=0x{0:X2} Input={1} ({2}ms)", map.InputVcpCode, swRes.Value, swRes.ElapsedMs));
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

            string lgGen5Mccs = "(prot(monitor)type(lcd)model(27GP850)vcp(10 12 F4(90 91 D0 D1))mccs_ver(2.1))";
            MonitorCapabilities capsGen5 = MccsCapabilitiesParser.Parse(lgGen5Mccs);
            List<byte> f4Vals = capsGen5.GetValuesForVcp(0xF4);
            check("MCCS LG Gen5 VCP F4 discrete values [144, 145, 208, 209]",
                f4Vals.Count == 4 && f4Vals[0] == 144 && f4Vals[1] == 145 && f4Vals[2] == 208 && f4Vals[3] == 209);

            // 3. VCP 0x60 / 0xF4 1-byte low-byte normalization regression tests
            VcpFeatureReply masked60 = DdcCiService.NormalizeVcpReply(0x60, 0, 0x0F01u, 0x0F03u, true);
            check("VCP 0x60 high-byte status 0x0F01 normalized to 1 while preserving raw 0x0F01",
                masked60.NormalizedCurrentValue == 1u &&
                masked60.NormalizedMaxValue == 3u &&
                masked60.RawCurrentValue == 0x0F01u &&
                masked60.WasOneByteMasked);

            VcpFeatureReply maskedF4 = DdcCiService.NormalizeVcpReply(0xF4, 0, 0x0190u, 0x01D1u, true);
            check("VCP 0xF4 high-byte status 0x0190 normalized to 144 (0x90)",
                maskedF4.NormalizedCurrentValue == 144u &&
                maskedF4.NormalizedMaxValue == 209u &&
                maskedF4.WasOneByteMasked);

            VcpFeatureReply unmaskedOther = DdcCiService.NormalizeVcpReply(0xC0, 0, 3096u, 65535u, true);
            check("Non-input VCP codes (e.g. 0xC0 Usage Time 3096) are never 1-byte truncated",
                unmaskedOther.NormalizedCurrentValue == 3096u && !unmaskedOther.WasOneByteMasked);

            // 4. Universal InputProfileResolver multi-vendor auto-detection tests
            ResolvedMonitorInputMap mapLg = InputProfileResolver.Resolve("GSM", 0x60, new List<byte> { 1, 3 }, 1u, 3u, null);
            check("Resolver LG Compact [1, 3] -> HDMI=1, DP=3",
                mapLg.InputVcpCode == 0x60 && mapLg.HdmiValue == 1u && mapLg.DpValue == 3u && mapLg.DetectedOptions.Count == 2);

            ResolvedMonitorInputMap mapDell = InputProfileResolver.Resolve("DEL", 0x60, new List<byte> { 15, 17, 18 }, 15u, 18u, null);
            check("Resolver VESA Standard (Dell/ASUS/MSI/BenQ) [15, 17, 18] -> HDMI=17, DP=15, 3 ports",
                mapDell.InputVcpCode == 0x60 && mapDell.HdmiValue == 17u && mapDell.DpValue == 15u && mapDell.DetectedOptions.Count == 3);

            ResolvedMonitorInputMap mapUsbC = InputProfileResolver.Resolve("AUS", 0x60, new List<byte> { 15, 17, 18, 27 }, 18u, 27u, null);
            check("Resolver VESA 4-Port USB-C [15, 17, 18, 27] with active HDMI 2 (18) -> HDMI=18, DP=15, 4 ports",
                mapUsbC.HdmiValue == 18u && mapUsbC.DpValue == 15u && mapUsbC.DetectedOptions.Count == 4);

            ResolvedMonitorInputMap mapLgGen5 = InputProfileResolver.Resolve("GSM", 0xF4, new List<byte> { 144, 145, 208, 209 }, 144u, 209u, null);
            check("Resolver LG Gen5 VCP F4 [144, 145, 208, 209] -> VCP=0xF4, HDMI=144, DP=208",
                mapLgGen5.InputVcpCode == 0xF4 && mapLgGen5.HdmiValue == 144u && mapLgGen5.DpValue == 208u);

            ResolvedMonitorInputMap mapCustomVendor = InputProfileResolver.Resolve("UNK", 0x60, new List<byte> { 2, 4 }, 2u, 4u, null);
            check("Resolver arbitrary vendor discrete [2, 4] -> binds directly to [2, 4]",
                mapCustomVendor.HdmiValue == 2u && mapCustomVendor.DpValue == 4u);

            ResolvedMonitorInputMap mapMissingCapsVesa = InputProfileResolver.Resolve("SAM", 0x60, new List<byte>(), 17u, 18u, null);
            check("Resolver missing MCCS discrete list + live VESA cur=17 -> infers VESA [17, 15]",
                mapMissingCapsVesa.HdmiValue == 17u && mapMissingCapsVesa.DpValue == 15u);

            MonitorInputProfile customProf = new MonitorInputProfile
            {
                InputVcpCode = 0x60,
                HdmiValue = 18u,
                DpValue = 27u,
                HdmiLabel = "HDMI 2",
                DpLabel = "USB-C",
                IsUserCustomized = true
            };
            ResolvedMonitorInputMap mapCustomOverride = InputProfileResolver.Resolve("DEL", 0x60, new List<byte> { 15, 17, 18, 27 }, 18u, 27u, customProf);
            check("Resolver honors per-monitor saved user override [18, 27]",
                mapCustomOverride.IsUserCustomized && mapCustomOverride.HdmiValue == 18u && mapCustomOverride.DpValue == 27u);

            // 5. Dxva2 0-based PHYSICAL_MONITOR handle (0x0) ownership & cleanup regression test
            bool destroyedZeroHandle = false;
            IntPtr destroyedValue = new IntPtr(-1);
            using (MonitorDeviceRecord zeroHandleRec = new MonitorDeviceRecord(delegate(IntPtr h)
            {
                destroyedZeroHandle = true;
                destroyedValue = h;
            }))
            {
                zeroHandleRec.PhysicalMonitorHandle = IntPtr.Zero;
                zeroHandleRec.HasPhysicalHandle = true;
            }
            check("Dxva2 index-0 handle (0x0) is owned and destroyed on Dispose()",
                destroyedZeroHandle && destroyedValue == IntPtr.Zero);

            bool destroyedUnacquired = false;
            using (MonitorDeviceRecord unacquiredRec = new MonitorDeviceRecord(delegate(IntPtr h)
            {
                destroyedUnacquired = true;
            }))
            {
            }
            check("Unacquired MonitorDeviceRecord never invokes destroy callback", !destroyedUnacquired);

            // 6. Global hotkey parser test
            ParsedHotkey hk1, hk2, hk3;
            check("Hotkey parser Ctrl+Alt+1", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+1", out hk1) && hk1.CanonicalText == "Ctrl+Alt+1");
            check("Hotkey parser Ctrl+Alt+2", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+2", out hk2) && hk2.CanonicalText == "Ctrl+Alt+2");
            check("Hotkey parser Ctrl+Alt+S", GlobalHotkeyManager.TryParseCombo("Ctrl+Alt+S", out hk3) && hk3.CanonicalText == "Ctrl+Alt+S");

            // 6. Read-only live hardware discovery & universal auto-resolve probe (NO SetVCPFeature writes!)
            log.AppendLine("--- LIVE READ-ONLY HARDWARE PROBE ---");
            log.AppendLine(string.Format("Process64Bit={0} | OS64Bit={1}", Environment.Is64BitProcess, Environment.Is64BitOperatingSystem));
            using (MonitorDiscoveryService disc = new MonitorDiscoveryService())
            using (DdcCiService ddc = new DdcCiService(disc))
            {
                IReadOnlyList<MonitorDeviceRecord> liveMons = disc.RefreshMonitors(true);
                log.AppendLine("DiscoveredPhysicalMonitors=" + liveMons.Count);
                for (int i = 0; i < liveMons.Count; i++)
                {
                    MonitorDeviceRecord m = liveMons[i];
                    List<byte> pIn = (m.Capabilities != null) ? m.Capabilities.GetInputSelectValues() : new List<byte>();
                    log.AppendLine(string.Format(
                        "  [{0}] Title='{1}' | Device='{2}' | GPU='{3}' | Mfg='{4}' | ShortID='{5}' | SupportsInputSelect={6} | PrefVCP=0x{7:X2} | Possible=[{8}]",
                        i,
                        m.FriendlyTitle,
                        m.MonitorDeviceName,
                        m.AdapterName,
                        m.ManufacturerCode,
                        m.ShortMonitorId,
                        m.SupportsInputSelect,
                        m.PreferredInputVcpCode,
                        string.Join(",", pIn)));
                }

                MonitorDeviceRecord target = ddc.ResolveTargetMonitor(string.Empty);
                if (target != null && target.SupportsInputSelect)
                {
                    DdcResult<MonitorLiveSnapshot> snap = ddc.ReadLiveSnapshotAsync(target.StableSelectionKey, null, true, true, true).GetAwaiter().GetResult();
                    check("Live DDC/CI read on resolved external monitor succeeded", snap.IsSuccess);
                    if (snap.IsSuccess)
                    {
                        List<byte> targetDiscrete = (target.Capabilities != null) ? target.Capabilities.GetInputSelectValues() : new List<byte>();
                        ResolvedMonitorInputMap liveAutoMap = InputProfileResolver.Resolve(
                            target.ManufacturerCode,
                            target.PreferredInputVcpCode,
                            targetDiscrete,
                            snap.Value.CurrentInputValue,
                            snap.Value.MaxInputValue,
                            null);

                        log.AppendLine(string.Format(
                            "  [LIVE AUTO-RESOLVED] Target='{0}' | Scheme='{1}' | VCP=0x{2:X2} | AutoHDMI={3} ({4}) | AutoDP={5} ({6}) | LiveActive={7} (Raw=0x{8:X4}) | Brightness={9}/{10} | Volume={11}/{12} | Muted={13} | Latency={14}ms",
                            target.FriendlyTitle,
                            liveAutoMap.DetectionSummary,
                            liveAutoMap.InputVcpCode,
                            liveAutoMap.HdmiValue,
                            liveAutoMap.HdmiLabel,
                            liveAutoMap.DpValue,
                            liveAutoMap.DpLabel,
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
