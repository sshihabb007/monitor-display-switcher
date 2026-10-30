using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DisplayInputSwitcher.Core;
using DisplayInputSwitcher.Native;

namespace DisplayInputSwitcher.Services
{
    /// <summary>
    /// Single owning DDC/CI service boundary.
    /// - Serializes all I2C commands with a semaphore so concurrent UI/tray/hotkey calls never collide on the DDC bus.
    /// - Normalizes VCP 0x60 (Input Select) low byte (& 0xFF) in one place, fixing ControlMyMonitor's /SwitchValue mismatch bug.
    /// - Retries transient I2C bus errors with a 60 ms backoff and auto-recovers stale physical monitor handles.
    /// </summary>
    public sealed class DdcCiService : IDisposable
    {
        public const byte VcpInputSelect = 0x60;
        public const byte VcpBrightness = 0x10;
        public const byte VcpSpeakerVolume = 0x62;
        public const byte VcpAudioMute = 0x8D;

        private readonly MonitorDiscoveryService _discovery;
        private readonly SemaphoreSlim _busLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public DdcCiService(MonitorDiscoveryService discovery)
        {
            if (discovery == null) throw new ArgumentNullException("discovery");
            _discovery = discovery;
        }

        public Task<IReadOnlyList<MonitorDeviceRecord>> RefreshMonitorsAsync(bool probeCapabilities)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    return _discovery.RefreshMonitors(probeCapabilities);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        public MonitorDeviceRecord ResolveTargetMonitor(string selectorKey)
        {
            IReadOnlyList<MonitorDeviceRecord> monitors = _discovery.GetCurrentMonitors();
            if (monitors.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(selectorKey))
            {
                for (int i = 0; i < monitors.Count; i++)
                {
                    if (monitors[i].MatchesQuery(selectorKey))
                    {
                        return monitors[i];
                    }
                }
            }

            // Default: pick the first monitor supporting VCP 0x60 Input Select (e.g., LG ULTRAGEAR), else index 0
            for (int i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].SupportsInputSelect)
                {
                    return monitors[i];
                }
            }

