using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DisplayInputSwitcher.Core;
using DisplayInputSwitcher.Native;
using DisplayInputSwitcher.Services;
using Microsoft.Win32;

namespace DisplayInputSwitcher.UI
{
    public sealed class MainWindow : Window
    {
        private readonly ConfigService _configService;
        private readonly DdcCiService _ddcService;
        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly TrayIconManager _trayManager;

        private AppConfig _config;
        private IReadOnlyList<MonitorDeviceRecord> _monitors = new List<MonitorDeviceRecord>();
        private MonitorDeviceRecord _selectedMonitor;
        private uint? _activeInputCode;
        private bool _isMuted;
        private bool _isSwitchingOrReading;
        private bool _settingsOpen;
        private bool _allowRealExit;

        // Debounce timers
        private readonly DispatcherTimer _brightnessDebounce;
        private readonly DispatcherTimer _volumeDebounce;
        private readonly DispatcherTimer _displayChangeDebounce;
        private uint? _pendingBrightness;
        private uint? _pendingVolume;

        // UI Elements
        private Border _pinButton;
        private TextBlock _pinGlyph;
        private Border _settingsButton;
        private Border _liveBadgeBorder;
        private Ellipse _liveBadgeDot;
        private TextBlock _liveBadgeText;

        private Border _monitorSelectorBtn;
        private TextBlock _monitorTitleText;
        private TextBlock _monitorSubText;
        private Popup _monitorDropdownPopup;
        private StackPanel _monitorDropdownList;

        // Dual Input Cards
        private Border _hdmiCard;
        private TextBlock _hdmiCardTitle;
        private TextBlock _hdmiCodeBadge;
        private Border _hdmiActivePill;
        private TextBlock _hdmiActivePillText;
        private TextBlock _hdmiHotkeyHint;

        private Border _dpCard;
        private TextBlock _dpCardTitle;
        private TextBlock _dpCodeBadge;
        private Border _dpActivePill;
        private TextBlock _dpActivePillText;
        private TextBlock _dpHotkeyHint;

        private TextBlock _toggleBtnLabel;
        private TextBlock _toggleBtnHotkey;

        // Quick Controls Drawer
        private Border _quickDrawerContent;
        private TextBlock _quickDrawerChevron;
        private ModernDarkSlider _brightnessSlider;
        private TextBlock _brightnessValueText;
        private Grid _brightnessRow;
        private ModernDarkSlider _volumeSlider;
        private TextBlock _volumeValueText;
        private Border _muteBtn;
        private TextBlock _muteGlyph;
        private Grid _volumeRow;

        // Settings Drawer
        private Border _settingsDrawerContent;
        private TextBox _hdmiCodeInput;
        private TextBox _dpCodeInput;
        private TextBlock _possibleValuesHint;
        private FrameworkElement _enableHotkeysToggle;
        private Action<bool> _setEnableHotkeysVisual;
        private TextBox _hdmiHotkeyBox;
        private TextBox _dpHotkeyBox;
        private TextBox _toggleHotkeyBox;
        private TextBlock _hotkeyStatusLabel;
        private FrameworkElement _closeToTrayToggle;
        private Action<bool> _setCloseToTrayVisual;
        private FrameworkElement _oneByteMaskToggle;
        private Action<bool> _setOneByteMaskVisual;
        private FrameworkElement _startupToggle;
        private Action<bool> _setStartupVisual;

        // Footer
        private TextBlock _statusFooterText;

        public MainWindow(
            ConfigService configService,
            DdcCiService ddcService,
            GlobalHotkeyManager hotkeyManager,
            TrayIconManager trayManager)
        {
            _configService = configService;
            _ddcService = ddcService;
            _hotkeyManager = hotkeyManager;
            _trayManager = trayManager;
            _config = _configService.Load();

            Title = "Display Input Switcher";
            Width = 412;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = _config.AlwaysOnTop;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _brightnessDebounce = new DispatcherTimer();
            _brightnessDebounce.Interval = TimeSpan.FromMilliseconds(130);
            _brightnessDebounce.Tick += OnBrightnessDebounceTick;

            _volumeDebounce = new DispatcherTimer();
            _volumeDebounce.Interval = TimeSpan.FromMilliseconds(130);
            _volumeDebounce.Tick += OnVolumeDebounceTick;

            _displayChangeDebounce = new DispatcherTimer();
            _displayChangeDebounce.Interval = TimeSpan.FromMilliseconds(750);
            _displayChangeDebounce.Tick += OnDisplayChangeDebounceTick;

            Content = BuildRootVisual();

            _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;
            _hotkeyManager.DisplayConfigurationChanged += ScheduleDisplayRefresh;
            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;

            _trayManager.OpenWindowRequested += RestoreAndActivateWindow;
            _trayManager.SwitchHdmiRequested += delegate { TriggerSwitchToInput(_config.HdmiValue, _config.HdmiLabel); };
            _trayManager.SwitchDpRequested += delegate { TriggerSwitchToInput(_config.DpValue, _config.DpLabel); };
            _trayManager.ToggleInputRequested += TriggerToggleInput;
            _trayManager.ToggleAlwaysOnTopRequested += ToggleAlwaysOnTop;
            _trayManager.ExitRequested += RequestRealExit;

            PreviewKeyDown += OnWindowPreviewKeyDown;
            Closing += OnWindowClosing;
            Closed += OnWindowClosed;

            ApplyConfigToUi();
        }

        private Task _initialLoadTask;

        public void InitializeHardwareAndHotkeys()
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            _hotkeyManager.AttachWindow(hwnd);
            RefreshHotkeys();
            _initialLoadTask = RefreshMonitorsAndLiveStateAsync(true);
        }

        private UIElement BuildRootVisual()
        {
            Border outerCard = new Border();
            outerCard.Margin = new Thickness(14);
            outerCard.CornerRadius = new CornerRadius(16);
            outerCard.BorderThickness = new Thickness(1);
            outerCard.BorderBrush = BrushFromHex("#26354D");

            LinearGradientBrush cardBg = new LinearGradientBrush();
            cardBg.StartPoint = new Point(0, 0);
            cardBg.EndPoint = new Point(0.3, 1);
            cardBg.GradientStops.Add(new GradientStop(ColorFromHex("#0B1120"), 0.0));
            cardBg.GradientStops.Add(new GradientStop(ColorFromHex("#0F172A"), 0.55));
            cardBg.GradientStops.Add(new GradientStop(ColorFromHex("#11192E"), 1.0));
            outerCard.Background = cardBg;

            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 22;
            shadow.ShadowDepth = 4;
            shadow.Direction = 270;
            shadow.Color = Colors.Black;
            shadow.Opacity = 0.62;
            outerCard.Effect = shadow;

            StackPanel mainStack = new StackPanel();
            mainStack.Orientation = Orientation.Vertical;

            // Top luminous gradient accent line inset cleanly within rounded bezel
            Border topAccent = new Border();
            topAccent.Height = 2.5;
            topAccent.Margin = new Thickness(16, 0, 16, 0);
            topAccent.CornerRadius = new CornerRadius(1.5);
            LinearGradientBrush accentGrad = new LinearGradientBrush();
            accentGrad.StartPoint = new Point(0, 0);
            accentGrad.EndPoint = new Point(1, 0);
            accentGrad.GradientStops.Add(new GradientStop(ColorFromHex("#22D3EE"), 0.0));
            accentGrad.GradientStops.Add(new GradientStop(ColorFromHex("#818CF8"), 0.5));
            accentGrad.GradientStops.Add(new GradientStop(ColorFromHex("#A855F7"), 1.0));
            topAccent.Background = accentGrad;
            mainStack.Children.Add(topAccent);

            // Body container
            StackPanel body = new StackPanel();
            body.Margin = new Thickness(16, 12, 16, 12);

            body.Children.Add(BuildTitleBar());
            body.Children.Add(BuildMonitorSelectorSection());
            body.Children.Add(BuildDualInputCardsSection());
            body.Children.Add(BuildQuickToggleButton());
            body.Children.Add(BuildQuickControlsSection());
            body.Children.Add(BuildSettingsSection());
            body.Children.Add(BuildFooterBar());

            mainStack.Children.Add(body);
            outerCard.Child = mainStack;
            return outerCard;
        }

