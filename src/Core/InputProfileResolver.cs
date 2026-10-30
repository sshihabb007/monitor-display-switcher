using System;
using System.Collections.Generic;

namespace DisplayInputSwitcher.Core
{
    /// <summary>
    /// Universal monitor input auto-resolver across VESA MCCS 2.x/3.0, LG Compact (1/3),
    /// LG Gen5 OSD (144/145/208/209), and arbitrary vendor discrete capability tables.
    /// Honors explicit per-monitor user overrides when present.
    /// </summary>
    public static class InputProfileResolver
    {
        public static ResolvedMonitorInputMap Resolve(
            string manufacturerCode,
            byte preferredVcpCode,
            List<byte> capabilityDiscreteValues,
            uint? liveCurrentValue,
            uint? liveMaxValue,
            MonitorInputProfile savedProfile)
        {
            ResolvedMonitorInputMap map = new ResolvedMonitorInputMap();
            byte vcp = (savedProfile != null && savedProfile.IsUserCustomized && savedProfile.InputVcpCode != 0)
                ? savedProfile.InputVcpCode
                : (preferredVcpCode != 0 ? preferredVcpCode : (byte)0x60);
            map.InputVcpCode = vcp;

            List<byte> discrete = capabilityDiscreteValues ?? new List<byte>();
            string mfg = (manufacturerCode ?? string.Empty).Trim().ToUpperInvariant();

            bool isLgGen5 = ContainsAny(discrete, 144, 145, 208, 209) ||
                            (liveCurrentValue.HasValue && IsLgGen5Code(liveCurrentValue.Value));

            bool isStandardVesa = !isLgGen5 && (
                ContainsAny(discrete, 15, 16, 17, 18, 19, 27) ||
                (discrete.Count == 0 && liveCurrentValue.HasValue && liveCurrentValue.Value >= 15 && liveCurrentValue.Value <= 30) ||
                (discrete.Count == 0 && liveMaxValue.HasValue && liveMaxValue.Value >= 15 && liveMaxValue.Value <= 30));

            bool isLgCompact = !isLgGen5 && !isStandardVesa && (
                (mfg == "GSM" && (discrete.Count == 0 || MaxByte(discrete) <= 12)) ||
                (discrete.Count == 2 && discrete[0] == 1 && discrete[1] == 3) ||
                (discrete.Count == 0 && liveMaxValue.HasValue && liveMaxValue.Value > 0 && liveMaxValue.Value <= 4));

            // 1. Build labeled DetectedOptions for every hardware-reported discrete value
            for (int i = 0; i < discrete.Count; i++)
            {
                byte code = discrete[i];
                DetectedInputOption opt = ClassifySingleCode(code, isLgGen5, isStandardVesa, isLgCompact, mfg);
                map.DetectedOptions.Add(opt);
            }

            // 2. Determine automatic HDMI & DisplayPort slot defaults from hardware scheme
            if (isLgGen5)
            {
                map.HdmiValue = PickBestFromFamily(map.DetectedOptions, true, liveCurrentValue, 144u);
                map.DpValue = PickBestFromFamily(map.DetectedOptions, false, liveCurrentValue, 208u);
                map.HdmiLabel = LabelForSelectedCode(map.DetectedOptions, map.HdmiValue, "HDMI");
                map.DpLabel = LabelForSelectedCode(map.DetectedOptions, map.DpValue, "DisplayPort");
                map.DetectionSummary = string.Format("Auto • LG Extended (VCP {0:X2})", vcp);
            }
            else if (isStandardVesa)
            {
                map.HdmiValue = PickBestFromFamily(map.DetectedOptions, true, liveCurrentValue, 17u);
                map.DpValue = PickBestFromFamily(map.DetectedOptions, false, liveCurrentValue, 15u);
                map.HdmiLabel = LabelForSelectedCode(map.DetectedOptions, map.HdmiValue, "HDMI");
                map.DpLabel = LabelForSelectedCode(map.DetectedOptions, map.DpValue, "DisplayPort");
                map.DetectionSummary = string.Format("Auto • VESA Standard [{0}, {1}]", map.HdmiValue, map.DpValue);
            }
            else if (isLgCompact)
            {
                uint defH = (discrete.Count >= 1) ? discrete[0] : 1u;
                uint defD = (discrete.Count >= 2) ? discrete[1] : 3u;
                if (discrete.Contains(1) && discrete.Contains(3))
                {
                    defH = 1u;
                    defD = 3u;
                }
                map.HdmiValue = defH;
                map.DpValue = defD;
                map.HdmiLabel = "HDMI";
                map.DpLabel = "DisplayPort";
                map.DetectionSummary = string.Format("Auto • Compact DDC/CI [{0}, {1}]", map.HdmiValue, map.DpValue);
            }
            else if (discrete.Count >= 2)
            {
                // Unknown vendor compact discrete table (e.g., [2, 4] or [5, 6]): bind directly to real hardware values!
                map.HdmiValue = discrete[0];
                map.DpValue = discrete[1];
                map.HdmiLabel = "HDMI / Port 1";
                map.DpLabel = "DP / Port 2";
                map.DetectionSummary = string.Format("Auto • Monitor Capabilities [{0}, {1}]", map.HdmiValue, map.DpValue);
            }
            else
            {
                // Zero capabilities string and unprobed range: default to VESA MCCS 17/15
                map.HdmiValue = 17u;
                map.DpValue = 15u;
                map.HdmiLabel = "HDMI";
                map.DpLabel = "DisplayPort";
                map.DetectionSummary = "Auto • Default VESA [17, 15]";
            }

            // 3. Apply explicit user-saved per-monitor customization on top if present
            if (savedProfile != null && savedProfile.IsUserCustomized)
            {
                map.HdmiValue = savedProfile.HdmiValue;
                map.DpValue = savedProfile.DpValue;
                if (!string.IsNullOrWhiteSpace(savedProfile.HdmiLabel))
                {
                    map.HdmiLabel = savedProfile.HdmiLabel;
                }
                if (!string.IsNullOrWhiteSpace(savedProfile.DpLabel))
                {
                    map.DpLabel = savedProfile.DpLabel;
                }
                map.IsUserCustomized = true;
                map.DetectionSummary = string.Format("Saved Monitor Profile [{0}, {1}]", map.HdmiValue, map.DpValue);
            }

            return map;
        }

