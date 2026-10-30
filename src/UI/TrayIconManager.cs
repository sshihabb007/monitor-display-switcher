using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DisplayInputSwitcher.Core;

namespace DisplayInputSwitcher.UI
{
    public sealed class TrayIconManager : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _headerItem;
        private readonly ToolStripMenuItem _hdmiItem;
        private readonly ToolStripMenuItem _dpItem;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripSeparator _extraPortsSep;
        private readonly List<ToolStripMenuItem> _extraPortItems = new List<ToolStripMenuItem>();
        private readonly ToolStripMenuItem _pinItem;
        private readonly ToolStripMenuItem _aboutItem;
        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _exitItem;

        private Icon _currentCustomIcon;
        private bool _disposed;

        public event Action OpenWindowRequested;
        public event Action AboutRequested;
        public event Action SwitchHdmiRequested;
        public event Action SwitchDpRequested;
        public event Action ToggleInputRequested;
        public event Action<uint, string> SwitchSpecificCodeRequested;
        public event Action ToggleAlwaysOnTopRequested;
        public event Action ExitRequested;

        public TrayIconManager()
        {
            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkMenuRenderer();
            _menu.BackColor = Color.FromArgb(15, 23, 42);
            _menu.ForeColor = Color.FromArgb(241, 245, 249);
            _menu.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            _menu.ShowImageMargin = false;
            _menu.Padding = new Padding(4);

            _headerItem = new ToolStripMenuItem("Display Input Switcher");
            _headerItem.Enabled = false;
            _headerItem.Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold);

            _hdmiItem = new ToolStripMenuItem("Switch to HDMI");
            _hdmiItem.Click += delegate { Raise(SwitchHdmiRequested); };

            _dpItem = new ToolStripMenuItem("Switch to DisplayPort");
            _dpItem.Click += delegate { Raise(SwitchDpRequested); };

            _toggleItem = new ToolStripMenuItem("Toggle HDMI / DP");
            _toggleItem.Click += delegate { Raise(ToggleInputRequested); };

            _extraPortsSep = new ToolStripSeparator();
            _extraPortsSep.Visible = false;

            _pinItem = new ToolStripMenuItem("Always on Top");
            _pinItem.Click += delegate { Raise(ToggleAlwaysOnTopRequested); };

            _aboutItem = new ToolStripMenuItem("ℹ  About Developer (Mehedi Hasan Shihab)");
            _aboutItem.Click += delegate { Raise(AboutRequested); };

