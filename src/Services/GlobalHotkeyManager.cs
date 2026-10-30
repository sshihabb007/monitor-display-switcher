using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using DisplayInputSwitcher.Native;

namespace DisplayInputSwitcher.Services
{
    public enum HotkeyAction
    {
        SwitchToHdmi = 1001,
        SwitchToDp = 1002,
        ToggleInput = 1003
    }

    public sealed class ParsedHotkey
    {
        public uint Win32Modifiers { get; set; }
        public uint VirtualKey { get; set; }
        public string CanonicalText { get; set; }
    }

    public sealed class GlobalHotkeyManager : IDisposable
    {
        private IntPtr _hwnd;
        private HwndSource _hwndSource;
        private readonly HashSet<int> _registeredIds = new HashSet<int>();
        private bool _disposed;

        public event Action<HotkeyAction> HotkeyPressed;
        public event Action DisplayConfigurationChanged;

        public void AttachWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _hwnd == hwnd) return;
            DetachWindow();
            _hwnd = hwnd;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(WndProc);
            }
        }

        public string ApplyBindings(bool enabled, string hdmiCombo, string dpCombo, string toggleCombo)
        {
            UnregisterAll();
            if (!enabled || _hwnd == IntPtr.Zero)
            {
                return enabled ? "Window handle not ready" : "Global hotkeys paused";
            }

            List<string> warnings = new List<string>();
            RegisterOne(HotkeyAction.SwitchToHdmi, hdmiCombo, "HDMI", warnings);
            RegisterOne(HotkeyAction.SwitchToDp, dpCombo, "DP", warnings);
            RegisterOne(HotkeyAction.ToggleInput, toggleCombo, "Toggle", warnings);

            if (warnings.Count > 0)
            {
                return "Hotkey conflict: " + string.Join(", ", warnings.ToArray());
            }
            return string.Format("{0} global hotkeys active", _registeredIds.Count);
        }

        private void RegisterOne(HotkeyAction action, string combo, string label, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            ParsedHotkey parsed;
            if (!TryParseCombo(combo, out parsed))
            {
                warnings.Add(label + " (invalid)");
                return;
            }

            int id = (int)action;
            uint mods = parsed.Win32Modifiers | NativeMethods.MOD_NOREPEAT;
            if (NativeMethods.RegisterHotKey(_hwnd, id, mods, parsed.VirtualKey))
            {
                _registeredIds.Add(id);
            }
            else
            {
                // Fallback without MOD_NOREPEAT on older subsystems
                if (NativeMethods.RegisterHotKey(_hwnd, id, parsed.Win32Modifiers, parsed.VirtualKey))
                {
                    _registeredIds.Add(id);
                }
                else
                {
                    warnings.Add(label + " (" + parsed.CanonicalText + " in use)");
                }
            }
        }

        public void UnregisterAll()
        {
            if (_hwnd != IntPtr.Zero)
            {
                foreach (int id in _registeredIds)
                {
                    NativeMethods.UnregisterHotKey(_hwnd, id);
                }
            }
            _registeredIds.Clear();
        }

        public static string FormatWpfKeyGesture(ModifierKeys modifiers, Key key)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.System)
            {
                return string.Empty;
            }

            if (modifiers == ModifierKeys.None && (key < Key.F1 || key > Key.F24))
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

            string keyName = FriendlyKeyToken(key);
            if (string.IsNullOrEmpty(keyName)) return string.Empty;
            parts.Add(keyName);
            return string.Join("+", parts.ToArray());
        }

        public static bool TryParseCombo(string text, out ParsedHotkey result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] rawParts = text.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawParts.Length == 0) return false;

            uint mods = 0;
            Key parsedKey = Key.None;
            List<string> canonical = new List<string>();

            for (int i = 0; i < rawParts.Length; i++)
            {
                string tok = rawParts[i].Trim();
                if (string.Equals(tok, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tok, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_CONTROL;
                }
                else if (string.Equals(tok, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_ALT;
                }
                else if (string.Equals(tok, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_SHIFT;
                }
                else if (string.Equals(tok, "Win", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= NativeMethods.MOD_WIN;
                }
                else
                {
                    parsedKey = ParseSingleKeyToken(tok);
                }
            }

            if (parsedKey == Key.None) return false;
            int vk = KeyInterop.VirtualKeyFromKey(parsedKey);
            if (vk <= 0) return false;

            // Require at least one modifier unless Function key F1..F24
            if (mods == 0 && (parsedKey < Key.F1 || parsedKey > Key.F24))
            {
                return false;
            }

            if ((mods & NativeMethods.MOD_CONTROL) != 0) canonical.Add("Ctrl");
            if ((mods & NativeMethods.MOD_ALT) != 0) canonical.Add("Alt");
            if ((mods & NativeMethods.MOD_SHIFT) != 0) canonical.Add("Shift");
            if ((mods & NativeMethods.MOD_WIN) != 0) canonical.Add("Win");
            canonical.Add(FriendlyKeyToken(parsedKey));

            ParsedHotkey ph = new ParsedHotkey();
            ph.Win32Modifiers = mods;
            ph.VirtualKey = (uint)vk;
            ph.CanonicalText = string.Join("+", canonical.ToArray());
            result = ph;
            return true;
        }

        private static Key ParseSingleKeyToken(string tok)
        {
            if (tok.Length == 1 && tok[0] >= '0' && tok[0] <= '9')
            {
                return (Key)((int)Key.D0 + (tok[0] - '0'));
            }
            if (tok.Length == 1 && ((tok[0] >= 'a' && tok[0] <= 'z') || (tok[0] >= 'A' && tok[0] <= 'Z')))
            {
                char up = char.ToUpperInvariant(tok[0]);
                return (Key)((int)Key.A + (up - 'A'));
            }

            Key k;
            if (Enum.TryParse<Key>(tok, true, out k))
            {
                return k;
            }
            return Key.None;
        }

        private static string FriendlyKeyToken(Key k)
        {
            if (k >= Key.D0 && k <= Key.D9)
            {
                return ((int)(k - Key.D0)).ToString();
            }
            if (k >= Key.NumPad0 && k <= Key.NumPad9)
            {
                return "NumPad" + ((int)(k - Key.NumPad0)).ToString();
            }
            return k.ToString();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == (int)HotkeyAction.SwitchToHdmi ||
                    id == (int)HotkeyAction.SwitchToDp ||
                    id == (int)HotkeyAction.ToggleInput)
                {
                    handled = true;
                    Action<HotkeyAction> handler = HotkeyPressed;
                    if (handler != null)
                    {
                        handler((HotkeyAction)id);
                    }
                }
            }
            else if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                Action dispHandler = DisplayConfigurationChanged;
                if (dispHandler != null)
                {
                    dispHandler();
                }
            }
            return IntPtr.Zero;
        }

        private void DetachWindow()
        {
            UnregisterAll();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
            _hwnd = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachWindow();
        }
    }
}