            return monitors[0];
        }

        public Task<DdcResult<VcpFeatureReply>> ReadVcpAsync(
            string monitorSelector,
            byte vcpCode,
            bool useOneByteInputMask,
            bool retryOnFailure)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    return ReadVcpSynchronized(monitorSelector, vcpCode, useOneByteInputMask, retryOnFailure);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        public Task<DdcResult<MonitorLiveSnapshot>> ReadLiveSnapshotAsync(
            string monitorSelector,
            bool useOneByteInputMask,
            bool retryOnFailure,
            bool includeQuickControls)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    MonitorDeviceRecord mon = ResolveTargetMonitor(monitorSelector);
                    if (mon == null)
                    {
                        return DdcResult<MonitorLiveSnapshot>.Fail(
                            DdcErrorKind.MonitorNotFound,
                            0,
                            "No connected DDC/CI display detected.",
                            sw.ElapsedMilliseconds);
                    }

                    MonitorLiveSnapshot snap = new MonitorLiveSnapshot();
                    if (mon.Capabilities != null)
                    {
                        snap.PossibleInputs = mon.Capabilities.GetInputSelectValues();
                    }

                    DdcResult<VcpFeatureReply> inRes = ReadVcpOnRecord(mon, VcpInputSelect, useOneByteInputMask, retryOnFailure, true);
                    if (inRes.IsSuccess)
                    {
                        snap.CurrentInputValue = inRes.Value.NormalizedCurrentValue;
                        snap.RawInputValue = inRes.Value.RawCurrentValue;
                        mon.SupportsInputSelect = true;
                    }

                    if (includeQuickControls)
                    {
                        if (mon.SupportsBrightness)
                        {
                            DdcResult<VcpFeatureReply> br = ReadVcpOnRecord(mon, VcpBrightness, false, retryOnFailure, false);
                            if (br.IsSuccess)
                            {
                                snap.Brightness = br.Value.NormalizedCurrentValue;
                                snap.BrightnessMax = br.Value.NormalizedMaxValue > 0 ? br.Value.NormalizedMaxValue : 100u;
                            }
                        }

                        if (mon.SupportsVolume)
                        {
                            DdcResult<VcpFeatureReply> vol = ReadVcpOnRecord(mon, VcpSpeakerVolume, false, retryOnFailure, false);
                            if (vol.IsSuccess)
                            {
                                snap.Volume = vol.Value.NormalizedCurrentValue;
                                snap.VolumeMax = vol.Value.NormalizedMaxValue > 0 ? vol.Value.NormalizedMaxValue : 100u;
                            }
                        }

                        if (mon.SupportsAudioMute)
                        {
                            DdcResult<VcpFeatureReply> mute = ReadVcpOnRecord(mon, VcpAudioMute, false, false, false);
                            if (mute.IsSuccess)
                            {
                                // MCCS VCP 8D: 1 = Mute, 2 = Unmute
                                snap.IsAudioMuted = ((mute.Value.RawCurrentValue & 0xFFu) == 1u);
                            }
                        }
                    }

                    sw.Stop();
                    snap.ReadLatencyMs = sw.ElapsedMilliseconds;

                    if (!inRes.IsSuccess)
                    {
                        return DdcResult<MonitorLiveSnapshot>.Fail(
                            inRes.ErrorKind,
                            inRes.Win32ErrorCode,
                            inRes.ErrorMessage,
                            sw.ElapsedMilliseconds);
                    }

                    snap.StatusSummary = string.Format("DDC/CI OK • Input Code {0} ({1} ms)", snap.CurrentInputValue, snap.ReadLatencyMs);
                    return DdcResult<MonitorLiveSnapshot>.Ok(snap, sw.ElapsedMilliseconds);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        public Task<DdcResult<uint>> SetInputSourceAsync(
            string monitorSelector,
            uint targetValue,
            bool useOneByteInputMask,
            bool retryOnFailure)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    return SetInputSourceSynchronized(monitorSelector, targetValue, useOneByteInputMask, retryOnFailure);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        public Task<DdcResult<uint>> ToggleInputSourceAsync(
            string monitorSelector,
            uint hdmiValue,
            uint dpValue,
            uint? lastKnownInput,
            bool useOneByteInputMask,
            bool retryOnFailure)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    DdcResult<VcpFeatureReply> cur = ReadVcpSynchronized(monitorSelector, VcpInputSelect, useOneByteInputMask, retryOnFailure);
                    uint activeVal;
                    if (cur.IsSuccess)
                    {
                        activeVal = cur.Value.NormalizedCurrentValue;
                    }
                    else if (lastKnownInput.HasValue)
                    {
                        activeVal = lastKnownInput.Value;
                    }
                    else
                    {
                        activeVal = hdmiValue;
                    }

                    uint nextTarget = (activeVal == hdmiValue) ? dpValue : hdmiValue;
                    return SetInputSourceSynchronized(monitorSelector, nextTarget, useOneByteInputMask, retryOnFailure);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        public Task<DdcResult<uint>> SetContinuousVcpAsync(
            string monitorSelector,
            byte vcpCode,
            uint newValue,
            bool retryOnFailure)
        {
            return Task.Run(delegate
            {
                _busLock.Wait();
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    MonitorDeviceRecord mon = ResolveTargetMonitor(monitorSelector);
                    if (mon == null)
                    {
                        return DdcResult<uint>.Fail(DdcErrorKind.MonitorNotFound, 0, "Target monitor not found.", sw.ElapsedMilliseconds);
                    }

                    DdcResult<bool> wr = WriteVcpOnRecord(mon, vcpCode, newValue, retryOnFailure, true);
                    sw.Stop();
                    if (!wr.IsSuccess)
                    {
                        return DdcResult<uint>.Fail(wr.ErrorKind, wr.Win32ErrorCode, wr.ErrorMessage, sw.ElapsedMilliseconds);
                    }
                    return DdcResult<uint>.Ok(newValue, sw.ElapsedMilliseconds);
                }
                finally
                {
                    _busLock.Release();
                }
            });
        }

        private DdcResult<uint> SetInputSourceSynchronized(
            string monitorSelector,
            uint targetValue,
            bool useOneByteInputMask,
            bool retryOnFailure)
        {
            Stopwatch sw = Stopwatch.StartNew();
            MonitorDeviceRecord mon = ResolveTargetMonitor(monitorSelector);
            if (mon == null)
            {
                return DdcResult<uint>.Fail(DdcErrorKind.MonitorNotFound, 0, "No DDC/CI monitor available.", sw.ElapsedMilliseconds);
            }

            DdcResult<bool> writeRes = WriteVcpOnRecord(mon, VcpInputSelect, targetValue, retryOnFailure, true);
            if (!writeRes.IsSuccess)
            {
                // Note: Some LG/DisplayPort scalers drop I2C ACK mid-switch right as they transition inputs.
                // Wait briefly and check if the read confirms the switch, otherwise report the error clearly.
                Thread.Sleep(140);
                DdcResult<VcpFeatureReply> checkAfterErr = ReadVcpOnRecord(mon, VcpInputSelect, useOneByteInputMask, false, false);
                sw.Stop();
                if (checkAfterErr.IsSuccess && checkAfterErr.Value.NormalizedCurrentValue == targetValue)
                {
                    return DdcResult<uint>.Ok(targetValue, sw.ElapsedMilliseconds);
                }
                return DdcResult<uint>.Fail(writeRes.ErrorKind, writeRes.Win32ErrorCode, writeRes.ErrorMessage, sw.ElapsedMilliseconds);
            }

            // Give the monitor scaler ~150 ms to settle before non-fatal confirmation read.
            Thread.Sleep(150);
            DdcResult<VcpFeatureReply> confirm = ReadVcpOnRecord(mon, VcpInputSelect, useOneByteInputMask, false, false);
            sw.Stop();

            if (confirm.IsSuccess)
            {
                return DdcResult<uint>.Ok(confirm.Value.NormalizedCurrentValue, sw.ElapsedMilliseconds);
            }

            // When switching AWAY to another physical port, the monitor may detach DDC/CI on this cable immediately after accepting SetVCPFeature.
            return DdcResult<uint>.Ok(targetValue, sw.ElapsedMilliseconds);
        }

        private DdcResult<VcpFeatureReply> ReadVcpSynchronized(
            string monitorSelector,
            byte vcpCode,
            bool useOneByteInputMask,
            bool retryOnFailure)
        {
            MonitorDeviceRecord mon = ResolveTargetMonitor(monitorSelector);
            if (mon == null)
            {
                return DdcResult<VcpFeatureReply>.Fail(DdcErrorKind.MonitorNotFound, 0, "Target monitor not found.", 0);
            }
            return ReadVcpOnRecord(mon, vcpCode, useOneByteInputMask, retryOnFailure, true);
        }

        private DdcResult<VcpFeatureReply> ReadVcpOnRecord(
            MonitorDeviceRecord mon,
            byte vcpCode,
            bool useOneByteInputMask,
            bool retryOnFailure,
            bool allowHandleRefresh)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string stableKey = mon.StableSelectionKey;
            int maxAttempts = retryOnFailure ? 2 : 1;
            int lastErr = 0;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(60);
                }

                uint vcpType, rawCur, rawMax;
                if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(mon.PhysicalMonitorHandle, vcpCode, out vcpType, out rawCur, out rawMax))
                {
                    sw.Stop();
                    VcpFeatureReply reply = NormalizeVcpReply(vcpCode, vcpType, rawCur, rawMax, useOneByteInputMask);
                    return DdcResult<VcpFeatureReply>.Ok(reply, sw.ElapsedMilliseconds);
                }

                lastErr = Marshal.GetLastWin32Error();
                if (allowHandleRefresh && NativeMethods.IsStaleMonitorHandleError(lastErr))
                {
                    _discovery.RefreshMonitors(false);
                    MonitorDeviceRecord refreshed = ResolveTargetMonitor(stableKey);
                    if (refreshed != null)
                    {
                        mon = refreshed;
                        allowHandleRefresh = false;
                        continue;
                    }
                }

                if (!NativeMethods.IsTransientI2cError(lastErr))
                {
                    break;
                }
            }

            sw.Stop();
            DdcErrorKind kind = ClassifyError(lastErr);
            return DdcResult<VcpFeatureReply>.Fail(kind, lastErr, NativeMethods.DescribeDdcError(lastErr), sw.ElapsedMilliseconds);
        }

        private DdcResult<bool> WriteVcpOnRecord(
            MonitorDeviceRecord mon,
            byte vcpCode,
            uint newValue,
            bool retryOnFailure,
            bool allowHandleRefresh)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string stableKey = mon.StableSelectionKey;
            int maxAttempts = retryOnFailure ? 2 : 1;
            int lastErr = 0;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(65);
                }

                if (NativeMethods.SetVCPFeature(mon.PhysicalMonitorHandle, vcpCode, newValue))
                {
                    sw.Stop();
                    return DdcResult<bool>.Ok(true, sw.ElapsedMilliseconds);
                }

                lastErr = Marshal.GetLastWin32Error();
                if (allowHandleRefresh && NativeMethods.IsStaleMonitorHandleError(lastErr))
                {
                    _discovery.RefreshMonitors(false);
                    MonitorDeviceRecord refreshed = ResolveTargetMonitor(stableKey);
                    if (refreshed != null)
                    {
                        mon = refreshed;
                        allowHandleRefresh = false;
                        continue;
                    }
                }

                if (!NativeMethods.IsTransientI2cError(lastErr))
                {
                    break;
                }
            }

            sw.Stop();
            return DdcResult<bool>.Fail(ClassifyError(lastErr), lastErr, NativeMethods.DescribeDdcError(lastErr), sw.ElapsedMilliseconds);
        }

        public static VcpFeatureReply NormalizeVcpReply(
            byte vcpCode,
            uint vcpType,
            uint rawCurrent,
            uint rawMax,
            bool useOneByteInputMask)
        {
            VcpFeatureReply r = new VcpFeatureReply();
            r.VcpCode = vcpCode;
            r.VcpType = vcpType;
            r.RawCurrentValue = rawCurrent;
            r.RawMaxValue = rawMax;

            if (vcpCode == VcpInputSelect && useOneByteInputMask)
            {
                r.NormalizedCurrentValue = rawCurrent & 0xFFu;
                r.NormalizedMaxValue = rawMax & 0xFFu;
                r.WasOneByteMasked = (rawCurrent > 0xFFu || rawMax > 0xFFu);
            }
            else
            {
                r.NormalizedCurrentValue = rawCurrent;
                r.NormalizedMaxValue = rawMax;
                r.WasOneByteMasked = false;
            }
            return r;
        }

        private static DdcErrorKind ClassifyError(int err)
        {
            if (NativeMethods.IsStaleMonitorHandleError(err)) return DdcErrorKind.HandleInvalid;
            if (err == NativeMethods.ERROR_GRAPHICS_DDCCI_VCP_NOT_SUPPORTED) return DdcErrorKind.UnsupportedVcpCode;
            if (NativeMethods.IsTransientI2cError(err) ||
                err == NativeMethods.ERROR_GRAPHICS_I2C_NOT_SUPPORTED ||
                err == NativeMethods.ERROR_GRAPHICS_I2C_DEVICE_DOES_NOT_EXIST)
            {
                return DdcErrorKind.I2cBusError;
            }
            return DdcErrorKind.Win32Failure;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _discovery.Dispose();
            _busLock.Dispose();
        }
    }
}