        private UIElement BuildTitleBar()
        {
            Grid bar = new Grid();
            bar.Margin = new Thickness(0, 0, 0, 12);
            bar.Background = Brushes.Transparent;
            bar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: App badge + Title + Live pill
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;

            Border logoPill = new Border();
            logoPill.Width = 26;
            logoPill.Height = 26;
            logoPill.CornerRadius = new CornerRadius(7);
            logoPill.Background = new LinearGradientBrush(ColorFromHex("#0E7490"), ColorFromHex("#4F46E5"), 45.0);
            logoPill.Margin = new Thickness(0, 0, 9, 0);
            Path logoPath = new Path();
            logoPath.Data = Geometry.Parse("M 4,6 L 20,6 L 20,16 L 4,16 Z M 9,19 L 15,19 M 12,16 L 12,19");
            logoPath.Stroke = Brushes.White;
            logoPath.StrokeThickness = 1.6;
            logoPath.HorizontalAlignment = HorizontalAlignment.Center;
            logoPath.VerticalAlignment = VerticalAlignment.Center;
            logoPill.Child = logoPath;
            left.Children.Add(logoPill);

            TextBlock titleText = new TextBlock();
            titleText.Text = "Input Switcher";
            titleText.Foreground = BrushFromHex("#F8FAFC");
            titleText.FontFamily = new FontFamily("Segoe UI Semibold");
            titleText.FontSize = 13.5;
            titleText.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(titleText);

            _liveBadgeBorder = new Border();
            _liveBadgeBorder.CornerRadius = new CornerRadius(9);
            _liveBadgeBorder.Background = BrushFromHex("#0F2922");
            _liveBadgeBorder.BorderBrush = BrushFromHex("#10B981");
            _liveBadgeBorder.BorderThickness = new Thickness(1);
            _liveBadgeBorder.Padding = new Thickness(7, 2, 7, 2);
            _liveBadgeBorder.Margin = new Thickness(8, 0, 0, 0);
            _liveBadgeBorder.VerticalAlignment = VerticalAlignment.Center;

            StackPanel badgeStack = new StackPanel();
            badgeStack.Orientation = Orientation.Horizontal;
            _liveBadgeDot = new Ellipse();
            _liveBadgeDot.Width = 6;
            _liveBadgeDot.Height = 6;
            _liveBadgeDot.Fill = BrushFromHex("#34D399");
            _liveBadgeDot.VerticalAlignment = VerticalAlignment.Center;
            _liveBadgeDot.Margin = new Thickness(0, 0, 5, 0);

            _liveBadgeText = new TextBlock();
            _liveBadgeText.Text = "DDC/CI";
            _liveBadgeText.FontSize = 9.5;
            _liveBadgeText.FontFamily = new FontFamily("Segoe UI Semibold");
            _liveBadgeText.Foreground = BrushFromHex("#6EE7B7");

            badgeStack.Children.Add(_liveBadgeDot);
            badgeStack.Children.Add(_liveBadgeText);
            _liveBadgeBorder.Child = badgeStack;
            left.Children.Add(_liveBadgeBorder);

            Grid.SetColumn(left, 0);
            bar.Children.Add(left);

            // Right: Window tool buttons (Pin, Refresh, Settings, Minimize, Close)
            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;

            _pinButton = CreateIconToolbarButton("📌", "Always on Top", out _pinGlyph, ToggleAlwaysOnTop, false);
            TextBlock dummy;
            Border refreshBtn = CreateIconToolbarButton("↻", "Refresh Monitor State (F5)", out dummy, delegate
            {
                Task ignored = RefreshMonitorsAndLiveStateAsync(true);
            }, false);
            _settingsButton = CreateIconToolbarButton("⚙", "Settings & Hotkeys", out dummy, ToggleSettingsDrawer, false);
            Border minBtn = CreateIconToolbarButton("—", "Minimize", out dummy, delegate
            {
                WindowState = WindowState.Minimized;
            }, false);
            Border closeBtn = CreateIconToolbarButton("✕", "Close (Minimizes to Tray if enabled)", out dummy, delegate
            {
                Close();
            }, true);

            right.Children.Add(_pinButton);
            right.Children.Add(refreshBtn);
            right.Children.Add(_settingsButton);
            right.Children.Add(minBtn);
            right.Children.Add(closeBtn);

            Grid.SetColumn(right, 1);
            bar.Children.Add(right);
            return bar;
        }

        private Border CreateIconToolbarButton(string symbol, string tooltip, out TextBlock glyphOut, Action onClick, bool isDangerHover)
        {
            Border b = new Border();
            b.Width = 26;
            b.Height = 26;
            b.CornerRadius = new CornerRadius(7);
            b.Background = Brushes.Transparent;
            b.Margin = new Thickness(2, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.ToolTip = tooltip;

            TextBlock t = new TextBlock();
            t.Text = symbol;
            t.FontSize = 11.5;
            t.Foreground = BrushFromHex("#94A3B8");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            glyphOut = t;

            b.MouseEnter += delegate
            {
                b.Background = isDangerHover ? BrushFromHex("#DC2626") : BrushFromHex("#1E293B");
                t.Foreground = Brushes.White;
            };
            b.MouseLeave += delegate
            {
                UpdateToolbarHighlights();
                if (b != _pinButton && b != _settingsButton)
                {
                    b.Background = Brushes.Transparent;
                    t.Foreground = BrushFromHex("#94A3B8");
                }
            };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) onClick();
            };