        private static DetectedInputOption ClassifySingleCode(
            byte code,
            bool isLgGen5,
            bool isStandardVesa,
            bool isLgCompact,
            string mfg)
        {
            DetectedInputOption opt = new DetectedInputOption();
            opt.Code = code;

            if (isLgGen5)
            {
                switch (code)
                {
                    case 144: opt.FriendlyName = "HDMI 1"; opt.IsHdmiFamily = true; break;
                    case 145: opt.FriendlyName = "HDMI 2"; opt.IsHdmiFamily = true; break;
                    case 146: opt.FriendlyName = "HDMI 3"; opt.IsHdmiFamily = true; break;
                    case 208: opt.FriendlyName = "DisplayPort"; opt.IsDpFamily = true; break;
                    case 209: opt.FriendlyName = "USB-C / DP 2"; opt.IsDpFamily = true; break;
                    default: opt.FriendlyName = "Input " + code; break;
                }
            }
            else if (isStandardVesa)
            {
                switch (code)
                {
                    case 1: opt.FriendlyName = "VGA 1"; break;
                    case 2: opt.FriendlyName = "VGA 2"; break;
                    case 3: opt.FriendlyName = "DVI 1"; break;
                    case 4: opt.FriendlyName = "DVI 2"; break;
                    case 15: opt.FriendlyName = "DisplayPort 1"; opt.IsDpFamily = true; break;
                    case 16: opt.FriendlyName = "DisplayPort 2"; opt.IsDpFamily = true; break;
                    case 17: opt.FriendlyName = "HDMI 1"; opt.IsHdmiFamily = true; break;
                    case 18: opt.FriendlyName = "HDMI 2"; opt.IsHdmiFamily = true; break;
                    case 19: opt.FriendlyName = "HDMI 3"; opt.IsHdmiFamily = true; break;
                    case 27: opt.FriendlyName = "USB-C"; opt.IsDpFamily = true; break;
                    default: opt.FriendlyName = "Input " + code; break;
                }
            }
            else if (isLgCompact)
            {
                if (code == 1) { opt.FriendlyName = "HDMI"; opt.IsHdmiFamily = true; }
                else if (code == 2) { opt.FriendlyName = (mfg == "GSM") ? "HDMI 2" : "Input 2"; opt.IsHdmiFamily = (mfg == "GSM"); }
                else if (code == 3) { opt.FriendlyName = "DisplayPort"; opt.IsDpFamily = true; }
                else if (code == 4) { opt.FriendlyName = "USB-C / Port 4"; opt.IsDpFamily = true; }
                else { opt.FriendlyName = "Input " + code; }
            }
            else
            {
                opt.FriendlyName = "Input " + code;
            }

            opt.ShortChipLabel = string.Format("{0} ({1})", opt.FriendlyName, code);
            return opt;
        }