            _openItem = new ToolStripMenuItem("Open Switcher Window");
            _openItem.Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold);
            _openItem.Click += delegate { Raise(OpenWindowRequested); };

            _exitItem = new ToolStripMenuItem("Exit Completely");
            _exitItem.ForeColor = Color.FromArgb(252, 165, 165);
            _exitItem.Click += delegate { Raise(ExitRequested); };

            _menu.Items.Add(_headerItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_hdmiItem);
            _menu.Items.Add(_dpItem);
            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(_extraPortsSep);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_pinItem);
            _menu.Items.Add(_aboutItem);
            _menu.Items.Add(_openItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_exitItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Text = "Display Input Switcher";
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += OnTrayMouseClick;

            UpdateTrayVisual(null, new AppConfig(), new ResolvedMonitorInputMap(), null);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Raise(OpenWindowRequested);
            }
            else if (e.Button == MouseButtons.Middle)
            {
                Raise(ToggleInputRequested);
            }
        }

        public void UpdateTrayVisual(string monitorName, AppConfig cfg, ResolvedMonitorInputMap map, uint? activeInput)
        {
            if (_disposed || cfg == null || map == null) return;

            bool isHdmi = activeInput.HasValue && activeInput.Value == map.HdmiValue;
            bool isDp = activeInput.HasValue && activeInput.Value == map.DpValue;

            string activeTag = isHdmi
                ? map.HdmiLabel + " [" + map.HdmiValue + "]"
                : (isDp ? map.DpLabel + " [" + map.DpValue + "]" : (activeInput.HasValue ? "Code " + activeInput.Value : "Ready"));

            string monShort = string.IsNullOrWhiteSpace(monitorName) ? "Input Switcher" : monitorName;
            _headerItem.Text = monShort + " • " + activeTag;

            string hdmiHk = (cfg.HotkeysEnabled && !string.IsNullOrEmpty(cfg.HdmiHotkey)) ? "   (" + cfg.HdmiHotkey + ")" : "";
            string dpHk = (cfg.HotkeysEnabled && !string.IsNullOrEmpty(cfg.DpHotkey)) ? "   (" + cfg.DpHotkey + ")" : "";
            string togHk = (cfg.HotkeysEnabled && !string.IsNullOrEmpty(cfg.ToggleHotkey)) ? "   (" + cfg.ToggleHotkey + ")" : "";

            _hdmiItem.Text = string.Format("{0}  {1} (Code {2}){3}", isHdmi ? "●" : "○", map.HdmiLabel, map.HdmiValue, hdmiHk);
            _dpItem.Text = string.Format("{0}  {1} (Code {2}){3}", isDp ? "●" : "○", map.DpLabel, map.DpValue, dpHk);
            _toggleItem.Text = string.Format("⇄  Toggle {0} / {1}{2}", map.HdmiLabel, map.DpLabel, togHk);
            _pinItem.Text = (cfg.AlwaysOnTop ? "✓  " : "    ") + "Always on Top";

            // Dynamic extra port entries for 3+ input monitors (e.g. HDMI 2, USB-C)
            for (int i = 0; i < _extraPortItems.Count; i++)
            {
                _menu.Items.Remove(_extraPortItems[i]);
                _extraPortItems[i].Dispose();
            }
            _extraPortItems.Clear();

            if (map.DetectedOptions != null && map.DetectedOptions.Count > 2)
            {
                int insertIdx = _menu.Items.IndexOf(_extraPortsSep) + 1;
                int added = 0;
                for (int i = 0; i < map.DetectedOptions.Count; i++)
                {
                    DetectedInputOption opt = map.DetectedOptions[i];
                    if (opt.Code == map.HdmiValue || opt.Code == map.DpValue) continue;
                    bool optActive = activeInput.HasValue && activeInput.Value == opt.Code;
                    ToolStripMenuItem mi = new ToolStripMenuItem(string.Format("{0}  {1} (Code {2})", optActive ? "●" : "○", opt.FriendlyName, opt.Code));
                    uint capCode = opt.Code;
                    string capName = opt.FriendlyName;
                    mi.Click += delegate
                    {
                        Action<uint, string> h = SwitchSpecificCodeRequested;
                        if (h != null) h(capCode, capName);
                    };
                    _menu.Items.Insert(insertIdx + added, mi);
                    _extraPortItems.Add(mi);
                    added++;
                }
                _extraPortsSep.Visible = (added > 0);
            }
            else
            {
                _extraPortsSep.Visible = false;
            }

            string tip = string.Format("{0}\nActive: {1}", monShort, activeTag);
            if (tip.Length > 63)
            {
                tip = tip.Substring(0, 63);
            }
            _notifyIcon.Text = tip;

            Icon nextIcon = RenderDynamicBadgeIcon(isHdmi, isDp);
            if (nextIcon != null)
            {
                Icon prev = _currentCustomIcon;
                _currentCustomIcon = nextIcon;
                _notifyIcon.Icon = nextIcon;
                if (prev != null)
                {
                    prev.Dispose();
                }
            }
        }

        private static Icon RenderDynamicBadgeIcon(bool isHdmi, bool isDp)
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        Color accent = isHdmi
                            ? Color.FromArgb(34, 211, 238)
                            : (isDp ? Color.FromArgb(167, 139, 250) : Color.FromArgb(96, 165, 250));

                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(15, 23, 42)))
                        using (Pen borderPen = new Pen(accent, 2.2f))
                        {
                            DrawRoundedRect(g, bg, borderPen, new RectangleF(2f, 2f, 28f, 28f), 7f);
                        }

                        string label = isHdmi ? "H" : (isDp ? "DP" : "IO");
                        float fontSize = label.Length > 1 ? 9.5f : 12.5f;
                        using (Font f = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString(label, f, textBrush, new RectangleF(1f, 2f, 30f, 29f), sf);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using (Icon temp = Icon.FromHandle(hIcon))
                        {
                            return (Icon)temp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private static void DrawRoundedRect(Graphics g, Brush fill, Pen stroke, RectangleF rect, float radius)
        {
            float d = radius * 2f;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(fill, path);
                g.DrawPath(stroke, path);
            }
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            if (_currentCustomIcon != null)
            {
                _currentCustomIcon.Dispose();
                _currentCustomIcon = null;
            }
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (!e.Item.Enabled)
                {
                    e.TextColor = Color.FromArgb(56, 189, 248);
                }
                else if (e.Item.ForeColor != Color.FromArgb(252, 165, 165))
                {
                    e.TextColor = e.Item.Selected
                        ? Color.White
                        : Color.FromArgb(226, 232, 240);
                }
                base.OnRenderItemText(e);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(15, 23, 42); } }
            public override Color MenuBorder { get { return Color.FromArgb(51, 65, 85); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(56, 189, 248); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(30, 41, 59); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(30, 41, 59); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(30, 41, 59); } }
            public override Color SeparatorDark { get { return Color.FromArgb(51, 65, 85); } }
            public override Color SeparatorLight { get { return Color.FromArgb(15, 23, 42); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(15, 23, 42); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(15, 23, 42); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(15, 23, 42); } }
        }
    }
}