            return b;
        }

        private UIElement BuildMonitorSelectorSection()
        {
            Grid wrap = new Grid();
            wrap.Margin = new Thickness(0, 0, 0, 12);

            _monitorSelectorBtn = new Border();
            _monitorSelectorBtn.CornerRadius = new CornerRadius(10);
            _monitorSelectorBtn.Background = BrushFromHex("#131D31");
            _monitorSelectorBtn.BorderBrush = BrushFromHex("#24324A");
            _monitorSelectorBtn.BorderThickness = new Thickness(1);
            _monitorSelectorBtn.Padding = new Thickness(11, 8, 11, 8);
            _monitorSelectorBtn.Cursor = Cursors.Hand;
            _monitorSelectorBtn.ToolTip = "Click to switch target monitor";

            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel textCol = new StackPanel();
            _monitorTitleText = new TextBlock();
            _monitorTitleText.Text = "Scanning DDC/CI Monitors...";
            _monitorTitleText.Foreground = BrushFromHex("#F1F5F9");
            _monitorTitleText.FontFamily = new FontFamily("Segoe UI Semibold");
            _monitorTitleText.FontSize = 12.2;
            _monitorTitleText.TextTrimming = TextTrimming.CharacterEllipsis;

            _monitorSubText = new TextBlock();
            _monitorSubText.Text = "Querying Dxva2 & EDID metadata...";
            _monitorSubText.Foreground = BrushFromHex("#64748B");
            _monitorSubText.FontSize = 10.2;
            _monitorSubText.Margin = new Thickness(0, 1, 0, 0);
            _monitorSubText.TextTrimming = TextTrimming.CharacterEllipsis;

            textCol.Children.Add(_monitorTitleText);
            textCol.Children.Add(_monitorSubText);
            Grid.SetColumn(textCol, 0);
            inner.Children.Add(textCol);

            TextBlock chev = new TextBlock();
            chev.Text = "▾";
            chev.Foreground = BrushFromHex("#64748B");
            chev.FontSize = 13;
            chev.VerticalAlignment = VerticalAlignment.Center;
            chev.Margin = new Thickness(8, 0, 2, 0);
            Grid.SetColumn(chev, 1);
            inner.Children.Add(chev);

            _monitorSelectorBtn.Child = inner;

            _monitorSelectorBtn.MouseEnter += delegate
            {
                _monitorSelectorBtn.BorderBrush = BrushFromHex("#38BDF8");
                _monitorSelectorBtn.Background = BrushFromHex("#17233B");
            };
            _monitorSelectorBtn.MouseLeave += delegate
            {
                _monitorSelectorBtn.BorderBrush = BrushFromHex("#24324A");
                _monitorSelectorBtn.Background = BrushFromHex("#131D31");
            };

            // Custom dark Popup list so there is zero white Aero ComboBox chrome
            _monitorDropdownPopup = new Popup();
            _monitorDropdownPopup.PlacementTarget = _monitorSelectorBtn;
            _monitorDropdownPopup.Placement = PlacementMode.Bottom;
            _monitorDropdownPopup.StaysOpen = false;
            _monitorDropdownPopup.AllowsTransparency = true;
            _monitorDropdownPopup.VerticalOffset = 4;

            Border popupBorder = new Border();
            popupBorder.Width = 352;
            popupBorder.CornerRadius = new CornerRadius(10);
            popupBorder.Background = BrushFromHex("#0F172A");
            popupBorder.BorderBrush = BrushFromHex("#334155");
            popupBorder.BorderThickness = new Thickness(1);
            popupBorder.Padding = new Thickness(5);
            popupBorder.Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Color = Colors.Black,
                Opacity = 0.65
            };

            _monitorDropdownList = new StackPanel();
            popupBorder.Child = _monitorDropdownList;
            _monitorDropdownPopup.Child = popupBorder;

            _monitorSelectorBtn.MouseLeftButtonUp += delegate
            {
                if (_monitors != null && _monitors.Count > 0)
                {
                    RebuildMonitorPopupItems();
                    _monitorDropdownPopup.IsOpen = !_monitorDropdownPopup.IsOpen;
                }
            };

            wrap.Children.Add(_monitorSelectorBtn);
            return wrap;
        }

        private void RebuildMonitorPopupItems()
        {
            _monitorDropdownList.Children.Clear();
            for (int i = 0; i < _monitors.Count; i++)
            {
                MonitorDeviceRecord m = _monitors[i];
                bool isSel = (_selectedMonitor != null && m.StableSelectionKey == _selectedMonitor.StableSelectionKey);

                Border item = new Border();
                item.CornerRadius = new CornerRadius(7);
                item.Padding = new Thickness(10, 7, 10, 7);
                item.Margin = new Thickness(0, 1, 0, 1);
                item.Cursor = Cursors.Hand;
                item.Background = isSel ? BrushFromHex("#1E293B") : Brushes.Transparent;

                StackPanel sp = new StackPanel();
                TextBlock l1 = new TextBlock();
                l1.Text = (isSel ? "●  " : "○  ") + m.DropdownLabel;
                l1.Foreground = isSel ? BrushFromHex("#38BDF8") : BrushFromHex("#F1F5F9");
                l1.FontFamily = new FontFamily("Segoe UI Semibold");
                l1.FontSize = 11.5;

                TextBlock l2 = new TextBlock();
                string ddcTag = m.SupportsInputSelect ? "DDC/CI Input Select Ready" : "Read-only / Internal Panel";
                l2.Text = string.Format("     {0} • {1}", string.IsNullOrEmpty(m.AdapterName) ? m.MonitorDeviceName : m.AdapterName, ddcTag);
                l2.Foreground = BrushFromHex("#64748B");
                l2.FontSize = 10;

                sp.Children.Add(l1);
                sp.Children.Add(l2);
                item.Child = sp;

                item.MouseEnter += delegate { if (!isSel) item.Background = BrushFromHex("#172033"); };
                item.MouseLeave += delegate { if (!isSel) item.Background = Brushes.Transparent; };

                MonitorDeviceRecord captured = m;
                item.MouseLeftButtonUp += delegate
                {
                    _monitorDropdownPopup.IsOpen = false;
                    SelectMonitor(captured, true);
                };

                _monitorDropdownList.Children.Add(item);
            }
        }

        private UIElement BuildDualInputCardsSection()
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 0, 0, 10);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: HDMI Hero Card
            _hdmiCard = BuildSingleInputCard(
                true,
                "M 3,6 L 25,6 L 25,13 L 22,16 L 6,16 L 3,13 Z M 7,10 L 21,10",
                out _hdmiCardTitle,
                out _hdmiCodeBadge,
                out _hdmiActivePill,
                out _hdmiActivePillText,
                out _hdmiHotkeyHint,
                delegate { TriggerSwitchToInput(_config.HdmiValue, _config.HdmiLabel); });
            Grid.SetColumn(_hdmiCard, 0);
            grid.Children.Add(_hdmiCard);

            // Center: 1-Click Swap Values button (1 <-> 3)
            Border swapBtn = new Border();
            swapBtn.Width = 30;
            swapBtn.Height = 30;
            swapBtn.CornerRadius = new CornerRadius(15);
            swapBtn.Background = BrushFromHex("#162136");
            swapBtn.BorderBrush = BrushFromHex("#2E405E");
            swapBtn.BorderThickness = new Thickness(1);
            swapBtn.Margin = new Thickness(7, 0, 7, 0);
            swapBtn.VerticalAlignment = VerticalAlignment.Center;
            swapBtn.Cursor = Cursors.Hand;
            swapBtn.ToolTip = "1-Click Swap HDMI & DisplayPort Values (1 ⇄ 3)";

            TextBlock swapIcon = new TextBlock();
            swapIcon.Text = "⇄";
            swapIcon.FontSize = 13.5;
            swapIcon.Foreground = BrushFromHex("#94A3B8");
            swapIcon.HorizontalAlignment = HorizontalAlignment.Center;
            swapIcon.VerticalAlignment = VerticalAlignment.Center;
            swapBtn.Child = swapIcon;

            swapBtn.MouseEnter += delegate
            {
                swapBtn.Background = BrushFromHex("#233252");
                swapBtn.BorderBrush = BrushFromHex("#38BDF8");
                swapIcon.Foreground = Brushes.White;
            };
            swapBtn.MouseLeave += delegate
            {
                swapBtn.Background = BrushFromHex("#162136");
                swapBtn.BorderBrush = BrushFromHex("#2E405E");
                swapIcon.Foreground = BrushFromHex("#94A3B8");
            };
            swapBtn.MouseLeftButtonUp += delegate { SwapHdmiAndDpValues(); };

            Grid.SetColumn(swapBtn, 1);
            grid.Children.Add(swapBtn);

            // Right: DisplayPort Hero Card
            _dpCard = BuildSingleInputCard(
                false,
                "M 3,6 L 21,6 L 25,10 L 25,16 L 3,16 Z M 8,11 L 19,11",
                out _dpCardTitle,
                out _dpCodeBadge,
                out _dpActivePill,
                out _dpActivePillText,
                out _dpHotkeyHint,
                delegate { TriggerSwitchToInput(_config.DpValue, _config.DpLabel); });
            Grid.SetColumn(_dpCard, 2);
            grid.Children.Add(_dpCard);

            return grid;
        }

        private Border BuildSingleInputCard(
            bool isHdmiSlot,
            string portVectorData,
            out TextBlock titleOut,
            out TextBlock codeOut,
            out Border pillOut,
            out TextBlock pillTextOut,
            out TextBlock hotkeyOut,
            Action onClick)
        {
            Border card = new Border();
            card.CornerRadius = new CornerRadius(13);
            card.BorderThickness = new Thickness(1.5);
            card.BorderBrush = BrushFromHex("#233047");
            card.Background = BrushFromHex("#131D31");
            card.Padding = new Thickness(12, 12, 12, 11);
            card.Cursor = Cursors.Hand;

            StackPanel stack = new StackPanel();
            stack.HorizontalAlignment = HorizontalAlignment.Center;

            // Top Active Status Pill
            Border pill = new Border();
            pill.CornerRadius = new CornerRadius(8);
            pill.Padding = new Thickness(8, 2, 8, 2);
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.Margin = new Thickness(0, 0, 0, 9);
            pill.Background = BrushFromHex("#1E293B");

            TextBlock pillText = new TextBlock();
            pillText.Text = "STANDBY";
            pillText.FontSize = 9.5;
            pillText.FontFamily = new FontFamily("Segoe UI Semibold");
            pillText.Foreground = BrushFromHex("#64748B");
            pill.Child = pillText;
            stack.Children.Add(pill);

            // Connector Vector Icon
            Path portIcon = new Path();
            portIcon.Data = Geometry.Parse(portVectorData);
            portIcon.Stroke = isHdmiSlot ? BrushFromHex("#22D3EE") : BrushFromHex("#A78BFA");
            portIcon.StrokeThickness = 1.9;
            portIcon.HorizontalAlignment = HorizontalAlignment.Center;
            portIcon.Margin = new Thickness(0, 0, 0, 7);
            stack.Children.Add(portIcon);

            // Port Name ("HDMI" / "DisplayPort")
            TextBlock title = new TextBlock();
            title.Text = isHdmiSlot ? "HDMI" : "DisplayPort";
            title.FontFamily = new FontFamily("Segoe UI Semibold");
            title.FontSize = 15;
            title.Foreground = BrushFromHex("#F8FAFC");
            title.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(title);

            // VCP 60 Code Badge
            TextBlock codeBadge = new TextBlock();
            codeBadge.Text = isHdmiSlot ? "VCP 60 = 1" : "VCP 60 = 3";
            codeBadge.FontSize = 10.5;
            codeBadge.Foreground = BrushFromHex("#94A3B8");
            codeBadge.HorizontalAlignment = HorizontalAlignment.Center;
            codeBadge.Margin = new Thickness(0, 2, 0, 5);
            stack.Children.Add(codeBadge);

            // Global Hotkey Hint
            TextBlock hkHint = new TextBlock();
            hkHint.Text = isHdmiSlot ? "Ctrl+Alt+1" : "Ctrl+Alt+2";
            hkHint.FontSize = 9.5;
            hkHint.Foreground = BrushFromHex("#475569");
            hkHint.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(hkHint);

            card.Child = stack;

            card.MouseEnter += delegate
            {
                bool active = IsSlotCurrentlyActive(isHdmiSlot);
                if (!active)
                {
                    card.Background = BrushFromHex("#192640");
                    card.BorderBrush = isHdmiSlot ? BrushFromHex("#0891B2") : BrushFromHex("#7C3AED");
                }
            };
            card.MouseLeave += delegate
            {
                UpdateInputCardsVisualState();
            };
            card.MouseLeftButtonUp += delegate
            {
                if (onClick != null) onClick();
            };

            titleOut = title;
            codeOut = codeBadge;
            pillOut = pill;
            pillTextOut = pillText;
            hotkeyOut = hkHint;
            return card;
        }

        private UIElement BuildQuickToggleButton()
        {
            Border btn = new Border();
            btn.CornerRadius = new CornerRadius(10);
            btn.BorderThickness = new Thickness(1);
            btn.BorderBrush = BrushFromHex("#2E3E5C");
            btn.Padding = new Thickness(12, 8, 12, 8);
            btn.Margin = new Thickness(0, 0, 0, 10);
            btn.Cursor = Cursors.Hand;

            LinearGradientBrush bg = new LinearGradientBrush(ColorFromHex("#162238"), ColorFromHex("#1D2844"), 0.0);
            btn.Background = bg;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _toggleBtnLabel = new TextBlock();
            _toggleBtnLabel.Text = "⇄   Quick Toggle Input (HDMI ⇄ DisplayPort)";
            _toggleBtnLabel.Foreground = BrushFromHex("#E2E8F0");
            _toggleBtnLabel.FontFamily = new FontFamily("Segoe UI Semibold");
            _toggleBtnLabel.FontSize = 11.5;
            _toggleBtnLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_toggleBtnLabel, 0);
            g.Children.Add(_toggleBtnLabel);

            _toggleBtnHotkey = new TextBlock();
            _toggleBtnHotkey.Text = "Ctrl+Alt+S";
            _toggleBtnHotkey.Foreground = BrushFromHex("#64748B");
            _toggleBtnHotkey.FontSize = 10.2;
            _toggleBtnHotkey.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_toggleBtnHotkey, 1);
            g.Children.Add(_toggleBtnHotkey);

            btn.Child = g;

            btn.MouseEnter += delegate
            {
                btn.BorderBrush = BrushFromHex("#60A5FA");
                btn.Background = new LinearGradientBrush(ColorFromHex("#1D2D4B"), ColorFromHex("#26355D"), 0.0);
            };
            btn.MouseLeave += delegate
            {
                btn.BorderBrush = BrushFromHex("#2E3E5C");
                btn.Background = bg;
            };
            btn.MouseLeftButtonUp += delegate { TriggerToggleInput(); };

            return btn;
        }

        private UIElement BuildQuickControlsSection()
        {
            StackPanel section = new StackPanel();
            section.Margin = new Thickness(0, 0, 0, 8);

            // Collapsible Drawer Header
            Border header = new Border();
            header.CornerRadius = new CornerRadius(9);
            header.Background = BrushFromHex("#111A2C");
            header.BorderBrush = BrushFromHex("#1E293B");
            header.BorderThickness = new Thickness(1);
            header.Padding = new Thickness(11, 6, 11, 6);
            header.Cursor = Cursors.Hand;

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock hTitle = new TextBlock();
            hTitle.Text = "☀  Quick Controls  (Brightness & Speaker Volume)";
            hTitle.Foreground = BrushFromHex("#94A3B8");
            hTitle.FontFamily = new FontFamily("Segoe UI Semibold");
            hTitle.FontSize = 11;
            Grid.SetColumn(hTitle, 0);
            hg.Children.Add(hTitle);

            _quickDrawerChevron = new TextBlock();
            _quickDrawerChevron.Text = _config.QuickDrawerExpanded ? "▴" : "▾";
            _quickDrawerChevron.Foreground = BrushFromHex("#64748B");
            _quickDrawerChevron.FontSize = 12;
            Grid.SetColumn(_quickDrawerChevron, 1);
            hg.Children.Add(_quickDrawerChevron);

            header.Child = hg;

            header.MouseEnter += delegate { header.BorderBrush = BrushFromHex("#334155"); hTitle.Foreground = BrushFromHex("#E2E8F0"); };
            header.MouseLeave += delegate { header.BorderBrush = BrushFromHex("#1E293B"); hTitle.Foreground = BrushFromHex("#94A3B8"); };
            header.MouseLeftButtonUp += delegate
            {
                _config.QuickDrawerExpanded = !_config.QuickDrawerExpanded;
                _quickDrawerContent.Visibility = _config.QuickDrawerExpanded ? Visibility.Visible : Visibility.Collapsed;
                _quickDrawerChevron.Text = _config.QuickDrawerExpanded ? "▴" : "▾";
                _configService.Save(_config);
                if (_config.QuickDrawerExpanded)
                {
                    Task ignored = RefreshLiveSnapshotOnlyAsync(true);
                }
            };

            section.Children.Add(header);

            // Collapsible Content
            _quickDrawerContent = new Border();
            _quickDrawerContent.CornerRadius = new CornerRadius(10);
            _quickDrawerContent.Background = BrushFromHex("#111A2E");
            _quickDrawerContent.BorderBrush = BrushFromHex("#1F2D46");
            _quickDrawerContent.BorderThickness = new Thickness(1);
            _quickDrawerContent.Padding = new Thickness(12, 10, 12, 10);
            _quickDrawerContent.Margin = new Thickness(0, 5, 0, 0);
            _quickDrawerContent.Visibility = _config.QuickDrawerExpanded ? Visibility.Visible : Visibility.Collapsed;

            StackPanel slidersStack = new StackPanel();

            // Brightness Row (VCP 10)
            _brightnessRow = new Grid();
            _brightnessRow.Margin = new Thickness(0, 0, 0, 8);
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            TextBlock brLabel = new TextBlock();
            brLabel.Text = "☀ Brightness";
            brLabel.Foreground = BrushFromHex("#CBD5E1");
            brLabel.FontSize = 11;
            brLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(brLabel, 0);
            _brightnessRow.Children.Add(brLabel);

            _brightnessSlider = new ModernDarkSlider(ColorFromHex("#06B6D4"), ColorFromHex("#38BDF8"));
            _brightnessSlider.ValuePreviewChanged += delegate(uint v)
            {
                _brightnessValueText.Text = v.ToString() + "%";
            };
            _brightnessSlider.ValueCommittedByUser += delegate(uint v)
            {
                _brightnessValueText.Text = v.ToString() + "%";
                _pendingBrightness = v;
                _brightnessDebounce.Stop();
                _brightnessDebounce.Start();
            };
            Grid.SetColumn(_brightnessSlider, 1);
            _brightnessRow.Children.Add(_brightnessSlider);

            _brightnessValueText = new TextBlock();
            _brightnessValueText.Text = "--%";
            _brightnessValueText.Foreground = BrushFromHex("#38BDF8");
            _brightnessValueText.FontFamily = new FontFamily("Segoe UI Semibold");
            _brightnessValueText.FontSize = 11;
            _brightnessValueText.TextAlignment = TextAlignment.Right;
            _brightnessValueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_brightnessValueText, 2);
            _brightnessRow.Children.Add(_brightnessValueText);

            slidersStack.Children.Add(_brightnessRow);

            // Speaker Volume Row (VCP 62 + VCP 8D Mute)
            _volumeRow = new Grid();
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            _muteBtn = new Border();
            _muteBtn.CornerRadius = new CornerRadius(5);
            _muteBtn.Background = Brushes.Transparent;
            _muteBtn.Cursor = Cursors.Hand;
            _muteBtn.ToolTip = "Click to toggle Monitor Audio Mute (VCP 8D)";
            _muteGlyph = new TextBlock();
            _muteGlyph.Text = "🔊 Volume";
            _muteGlyph.Foreground = BrushFromHex("#CBD5E1");
            _muteGlyph.FontSize = 11;
            _muteGlyph.VerticalAlignment = VerticalAlignment.Center;
            _muteBtn.Child = _muteGlyph;
            _muteBtn.MouseLeftButtonUp += delegate { ToggleMonitorMute(); };
            Grid.SetColumn(_muteBtn, 0);
            _volumeRow.Children.Add(_muteBtn);

            _volumeSlider = new ModernDarkSlider(ColorFromHex("#8B5CF6"), ColorFromHex("#A78BFA"));
            _volumeSlider.ValuePreviewChanged += delegate(uint v)
            {
                _volumeValueText.Text = v.ToString() + "%";
            };
            _volumeSlider.ValueCommittedByUser += delegate(uint v)
            {
                _volumeValueText.Text = v.ToString() + "%";
                _pendingVolume = v;
                _volumeDebounce.Stop();
                _volumeDebounce.Start();
            };
            Grid.SetColumn(_volumeSlider, 1);
            _volumeRow.Children.Add(_volumeSlider);

            _volumeValueText = new TextBlock();
            _volumeValueText.Text = "--%";
            _volumeValueText.Foreground = BrushFromHex("#A78BFA");
            _volumeValueText.FontFamily = new FontFamily("Segoe UI Semibold");
            _volumeValueText.FontSize = 11;
            _volumeValueText.TextAlignment = TextAlignment.Right;
            _volumeValueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_volumeValueText, 2);
            _volumeRow.Children.Add(_volumeValueText);

            slidersStack.Children.Add(_volumeRow);
            _quickDrawerContent.Child = slidersStack;
            section.Children.Add(_quickDrawerContent);

            return section;
        }

        private UIElement BuildSettingsSection()
        {
            _settingsDrawerContent = new Border();
            _settingsDrawerContent.CornerRadius = new CornerRadius(11);
            _settingsDrawerContent.Background = BrushFromHex("#111A2E");
            _settingsDrawerContent.BorderBrush = BrushFromHex("#2E3E5C");
            _settingsDrawerContent.BorderThickness = new Thickness(1);
            _settingsDrawerContent.Padding = new Thickness(12, 10, 12, 11);
            _settingsDrawerContent.Margin = new Thickness(0, 0, 0, 8);
            _settingsDrawerContent.Visibility = Visibility.Collapsed;

            StackPanel sp = new StackPanel();

            TextBlock sec1 = new TextBlock();
            sec1.Text = "VCP 60 INPUT CODES (1 – 255)";
            sec1.FontSize = 9.8;
            sec1.FontFamily = new FontFamily("Segoe UI Semibold");
            sec1.Foreground = BrushFromHex("#38BDF8");
            sec1.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(sec1);

            Grid codeGrid = new Grid();
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lHdmi = CreateSmallLabel("HDMI Code:");
            lHdmi.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(lHdmi, 0);
            codeGrid.Children.Add(lHdmi);

            _hdmiCodeInput = CreateDarkTextBox(_config.HdmiValue.ToString());
            Grid.SetColumn(_hdmiCodeInput, 1);
            codeGrid.Children.Add(_hdmiCodeInput);

            TextBlock lDp = CreateSmallLabel("DP Code:");
            lDp.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(lDp, 3);
            codeGrid.Children.Add(lDp);

            _dpCodeInput = CreateDarkTextBox(_config.DpValue.ToString());
            Grid.SetColumn(_dpCodeInput, 4);
            codeGrid.Children.Add(_dpCodeInput);

            Border applyCodesBtn = CreateCompactPillButton("Save Codes", "#0284C7", ApplyCustomVcpCodesFromUi);
            applyCodesBtn.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(applyCodesBtn, 5);
            codeGrid.Children.Add(applyCodesBtn);

            sp.Children.Add(codeGrid);

            _possibleValuesHint = new TextBlock();
            _possibleValuesHint.Text = "Monitor Possible Values (VCP 60): 1, 3";
            _possibleValuesHint.FontSize = 10;
            _possibleValuesHint.Foreground = BrushFromHex("#64748B");
            _possibleValuesHint.Margin = new Thickness(0, 5, 0, 10);
            sp.Children.Add(_possibleValuesHint);

            // Global Hotkeys Section
            TextBlock sec2 = new TextBlock();
            sec2.Text = "GLOBAL KEYBOARD SHORTCUTS (CLICK BOX & PRESS KEYS)";
            sec2.FontSize = 9.8;
            sec2.FontFamily = new FontFamily("Segoe UI Semibold");
            sec2.Foreground = BrushFromHex("#A78BFA");
            sec2.Margin = new Thickness(0, 0, 0, 5);
            sp.Children.Add(sec2);

            _enableHotkeysToggle = CreateDarkToggleSwitch("Enable global hotkeys in background", _config.HotkeysEnabled, out _setEnableHotkeysVisual, delegate(bool isChecked)
            {
                _config.HotkeysEnabled = isChecked;
                SaveAndRefreshHotkeys();
            });
            sp.Children.Add(_enableHotkeysToggle);

            sp.Children.Add(BuildHotkeyCaptureRow("HDMI Shortcut:", out _hdmiHotkeyBox, _config.HdmiHotkey, delegate(string combo)
            {
                _config.HdmiHotkey = combo;
                SaveAndRefreshHotkeys();
            }));
            sp.Children.Add(BuildHotkeyCaptureRow("DP Shortcut:", out _dpHotkeyBox, _config.DpHotkey, delegate(string combo)
            {
                _config.DpHotkey = combo;
                SaveAndRefreshHotkeys();
            }));
            sp.Children.Add(BuildHotkeyCaptureRow("Toggle Shortcut:", out _toggleHotkeyBox, _config.ToggleHotkey, delegate(string combo)
            {
                _config.ToggleHotkey = combo;
                SaveAndRefreshHotkeys();
            }));

            _hotkeyStatusLabel = new TextBlock();
            _hotkeyStatusLabel.FontSize = 9.8;
            _hotkeyStatusLabel.Foreground = BrushFromHex("#6EE7B7");
            _hotkeyStatusLabel.Margin = new Thickness(0, 3, 0, 8);
            sp.Children.Add(_hotkeyStatusLabel);

            // General Behavior Toggles
            _closeToTrayToggle = CreateDarkToggleSwitch("Close button (✕) minimizes to System Tray", _config.MinimizeToTrayOnClose, out _setCloseToTrayVisual, delegate(bool c)
            {
                _config.MinimizeToTrayOnClose = c;
                _configService.Save(_config);
            });
            sp.Children.Add(_closeToTrayToggle);

            _oneByteMaskToggle = CreateDarkToggleSwitch("Use 1-Byte VCP 60 mask (& 0xFF for LG/Dell DDC/CI)", _config.UseOneByteInputMask, out _setOneByteMaskVisual, delegate(bool c)
            {
                _config.UseOneByteInputMask = c;
                _configService.Save(_config);
                Task ignored = RefreshLiveSnapshotOnlyAsync(false);
            });
            sp.Children.Add(_oneByteMaskToggle);

            _startupToggle = CreateDarkToggleSwitch("Launch minimized in Tray when Windows starts", _configService.IsLaunchAtStartupEnabled(), out _setStartupVisual, delegate(bool c)
            {
                _configService.SetLaunchAtStartup(c);
            });
            sp.Children.Add(_startupToggle);

            // Bottom action row inside Settings (Reset Defaults + Quit Completely)
            Grid bottomActions = new Grid();
            bottomActions.Margin = new Thickness(0, 8, 0, 0);
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border resetHkBtn = CreateCompactPillButton("Reset Defaults", "#334155", ResetSettingsDefaults);
            Grid.SetColumn(resetHkBtn, 0);
            bottomActions.Children.Add(resetHkBtn);

            Border quitBtn = CreateCompactPillButton("Exit App Completely", "#B91C1C", RequestRealExit);
            Grid.SetColumn(quitBtn, 2);
            bottomActions.Children.Add(quitBtn);

            sp.Children.Add(bottomActions);
            _settingsDrawerContent.Child = sp;
            return _settingsDrawerContent;
        }

        private UIElement BuildHotkeyCaptureRow(string labelText, out TextBox boxOut, string initialValue, Action<string> onCaptured)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 2);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = CreateSmallLabel(labelText);
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            TextBox box = CreateDarkTextBox(initialValue);
            box.IsReadOnly = true;
            box.Cursor = Cursors.Hand;
            box.ToolTip = "Click here and press your desired shortcut (e.g. Ctrl+Alt+1), or Backspace to clear";

            TextBox capturedBox = box;
            box.GotKeyboardFocus += delegate
            {
                capturedBox.BorderBrush = BrushFromHex("#A78BFA");
                capturedBox.Background = BrushFromHex("#1E1B3A");
            };
            box.LostKeyboardFocus += delegate
            {
                capturedBox.BorderBrush = BrushFromHex("#334155");
                capturedBox.Background = BrushFromHex("#0B1120");
            };
            box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                e.Handled = true;
                Key realKey = (e.Key == Key.System) ? e.SystemKey : e.Key;
                if (realKey == Key.Escape)
                {
                    Keyboard.ClearFocus();
                    return;
                }
                if ((realKey == Key.Back || realKey == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
                {
                    capturedBox.Text = string.Empty;
                    if (onCaptured != null) onCaptured(string.Empty);
                    Keyboard.ClearFocus();
                    return;
                }
                string formatted = GlobalHotkeyManager.FormatWpfKeyGesture(Keyboard.Modifiers, realKey);
                if (!string.IsNullOrEmpty(formatted))
                {
                    capturedBox.Text = formatted;
                    if (onCaptured != null) onCaptured(formatted);
                    Keyboard.ClearFocus();
                }
            };

            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            boxOut = box;
            return row;
        }

        private UIElement BuildFooterBar()
        {
            Grid footer = new Grid();
            footer.Margin = new Thickness(2, 2, 2, 0);
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusFooterText = new TextBlock();
            _statusFooterText.Text = "Initializing DDC/CI engine...";
            _statusFooterText.Foreground = BrushFromHex("#64748B");
            _statusFooterText.FontSize = 10.2;
            _statusFooterText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_statusFooterText, 0);
            footer.Children.Add(_statusFooterText);

            TextBlock credit = new TextBlock();
            credit.Text = "Portable DDC/CI • Shihab";
            credit.Foreground = BrushFromHex("#475569");
            credit.FontSize = 9.8;
            credit.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(credit, 1);
            footer.Children.Add(credit);

            return footer;
        }

        private TextBlock CreateSmallLabel(string text)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.Foreground = BrushFromHex("#CBD5E1");
            tb.FontSize = 10.8;
            tb.VerticalAlignment = VerticalAlignment.Center;
            return tb;
        }

        private TextBox CreateDarkTextBox(string initial)
        {
            TextBox tb = new TextBox();
            tb.Text = initial;
            tb.Background = BrushFromHex("#0B1120");
            tb.Foreground = BrushFromHex("#F8FAFC");
            tb.CaretBrush = Brushes.White;
            tb.BorderBrush = BrushFromHex("#334155");
            tb.BorderThickness = new Thickness(1);
            tb.Padding = new Thickness(6, 3, 6, 3);
            tb.FontSize = 11;
            tb.FontFamily = new FontFamily("Segoe UI Semibold");
            tb.TextAlignment = TextAlignment.Center;
            return tb;
        }

        private FrameworkElement CreateDarkToggleSwitch(
            string label,
            bool initial,
            out Action<bool> setStateVisualOut,
            Action<bool> onChanged)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 4, 0, 4);
            row.Cursor = Cursors.Hand;
            row.Background = Brushes.Transparent;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border pillTrack = new Border();
            pillTrack.Width = 30;
            pillTrack.Height = 16;
            pillTrack.CornerRadius = new CornerRadius(8);
            pillTrack.Margin = new Thickness(0, 0, 9, 0);
            pillTrack.VerticalAlignment = VerticalAlignment.Center;

            Ellipse knob = new Ellipse();
            knob.Width = 12;
            knob.Height = 12;
            knob.Fill = Brushes.White;
            knob.VerticalAlignment = VerticalAlignment.Center;
            pillTrack.Child = knob;

            Grid.SetColumn(pillTrack, 0);
            row.Children.Add(pillTrack);

            TextBlock txt = new TextBlock();
            txt.Text = label;
            txt.Foreground = BrushFromHex("#CBD5E1");
            txt.FontSize = 10.8;
            txt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);

            bool state = initial;
            Action<bool> applyVisual = delegate(bool active)
            {
                state = active;
                pillTrack.Background = active ? BrushFromHex("#0284C7") : BrushFromHex("#1E293B");
                pillTrack.BorderBrush = active ? BrushFromHex("#38BDF8") : BrushFromHex("#334155");
                pillTrack.BorderThickness = new Thickness(1);
                knob.HorizontalAlignment = active ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Margin = active ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
            };
            applyVisual(initial);

            row.MouseLeftButtonUp += delegate
            {
                bool next = !state;
                applyVisual(next);
                if (onChanged != null) onChanged(next);
            };

            setStateVisualOut = applyVisual;
            return row;
        }

        private Border CreateCompactPillButton(string text, string hexColor, Action onClick)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Background = BrushFromHex(hexColor);
            b.Padding = new Thickness(10, 4, 10, 4);
            b.Cursor = Cursors.Hand;

            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = Brushes.White;
            t.FontSize = 10.5;
            t.FontFamily = new FontFamily("Segoe UI Semibold");
            b.Child = t;

            b.MouseEnter += delegate { b.Opacity = 0.88; };
            b.MouseLeave += delegate { b.Opacity = 1.0; };
            b.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            return b;
        }

        private void ApplyConfigToUi()
        {
            Topmost = _config.AlwaysOnTop;
            _hdmiCardTitle.Text = _config.HdmiLabel;
            _dpCardTitle.Text = _config.DpLabel;
            _hdmiCodeBadge.Text = string.Format("VCP 60 • Code {0}", _config.HdmiValue);
            _dpCodeBadge.Text = string.Format("VCP 60 • Code {0}", _config.DpValue);

            _hdmiHotkeyHint.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.HdmiHotkey)) ? _config.HdmiHotkey : "Click to Switch";
            _dpHotkeyHint.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.DpHotkey)) ? _config.DpHotkey : "Click to Switch";
            _toggleBtnHotkey.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.ToggleHotkey)) ? _config.ToggleHotkey : "";

            _hdmiCodeInput.Text = _config.HdmiValue.ToString();
            _dpCodeInput.Text = _config.DpValue.ToString();
            _hdmiHotkeyBox.Text = _config.HdmiHotkey;
            _dpHotkeyBox.Text = _config.DpHotkey;
            _toggleHotkeyBox.Text = _config.ToggleHotkey;

            UpdateToolbarHighlights();
            UpdateInputCardsVisualState();
            _trayManager.UpdateTrayVisual(_selectedMonitor != null ? _selectedMonitor.EdidMonitorName : null, _config, _activeInputCode);
        }

        private void UpdateToolbarHighlights()
        {
            if (_pinButton != null)
            {
                _pinButton.Background = _config.AlwaysOnTop ? BrushFromHex("#0E3A4C") : Brushes.Transparent;
                _pinGlyph.Foreground = _config.AlwaysOnTop ? BrushFromHex("#22D3EE") : BrushFromHex("#94A3B8");
            }
            if (_settingsButton != null)
            {
                _settingsButton.Background = _settingsOpen ? BrushFromHex("#271E47") : Brushes.Transparent;
                TextBlock sg = _settingsButton.Child as TextBlock;
                if (sg != null)
                {
                    sg.Foreground = _settingsOpen ? BrushFromHex("#A78BFA") : BrushFromHex("#94A3B8");
                }
            }
        }

        private bool IsSlotCurrentlyActive(bool isHdmiSlot)
        {
            if (!_activeInputCode.HasValue) return false;
            return isHdmiSlot
                ? (_activeInputCode.Value == _config.HdmiValue)
                : (_activeInputCode.Value == _config.DpValue);
        }

        private void UpdateInputCardsVisualState()
        {
            bool hdmiActive = IsSlotCurrentlyActive(true);
            bool dpActive = IsSlotCurrentlyActive(false);

            if (hdmiActive)
            {
                _hdmiCard.Background = new LinearGradientBrush(ColorFromHex("#0D2D3E"), ColorFromHex("#112238"), 45.0);
                _hdmiCard.BorderBrush = BrushFromHex("#22D3EE");
                _hdmiActivePill.Background = BrushFromHex("#0891B2");
                _hdmiActivePillText.Text = "● ACTIVE";
                _hdmiActivePillText.Foreground = Brushes.White;
            }
            else
            {
                _hdmiCard.Background = BrushFromHex("#131D31");
                _hdmiCard.BorderBrush = BrushFromHex("#233047");
                _hdmiActivePill.Background = BrushFromHex("#1E293B");
                _hdmiActivePillText.Text = "SWITCH TO";
                _hdmiActivePillText.Foreground = BrushFromHex("#64748B");
            }

            if (dpActive)
            {
                _dpCard.Background = new LinearGradientBrush(ColorFromHex("#231942"), ColorFromHex("#161D36"), 45.0);
                _dpCard.BorderBrush = BrushFromHex("#A78BFA");
                _dpActivePill.Background = BrushFromHex("#7C3AED");
                _dpActivePillText.Text = "● ACTIVE";
                _dpActivePillText.Foreground = Brushes.White;
            }
            else
            {
                _dpCard.Background = BrushFromHex("#131D31");
                _dpCard.BorderBrush = BrushFromHex("#233047");
                _dpActivePill.Background = BrushFromHex("#1E293B");
                _dpActivePillText.Text = "SWITCH TO";
                _dpActivePillText.Foreground = BrushFromHex("#64748B");
            }
        }

        private void SetLiveBadge(string text, string bgHex, string borderHex, string dotHex, string fgHex)
        {
            _liveBadgeText.Text = text;
            _liveBadgeBorder.Background = BrushFromHex(bgHex);
            _liveBadgeBorder.BorderBrush = BrushFromHex(borderHex);
            _liveBadgeDot.Fill = BrushFromHex(dotHex);
            _liveBadgeText.Foreground = BrushFromHex(fgHex);
        }

        private async Task RefreshMonitorsAndLiveStateAsync(bool probeCapabilities)
        {
            if (_isSwitchingOrReading) return;
            _isSwitchingOrReading = true;
            SetLiveBadge("SCANNING", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
            _statusFooterText.Text = "Enumerating displays & DDC/CI capabilities...";

            try
            {
                _monitors = await _ddcService.RefreshMonitorsAsync(probeCapabilities);
                if (_monitors.Count == 0)
                {
                    _selectedMonitor = null;
                    _monitorTitleText.Text = "No DDC/CI Display Found";
                    _monitorSubText.Text = "Check monitor power / cable connection";
                    SetLiveBadge("OFFLINE", "#331418", "#EF4444", "#F87171", "#FCA5A5");
                    _statusFooterText.Text = "No active display handles returned by Windows.";
                    return;
                }

                MonitorDeviceRecord resolved = _ddcService.ResolveTargetMonitor(_config.SelectedMonitorKey);
                SelectMonitor(resolved, false);
            }
            finally
            {
                _isSwitchingOrReading = false;
            }

            await RefreshLiveSnapshotOnlyAsync(_config.QuickDrawerExpanded);
        }

        private void SelectMonitor(MonitorDeviceRecord mon, bool saveSelection)
        {
            _selectedMonitor = mon;
            if (mon == null) return;

            if (saveSelection)
            {
                _config.SelectedMonitorKey = mon.StableSelectionKey;
                _configService.Save(_config);
            }

            _monitorTitleText.Text = mon.DropdownLabel;

            List<byte> possibleInputs = (mon.Capabilities != null) ? mon.Capabilities.GetInputSelectValues() : new List<byte>();
            string posStr = possibleInputs.Count > 0 ? string.Join(", ", possibleInputs) : "1, 3";
            _possibleValuesHint.Text = string.Format("Monitor Possible Values (VCP 60): {0}", posStr);

            string gpuText = !string.IsNullOrEmpty(mon.AdapterName) ? mon.AdapterName : mon.MonitorDeviceName;
            _monitorSubText.Text = string.Format("{0}  •  VCP 60 [{1}]", gpuText, posStr);

            _brightnessRow.IsEnabled = mon.SupportsBrightness;
            _brightnessRow.Opacity = mon.SupportsBrightness ? 1.0 : 0.45;
            _volumeRow.IsEnabled = mon.SupportsVolume;
            _volumeRow.Opacity = mon.SupportsVolume ? 1.0 : 0.45;

            if (saveSelection)
            {
                Task ignored = RefreshLiveSnapshotOnlyAsync(_config.QuickDrawerExpanded);
            }
        }

        private async Task RefreshLiveSnapshotOnlyAsync(bool includeQuickControls)
        {
            if (_selectedMonitor == null || _isSwitchingOrReading) return;
            _isSwitchingOrReading = true;
            try
            {
                DdcResult<MonitorLiveSnapshot> res = await _ddcService.ReadLiveSnapshotAsync(
                    _selectedMonitor.StableSelectionKey,
                    _config.UseOneByteInputMask,
                    _config.RetryOnI2cFailure,
                    includeQuickControls);

                if (res.IsSuccess)
                {
                    MonitorLiveSnapshot snap = res.Value;
                    _activeInputCode = snap.CurrentInputValue;

                    if (snap.Brightness.HasValue)
                    {
                        uint bMax = snap.BrightnessMax.HasValue ? snap.BrightnessMax.Value : 100u;
                        _brightnessSlider.SetValueFromHardware(snap.Brightness.Value, bMax);
                        _brightnessValueText.Text = snap.Brightness.Value.ToString() + "%";
                    }

                    if (snap.Volume.HasValue)
                    {
                        uint vMax = snap.VolumeMax.HasValue ? snap.VolumeMax.Value : 100u;
                        _volumeSlider.SetValueFromHardware(snap.Volume.Value, vMax);
                        _volumeValueText.Text = snap.Volume.Value.ToString() + "%";
                    }

                    if (snap.IsAudioMuted.HasValue)
                    {
                        _isMuted = snap.IsAudioMuted.Value;
                        _muteGlyph.Text = _isMuted ? "🔇 Muted" : "🔊 Volume";
                        _muteGlyph.Foreground = _isMuted ? BrushFromHex("#F87171") : BrushFromHex("#CBD5E1");
                    }

                    SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                    string activeFriendly = DescribeInputCode(_activeInputCode);
                    _statusFooterText.Text = string.Format("Active: {0}  •  DDC/CI {1} ms", activeFriendly, snap.ReadLatencyMs);
                }
                else
                {
                    SetLiveBadge("STANDBY", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
                    _statusFooterText.Text = res.ErrorMessage;
                }

                UpdateInputCardsVisualState();
                _trayManager.UpdateTrayVisual(_selectedMonitor.EdidMonitorName, _config, _activeInputCode);
            }
            finally
            {
                _isSwitchingOrReading = false;
            }
        }

        private async void TriggerSwitchToInput(uint targetValue, string friendlyLabel)
        {
            if (_selectedMonitor == null)
            {
                await RefreshMonitorsAndLiveStateAsync(false);
                if (_selectedMonitor == null) return;
            }

            SetLiveBadge("SWITCHING", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
            _statusFooterText.Text = string.Format("Switching {0} to {1} (VCP 60 = {2})...", _selectedMonitor.FriendlyTitle, friendlyLabel, targetValue);

            DdcResult<uint> res = await _ddcService.SetInputSourceAsync(
                _selectedMonitor.StableSelectionKey,
                targetValue,
                _config.UseOneByteInputMask,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _activeInputCode = res.Value;
                SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                _statusFooterText.Text = string.Format("Switched to {0} (Code {1})  •  {2} ms", friendlyLabel, _activeInputCode.Value, res.ElapsedMs);
            }
            else
            {
                SetLiveBadge("I2C WARN", "#331418", "#EF4444", "#F87171", "#FCA5A5");
                _statusFooterText.Text = res.ErrorMessage;
            }

            UpdateInputCardsVisualState();
            _trayManager.UpdateTrayVisual(_selectedMonitor.EdidMonitorName, _config, _activeInputCode);
        }

        private async void TriggerToggleInput()
        {
            if (_selectedMonitor == null)
            {
                await RefreshMonitorsAndLiveStateAsync(false);
                if (_selectedMonitor == null) return;
            }

            SetLiveBadge("TOGGLING", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
            _statusFooterText.Text = "Toggling HDMI ⇄ DisplayPort over DDC/CI...";

            DdcResult<uint> res = await _ddcService.ToggleInputSourceAsync(
                _selectedMonitor.StableSelectionKey,
                _config.HdmiValue,
                _config.DpValue,
                _activeInputCode,
                _config.UseOneByteInputMask,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _activeInputCode = res.Value;
                string name = DescribeInputCode(_activeInputCode);
                SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                _statusFooterText.Text = string.Format("Switched to {0}  •  {1} ms", name, res.ElapsedMs);
            }
            else
            {
                SetLiveBadge("I2C WARN", "#331418", "#EF4444", "#F87171", "#FCA5A5");
                _statusFooterText.Text = res.ErrorMessage;
            }

            UpdateInputCardsVisualState();
            _trayManager.UpdateTrayVisual(_selectedMonitor.EdidMonitorName, _config, _activeInputCode);
        }

        private string DescribeInputCode(uint? code)
        {
            if (!code.HasValue) return "Unknown";
            if (code.Value == _config.HdmiValue) return string.Format("{0} (Code {1})", _config.HdmiLabel, code.Value);
            if (code.Value == _config.DpValue) return string.Format("{0} (Code {1})", _config.DpLabel, code.Value);
            return string.Format("Input Code {0}", code.Value);
        }

        private void SwapHdmiAndDpValues()
        {
            uint tmp = _config.HdmiValue;
            _config.HdmiValue = _config.DpValue;
            _config.DpValue = tmp;
            _configService.Save(_config);
            ApplyConfigToUi();
            _statusFooterText.Text = string.Format("Swapped mapping: {0} = {1}, {2} = {3}", _config.HdmiLabel, _config.HdmiValue, _config.DpLabel, _config.DpValue);
        }

        private void ApplyCustomVcpCodesFromUi()
        {
            uint hVal, dVal;
            if (!uint.TryParse(_hdmiCodeInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hVal) || hVal > 255 ||
                !uint.TryParse(_dpCodeInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dVal) || dVal > 255)
            {
                _statusFooterText.Text = "Please enter valid VCP 60 integer codes between 0 and 255.";
                return;
            }

            _config.HdmiValue = hVal;
            _config.DpValue = dVal;
            _configService.Save(_config);
            ApplyConfigToUi();
            _statusFooterText.Text = string.Format("Saved VCP 60 codes: HDMI = {0}, DisplayPort = {1}", hVal, dVal);
        }

        private void ResetSettingsDefaults()
        {
            _config.HdmiValue = 1;
            _config.DpValue = 3;
            _config.HotkeysEnabled = true;
            _config.HdmiHotkey = "Ctrl+Alt+1";
            _config.DpHotkey = "Ctrl+Alt+2";
            _config.ToggleHotkey = "Ctrl+Alt+S";
            _config.UseOneByteInputMask = true;
            _config.MinimizeToTrayOnClose = true;
            if (_setEnableHotkeysVisual != null) _setEnableHotkeysVisual(true);
            if (_setCloseToTrayVisual != null) _setCloseToTrayVisual(true);
            if (_setOneByteMaskVisual != null) _setOneByteMaskVisual(true);
            _configService.Save(_config);
            ApplyConfigToUi();
            RefreshHotkeys();
            _statusFooterText.Text = "Restored default codes (1 = HDMI, 3 = DP) and shortcuts.";
        }

        public async void RunUiPreviewCaptureAsync(string outputDir)
        {
            try
            {
                if (!System.IO.Directory.Exists(outputDir))
                {
                    System.IO.Directory.CreateDirectory(outputDir);
                }
                if (_initialLoadTask != null)
                {
                    await _initialLoadTask;
                }
                await Task.Delay(120);
                UpdateLayout();
                SaveWindowPng(System.IO.Path.Combine(outputDir, "ui_compact.png"));

                _quickDrawerContent.Visibility = Visibility.Visible;
                _quickDrawerChevron.Text = "▴";
                await RefreshLiveSnapshotOnlyAsync(true);
                _settingsOpen = true;
                _settingsDrawerContent.Visibility = Visibility.Visible;
                UpdateToolbarHighlights();
                await Task.Delay(180);
                UpdateLayout();
                SaveWindowPng(System.IO.Path.Combine(outputDir, "ui_expanded.png"));
            }
            catch
            {
            }
            finally
            {
                RequestRealExit();
            }
        }

        private void SaveWindowPng(string filePath)
        {
            FrameworkElement visual = Content as FrameworkElement;
            if (visual == null) return;
            DpiScale dpi = VisualTreeHelper.GetDpi(visual);
            int pxW = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
            int pxH = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
            RenderTargetBitmap rtb = new RenderTargetBitmap(
                pxW,
                pxH,
                96.0 * dpi.DpiScaleX,
                96.0 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            rtb.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (System.IO.FileStream fs = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                encoder.Save(fs);
            }
        }

        private void SaveAndRefreshHotkeys()
        {
            _configService.Save(_config);
            ApplyConfigToUi();
            RefreshHotkeys();
        }

        private void RefreshHotkeys()
        {
            string status = _hotkeyManager.ApplyBindings(
                _config.HotkeysEnabled,
                _config.HdmiHotkey,
                _config.DpHotkey,
                _config.ToggleHotkey);

            if (_hotkeyStatusLabel != null)
            {
                _hotkeyStatusLabel.Text = status;
                _hotkeyStatusLabel.Foreground = status.StartsWith("Hotkey conflict")
                    ? BrushFromHex("#FCA5A5")
                    : BrushFromHex("#6EE7B7");
            }
        }

        private void OnGlobalHotkeyPressed(HotkeyAction action)
        {
            if (action == HotkeyAction.SwitchToHdmi)
            {
                TriggerSwitchToInput(_config.HdmiValue, _config.HdmiLabel);
            }
            else if (action == HotkeyAction.SwitchToDp)
            {
                TriggerSwitchToInput(_config.DpValue, _config.DpLabel);
            }
            else if (action == HotkeyAction.ToggleInput)
            {
                TriggerToggleInput();
            }
        }

        private async void OnBrightnessDebounceTick(object sender, EventArgs e)
        {
            _brightnessDebounce.Stop();
            if (!_pendingBrightness.HasValue || _selectedMonitor == null) return;
            uint val = _pendingBrightness.Value;
            _pendingBrightness = null;

            DdcResult<uint> res = await _ddcService.SetContinuousVcpAsync(
                _selectedMonitor.StableSelectionKey,
                DdcCiService.VcpBrightness,
                val,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _statusFooterText.Text = string.Format("Brightness set to {0}% ({1} ms)", val, res.ElapsedMs);
            }
        }

        private async void OnVolumeDebounceTick(object sender, EventArgs e)
        {
            _volumeDebounce.Stop();
            if (!_pendingVolume.HasValue || _selectedMonitor == null) return;
            uint val = _pendingVolume.Value;
            _pendingVolume = null;

            DdcResult<uint> res = await _ddcService.SetContinuousVcpAsync(
                _selectedMonitor.StableSelectionKey,
                DdcCiService.VcpSpeakerVolume,
                val,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _statusFooterText.Text = string.Format("Speaker Volume set to {0}% ({1} ms)", val, res.ElapsedMs);
            }
        }

        private async void ToggleMonitorMute()
        {
            if (_selectedMonitor == null || !_selectedMonitor.SupportsAudioMute) return;
            bool nextMute = !_isMuted;
            uint vcpVal = nextMute ? 1u : 2u; // MCCS 8D: 1 = Mute, 2 = Unmute
            DdcResult<uint> res = await _ddcService.SetContinuousVcpAsync(
                _selectedMonitor.StableSelectionKey,
                DdcCiService.VcpAudioMute,
                vcpVal,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _isMuted = nextMute;
                _muteGlyph.Text = _isMuted ? "🔇 Muted" : "🔊 Volume";
                _muteGlyph.Foreground = _isMuted ? BrushFromHex("#F87171") : BrushFromHex("#CBD5E1");
                _statusFooterText.Text = _isMuted ? "Monitor audio muted (VCP 8D = 1)" : "Monitor audio unmuted (VCP 8D = 2)";
            }
        }

        private void ToggleAlwaysOnTop()
        {
            _config.AlwaysOnTop = !_config.AlwaysOnTop;
            Topmost = _config.AlwaysOnTop;
            _configService.Save(_config);
            UpdateToolbarHighlights();
            _trayManager.UpdateTrayVisual(_selectedMonitor != null ? _selectedMonitor.EdidMonitorName : null, _config, _activeInputCode);
            _statusFooterText.Text = _config.AlwaysOnTop ? "Pinned window Always on Top" : "Always on Top disabled";
        }

        private void ToggleSettingsDrawer()
        {
            _settingsOpen = !_settingsOpen;
            _settingsDrawerContent.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
            UpdateToolbarHighlights();
        }

        public void RestoreAndActivateWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
            }
            Task ignored = RefreshLiveSnapshotOnlyAsync(_config.QuickDrawerExpanded);
        }

        public void RequestRealExit()
        {
            _allowRealExit = true;
            Close();
        }

        private void ScheduleDisplayRefresh()
        {
            _displayChangeDebounce.Stop();
            _displayChangeDebounce.Start();
        }

        private void OnDisplayChangeDebounceTick(object sender, EventArgs e)
        {
            _displayChangeDebounce.Stop();
            Task ignored = RefreshMonitorsAndLiveStateAsync(false);
        }

        private void OnSystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Dispatcher.BeginInvoke(new Action(ScheduleDisplayRefresh));
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                Task ignored = RefreshMonitorsAndLiveStateAsync(true);
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowRealExit && _config.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            _brightnessDebounce.Stop();
            _volumeDebounce.Stop();
            _displayChangeDebounce.Stop();
            _hotkeyManager.Dispose();
            _trayManager.Dispose();
            _ddcService.Dispose();
        }

        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            SolidColorBrush b = new SolidColorBrush(ColorFromHex(hex));
            b.Freeze();
            return b;
        }
    }
}