        private static uint PickBestFromFamily(
            List<DetectedInputOption> options,
            bool wantHdmiFamily,
            uint? liveCurrent,
            uint fallbackCode)
        {
            // If the monitor is currently active on one of the ports in this family (e.g. HDMI 2 = 18 when HDMI 1 isn't used), keep that active variant ready or pick primary!
            // Actually: picking primary (HDMI 1 = 17, DP 1 = 15) unless active is in the same family and primary isn't listed, OR if active IS HDMI 2 (18), binding HDMI card to 18 means HDMI card immediately glows ACTIVE!
            if (liveCurrent.HasValue)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    DetectedInputOption o = options[i];
                    if (o.Code == liveCurrent.Value && (wantHdmiFamily ? o.IsHdmiFamily : o.IsDpFamily))
                    {
                        return o.Code;
                    }
                }
            }

            for (int i = 0; i < options.Count; i++)
            {
                DetectedInputOption o = options[i];
                if (wantHdmiFamily ? o.IsHdmiFamily : o.IsDpFamily)
                {
                    return o.Code;
                }
            }

            return fallbackCode;
        }

        private static string LabelForSelectedCode(List<DetectedInputOption> options, uint code, string defaultTitle)
        {
            int familyCount = 0;
            DetectedInputOption matched = null;
            for (int i = 0; i < options.Count; i++)
            {
                DetectedInputOption o = options[i];
                if (o.Code == code) matched = o;
            }

            if (matched == null) return defaultTitle;

            for (int i = 0; i < options.Count; i++)
            {
                if (matched.IsHdmiFamily && options[i].IsHdmiFamily) familyCount++;
                else if (matched.IsDpFamily && options[i].IsDpFamily) familyCount++;
            }

            // If monitor only has 1 HDMI port and 1 DP port, clean labels "HDMI" and "DisplayPort" look sleekest!
            if (familyCount <= 1)
            {
                if (matched.IsHdmiFamily) return "HDMI";
                if (matched.IsDpFamily && matched.Code != 27) return "DisplayPort";
            }

            return matched.FriendlyName;
        }

        private static bool ContainsAny(List<byte> list, params byte[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (list.Contains(targets[i])) return true;
            }
            return false;
        }

        private static bool IsLgGen5Code(uint v)
        {
            return v == 144u || v == 145u || v == 146u || v == 208u || v == 209u;
        }

        private static byte MaxByte(List<byte> list)
        {
            byte m = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] > m) m = list[i];
            }
            return m;
        }
    }
}

