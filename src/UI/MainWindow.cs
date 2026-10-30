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
        private ResolvedMonitorInputMap _activeMap = new ResolvedMonitorInputMap();
        private uint? _activeInputCode;
        private uint? _activeMaxInputCode;
        private bool _isMuted;
        private bool _isSwitchingOrReading;
        private bool _settingsOpen;
        private bool _aboutOpen;
        private bool _allowRealExit;
        private Task _initialLoadTask;

        private readonly DispatcherTimer _brightnessDebounce;
        private readonly DispatcherTimer _volumeDebounce;
        private readonly DispatcherTimer _displayChangeDebounce;
        private uint? _pendingBrightness;
        private uint? _pendingVolume;

        // Toolbar & Header
        private Border _pinButton;
        private TextBlock _pinGlyph;
        private Border _settingsButton;
        private Border _aboutButton;
        private Border _aboutBadgeCircle;
        private TextBlock _aboutBadgeGlyph;
        private Border _liveBadgeBorder;
        private Ellipse _liveBadgeDot;
        private TextBlock _liveBadgeText;

        // Monitor Selector
        private Border _monitorSelectorBtn;
        private TextBlock _monitorTitleText;
        private TextBlock _monitorSubText;
        private Popup _monitorDropdownPopup;
        private StackPanel _monitorDropdownList;

        // Dual Hero Input Cards
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

        // Dynamic Multi-Port Chips Bar (auto-shown when monitor has 3+ inputs like HDMI 1, HDMI 2, DP, USB-C)
        private Border _extraPortsContainer;
        private WrapPanel _extraPortsWrap;

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
        private TextBlock _schemeBadgeText;
        private TextBox _vcpHexInput;
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

        // About Developer Drawer
        private Border _aboutDrawerContent;

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

            _brightnessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
            _brightnessDebounce.Tick += OnBrightnessDebounceTick;

            _volumeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
            _volumeDebounce.Tick += OnVolumeDebounceTick;

            _displayChangeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            _displayChangeDebounce.Tick += OnDisplayChangeDebounceTick;

            Content = BuildRootVisual();

            _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;
            _hotkeyManager.DisplayConfigurationChanged += ScheduleDisplayRefresh;
            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;

            _trayManager.OpenWindowRequested += RestoreAndActivateWindow;
            _trayManager.AboutRequested += OpenAboutDeveloperView;
            _trayManager.SwitchHdmiRequested += delegate { TriggerSwitchToInput(_activeMap.HdmiValue, _activeMap.HdmiLabel); };
            _trayManager.SwitchDpRequested += delegate { TriggerSwitchToInput(_activeMap.DpValue, _activeMap.DpLabel); };
            _trayManager.ToggleInputRequested += TriggerToggleInput;
            _trayManager.SwitchSpecificCodeRequested += TriggerSwitchToInput;
            _trayManager.ToggleAlwaysOnTopRequested += ToggleAlwaysOnTop;
            _trayManager.ExitRequested += RequestRealExit;

            PreviewKeyDown += OnWindowPreviewKeyDown;
            Closing += OnWindowClosing;
            Closed += OnWindowClosed;

            ApplyResolvedMapAndConfigToUi();
        }

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

            outerCard.Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 4,
                Direction = 270,
                Color = Colors.Black,
                Opacity = 0.62
            };

            StackPanel mainStack = new StackPanel();
            mainStack.Orientation = Orientation.Vertical;

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

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(16, 12, 16, 12);

            body.Children.Add(BuildTitleBar());
            body.Children.Add(BuildMonitorSelectorSection());
            body.Children.Add(BuildDualInputCardsSection());
            body.Children.Add(BuildExtraPortsBarSection());
            body.Children.Add(BuildQuickToggleButton());
            body.Children.Add(BuildQuickControlsSection());
            body.Children.Add(BuildSettingsSection());
            body.Children.Add(BuildAboutDeveloperSection());
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

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Border logoPill = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = new LinearGradientBrush(ColorFromHex("#0E7490"), ColorFromHex("#4F46E5"), 45.0),
                Margin = new Thickness(0, 0, 9, 0)
            };
            Path logoPath = new Path
            {
                Data = Geometry.Parse("M 4,6 L 20,6 L 20,16 L 4,16 Z M 9,19 L 15,19 M 12,16 L 12,19"),
                Stroke = Brushes.White,
                StrokeThickness = 1.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoPill.Child = logoPath;
            left.Children.Add(logoPill);

            TextBlock titleText = new TextBlock
            {
                Text = "Input Switcher",
                Foreground = BrushFromHex("#F8FAFC"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            left.Children.Add(titleText);

            _liveBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = BrushFromHex("#0F2922"),
                BorderBrush = BrushFromHex("#10B981"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel badgeStack = new StackPanel { Orientation = Orientation.Horizontal };
            _liveBadgeDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = BrushFromHex("#34D399"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            _liveBadgeText = new TextBlock
            {
                Text = "DDC/CI",
                FontSize = 9.5,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#6EE7B7")
            };
            badgeStack.Children.Add(_liveBadgeDot);
            badgeStack.Children.Add(_liveBadgeText);
            _liveBadgeBorder.Child = badgeStack;
            left.Children.Add(_liveBadgeBorder);

            Grid.SetColumn(left, 0);
            bar.Children.Add(left);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _pinButton = CreateIconToolbarButton("📌", "Always on Top", out _pinGlyph, ToggleAlwaysOnTop, false);
            TextBlock dummy;
            Border refreshBtn = CreateIconToolbarButton("↻", "Refresh Monitors & DDC/CI (F5)", out dummy, delegate
            {
                Task ignored = RefreshMonitorsAndLiveStateAsync(true);
            }, false);
            _settingsButton = CreateIconToolbarButton("⚙", "Monitor Codes, Presets & Hotkeys", out dummy, ToggleSettingsDrawer, false);
            _aboutButton = CreateSmallAboutToolbarButton(ToggleAboutDrawer);
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
            right.Children.Add(_aboutButton);
            right.Children.Add(minBtn);
            right.Children.Add(closeBtn);

            Grid.SetColumn(right, 1);
            bar.Children.Add(right);
            return bar;
        }

        private Border CreateIconToolbarButton(string symbol, string tooltip, out TextBlock glyphOut, Action onClick, bool isDangerHover)
        {
            Border b = new Border
            {
                Width = 23,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };

            TextBlock t = new TextBlock
            {
                Text = symbol,
                FontSize = 11,
                Foreground = BrushFromHex("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
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

        private Border CreateSmallAboutToolbarButton(Action onClick)
        {
            Border b = new Border
            {
                Width = 22,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Margin = new Thickness(1, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "About Monitor Display Switcher & Developer (Mehedi Hasan Shihab)"
            };

            _aboutBadgeCircle = new Border
            {
                Width = 13.5,
                Height = 13.5,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1.15),
                BorderBrush = BrushFromHex("#94A3B8"),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _aboutBadgeGlyph = new TextBlock
            {
                Text = "i",
                FontFamily = new FontFamily("Georgia"),
                FontWeight = FontWeights.Bold,
                FontStyle = FontStyles.Italic,
                FontSize = 8.6,
                Foreground = BrushFromHex("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -0.5, 0, 0)
            };
            _aboutBadgeCircle.Child = _aboutBadgeGlyph;
            b.Child = _aboutBadgeCircle;

            b.MouseEnter += delegate
            {
                b.Background = BrushFromHex("#1E293B");
                _aboutBadgeCircle.BorderBrush = BrushFromHex("#38BDF8");
                _aboutBadgeGlyph.Foreground = Brushes.White;
            };
            b.MouseLeave += delegate
            {
                UpdateToolbarHighlights();
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
            Grid wrap = new Grid { Margin = new Thickness(0, 0, 0, 12) };

            _monitorSelectorBtn = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = BrushFromHex("#131D31"),
                BorderBrush = BrushFromHex("#24324A"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 8, 11, 8),
                Cursor = Cursors.Hand,
                ToolTip = "Click to select among connected monitors"
            };

            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel textCol = new StackPanel();
            _monitorTitleText = new TextBlock
            {
                Text = "Scanning DDC/CI Monitors...",
                Foreground = BrushFromHex("#F1F5F9"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12.2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _monitorSubText = new TextBlock
            {
                Text = "Auto-detecting EDID & VCP 60 profile...",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 10.2,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textCol.Children.Add(_monitorTitleText);
            textCol.Children.Add(_monitorSubText);
            Grid.SetColumn(textCol, 0);
            inner.Children.Add(textCol);

            TextBlock chev = new TextBlock
            {
                Text = "▾",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 2, 0)
            };
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

            _monitorDropdownPopup = new Popup
            {
                PlacementTarget = _monitorSelectorBtn,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                VerticalOffset = 4
            };

            Border popupBorder = new Border
            {
                Width = 352,
                CornerRadius = new CornerRadius(10),
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.65 }
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

                Border item = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    Background = isSel ? BrushFromHex("#1E293B") : Brushes.Transparent
                };

                StackPanel sp = new StackPanel();
                TextBlock l1 = new TextBlock
                {
                    Text = (isSel ? "●  " : "○  ") + m.DropdownLabel,
                    Foreground = isSel ? BrushFromHex("#38BDF8") : BrushFromHex("#F1F5F9"),
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 11.5
                };
                string ddcTag = m.SupportsInputSelect ? "DDC/CI Input Switch Ready" : "Internal / Non-DDC Panel";
                TextBlock l2 = new TextBlock
                {
                    Text = string.Format("     {0} • {1}", string.IsNullOrEmpty(m.AdapterName) ? m.MonitorDeviceName : m.AdapterName, ddcTag),
                    Foreground = BrushFromHex("#64748B"),
                    FontSize = 10
                };
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
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _hdmiCard = BuildSingleInputCard(
                true,
                "M 3,6 L 25,6 L 25,13 L 22,16 L 6,16 L 3,13 Z M 7,10 L 21,10",
                out _hdmiCardTitle,
                out _hdmiCodeBadge,
                out _hdmiActivePill,
                out _hdmiActivePillText,
                out _hdmiHotkeyHint,
                delegate { TriggerSwitchToInput(_activeMap.HdmiValue, _activeMap.HdmiLabel); });
            Grid.SetColumn(_hdmiCard, 0);
            grid.Children.Add(_hdmiCard);

            Border swapBtn = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = BrushFromHex("#162136"),
                BorderBrush = BrushFromHex("#2E405E"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "1-Click Swap HDMI & DisplayPort Codes for this monitor"
            };
            TextBlock swapIcon = new TextBlock
            {
                Text = "⇄",
                FontSize = 13.5,
                Foreground = BrushFromHex("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            swapBtn.MouseLeftButtonUp += delegate { SwapCurrentMonitorHdmiAndDp(); };

            Grid.SetColumn(swapBtn, 1);
            grid.Children.Add(swapBtn);

            _dpCard = BuildSingleInputCard(
                false,
                "M 3,6 L 21,6 L 25,10 L 25,16 L 3,16 Z M 8,11 L 19,11",
                out _dpCardTitle,
                out _dpCodeBadge,
                out _dpActivePill,
                out _dpActivePillText,
                out _dpHotkeyHint,
                delegate { TriggerSwitchToInput(_activeMap.DpValue, _activeMap.DpLabel); });
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
            Border card = new Border
            {
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1.5),
                BorderBrush = BrushFromHex("#233047"),
                Background = BrushFromHex("#131D31"),
                Padding = new Thickness(12, 12, 12, 11),
                Cursor = Cursors.Hand
            };

            StackPanel stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            Border pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 9),
                Background = BrushFromHex("#1E293B")
            };
            TextBlock pillText = new TextBlock
            {
                Text = "SWITCH TO",
                FontSize = 9.5,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#64748B")
            };
            pill.Child = pillText;
            stack.Children.Add(pill);

            Path portIcon = new Path
            {
                Data = Geometry.Parse(portVectorData),
                Stroke = isHdmiSlot ? BrushFromHex("#22D3EE") : BrushFromHex("#A78BFA"),
                StrokeThickness = 1.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 7)
            };
            stack.Children.Add(portIcon);

            TextBlock title = new TextBlock
            {
                Text = isHdmiSlot ? "HDMI" : "DisplayPort",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 15,
                Foreground = BrushFromHex("#F8FAFC"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(title);

            TextBlock codeBadge = new TextBlock
            {
                Text = isHdmiSlot ? "VCP 60 • Code 1" : "VCP 60 • Code 3",
                FontSize = 10.5,
                Foreground = BrushFromHex("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 5)
            };
            stack.Children.Add(codeBadge);

            TextBlock hkHint = new TextBlock
            {
                Text = isHdmiSlot ? "Ctrl+Alt+1" : "Ctrl+Alt+2",
                FontSize = 9.5,
                Foreground = BrushFromHex("#475569"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(hkHint);

            card.Child = stack;

            card.MouseEnter += delegate
            {
                if (!IsSlotCurrentlyActive(isHdmiSlot))
                {
                    card.Background = BrushFromHex("#192640");
                    card.BorderBrush = isHdmiSlot ? BrushFromHex("#0891B2") : BrushFromHex("#7C3AED");
                }
            };
            card.MouseLeave += delegate { UpdateInputCardsVisualState(); };
            card.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };

            titleOut = title;
            codeOut = codeBadge;
            pillOut = pill;
            pillTextOut = pillText;
            hotkeyOut = hkHint;
            return card;
        }

        private UIElement BuildExtraPortsBarSection()
        {
            _extraPortsContainer = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = BrushFromHex("#111A2C"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };

            _extraPortsWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _extraPortsContainer.Child = _extraPortsWrap;
            return _extraPortsContainer;
        }

        private void RebuildExtraPortsChips()
        {
            _extraPortsWrap.Children.Clear();
            if (_activeMap == null || _activeMap.DetectedOptions == null || _activeMap.DetectedOptions.Count <= 2)
            {
                _extraPortsContainer.Visibility = Visibility.Collapsed;
                return;
            }

            _extraPortsContainer.Visibility = Visibility.Visible;
            for (int i = 0; i < _activeMap.DetectedOptions.Count; i++)
            {
                DetectedInputOption opt = _activeMap.DetectedOptions[i];
                bool isActive = _activeInputCode.HasValue && _activeInputCode.Value == opt.Code;

                Border chip = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(3, 2, 3, 2),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Background = isActive ? BrushFromHex("#0E3A4C") : BrushFromHex("#162136"),
                    BorderBrush = isActive ? BrushFromHex("#22D3EE") : BrushFromHex("#2A3A54")
                };

                TextBlock t = new TextBlock
                {
                    Text = (isActive ? "● " : "") + opt.ShortChipLabel,
                    FontSize = 10,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    Foreground = isActive ? BrushFromHex("#67E8F9") : BrushFromHex("#CBD5E1")
                };
                chip.Child = t;

                uint capCode = opt.Code;
                string capName = opt.FriendlyName;
                chip.MouseLeftButtonUp += delegate { TriggerSwitchToInput(capCode, capName); };
                _extraPortsWrap.Children.Add(chip);
            }
        }

        private UIElement BuildQuickToggleButton()
        {
            Border btn = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushFromHex("#2E3E5C"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };

            LinearGradientBrush bg = new LinearGradientBrush(ColorFromHex("#162238"), ColorFromHex("#1D2844"), 0.0);
            btn.Background = bg;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _toggleBtnLabel = new TextBlock
            {
                Text = "⇄   Quick Toggle Input (HDMI ⇄ DisplayPort)",
                Foreground = BrushFromHex("#E2E8F0"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_toggleBtnLabel, 0);
            g.Children.Add(_toggleBtnLabel);

            _toggleBtnHotkey = new TextBlock
            {
                Text = "Ctrl+Alt+S",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 10.2,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            StackPanel section = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            Border header = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = BrushFromHex("#111A2C"),
                BorderBrush = BrushFromHex("#1E293B"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 6, 11, 6),
                Cursor = Cursors.Hand
            };

            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock hTitle = new TextBlock
            {
                Text = "☀  Quick Controls  (Brightness & Speaker Volume)",
                Foreground = BrushFromHex("#94A3B8"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11
            };
            Grid.SetColumn(hTitle, 0);
            hg.Children.Add(hTitle);

            _quickDrawerChevron = new TextBlock
            {
                Text = _config.QuickDrawerExpanded ? "▴" : "▾",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 12
            };
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

            _quickDrawerContent = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = BrushFromHex("#111A2E"),
                BorderBrush = BrushFromHex("#1F2D46"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = _config.QuickDrawerExpanded ? Visibility.Visible : Visibility.Collapsed
            };

            StackPanel slidersStack = new StackPanel();

            _brightnessRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _brightnessRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            TextBlock brLabel = new TextBlock
            {
                Text = "☀ Brightness",
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(brLabel, 0);
            _brightnessRow.Children.Add(brLabel);

            _brightnessSlider = new ModernDarkSlider(ColorFromHex("#06B6D4"), ColorFromHex("#38BDF8"));
            _brightnessSlider.ValuePreviewChanged += delegate(uint v) { _brightnessValueText.Text = v + "%"; };
            _brightnessSlider.ValueCommittedByUser += delegate(uint v)
            {
                _brightnessValueText.Text = v + "%";
                _pendingBrightness = v;
                _brightnessDebounce.Stop();
                _brightnessDebounce.Start();
            };
            Grid.SetColumn(_brightnessSlider, 1);
            _brightnessRow.Children.Add(_brightnessSlider);

            _brightnessValueText = new TextBlock
            {
                Text = "--%",
                Foreground = BrushFromHex("#38BDF8"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_brightnessValueText, 2);
            _brightnessRow.Children.Add(_brightnessValueText);
            slidersStack.Children.Add(_brightnessRow);

            _volumeRow = new Grid();
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            _muteBtn = new Border { CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Toggle Monitor Audio Mute (VCP 8D)" };
            _muteGlyph = new TextBlock { Text = "🔊 Volume", Foreground = BrushFromHex("#CBD5E1"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            _muteBtn.Child = _muteGlyph;
            _muteBtn.MouseLeftButtonUp += delegate { ToggleMonitorMute(); };
            Grid.SetColumn(_muteBtn, 0);
            _volumeRow.Children.Add(_muteBtn);

            _volumeSlider = new ModernDarkSlider(ColorFromHex("#8B5CF6"), ColorFromHex("#A78BFA"));
            _volumeSlider.ValuePreviewChanged += delegate(uint v) { _volumeValueText.Text = v + "%"; };
            _volumeSlider.ValueCommittedByUser += delegate(uint v)
            {
                _volumeValueText.Text = v + "%";
                _pendingVolume = v;
                _volumeDebounce.Stop();
                _volumeDebounce.Start();
            };
            Grid.SetColumn(_volumeSlider, 1);
            _volumeRow.Children.Add(_volumeSlider);

            _volumeValueText = new TextBlock
            {
                Text = "--%",
                Foreground = BrushFromHex("#A78BFA"),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_volumeValueText, 2);
            _volumeRow.Children.Add(_volumeValueText);
            slidersStack.Children.Add(_volumeRow);

            _quickDrawerContent.Child = slidersStack;
            section.Children.Add(_quickDrawerContent);
            return section;
        }

        private UIElement BuildSettingsSection()
        {
            _settingsDrawerContent = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = BrushFromHex("#111A2E"),
                BorderBrush = BrushFromHex("#2E3E5C"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 11),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };

            StackPanel sp = new StackPanel();

            Grid sec1Header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            sec1Header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sec1Header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock sec1 = new TextBlock
            {
                Text = "UNIVERSAL MONITOR INPUT MAPPING",
                FontSize = 9.8,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#38BDF8")
            };
            Grid.SetColumn(sec1, 0);
            sec1Header.Children.Add(sec1);

            _schemeBadgeText = new TextBlock
            {
                Text = "Auto-Detect",
                FontSize = 9.5,
                Foreground = BrushFromHex("#6EE7B7")
            };
            Grid.SetColumn(_schemeBadgeText, 1);
            sec1Header.Children.Add(_schemeBadgeText);
            sp.Children.Add(sec1Header);

            // 1-Click Monitor Preset Pills Row
            WrapPanel presetsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            presetsRow.Children.Add(CreateSchemePresetPill("Auto-Detect", delegate { ClearOverrideToAutoDetect(); }));
            presetsRow.Children.Add(CreateSchemePresetPill("VESA (17 / 15)", delegate { ApplyQuickSchemePreset(0x60, 17, 15); }));
            presetsRow.Children.Add(CreateSchemePresetPill("LG (1 / 3)", delegate { ApplyQuickSchemePreset(0x60, 1, 3); }));
            presetsRow.Children.Add(CreateSchemePresetPill("LG Gen5 (144 / 208)", delegate { ApplyQuickSchemePreset(0xF4, 144, 208); }));
            sp.Children.Add(presetsRow);

            Grid codeGrid = new Grid();
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lVcp = CreateSmallLabel("VCP:");
            lVcp.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(lVcp, 0);
            codeGrid.Children.Add(lVcp);

            _vcpHexInput = CreateDarkTextBox("60");
            _vcpHexInput.ToolTip = "Hex VCP input code (60 standard, F4 on some LG Gen5 models)";
            Grid.SetColumn(_vcpHexInput, 1);
            codeGrid.Children.Add(_vcpHexInput);

            TextBlock lHdmi = CreateSmallLabel("HDMI:");
            lHdmi.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(lHdmi, 3);
            codeGrid.Children.Add(lHdmi);

            _hdmiCodeInput = CreateDarkTextBox("1");
            Grid.SetColumn(_hdmiCodeInput, 4);
            codeGrid.Children.Add(_hdmiCodeInput);

            TextBlock lDp = CreateSmallLabel("DP:");
            lDp.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(lDp, 6);
            codeGrid.Children.Add(lDp);

            _dpCodeInput = CreateDarkTextBox("3");
            Grid.SetColumn(_dpCodeInput, 7);
            codeGrid.Children.Add(_dpCodeInput);

            Border applyCodesBtn = CreateCompactPillButton("Save", "#0284C7", ApplyCustomVcpCodesFromUi);
            applyCodesBtn.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(applyCodesBtn, 8);
            codeGrid.Children.Add(applyCodesBtn);

            sp.Children.Add(codeGrid);

            _possibleValuesHint = new TextBlock
            {
                Text = "Detected Monitor Inputs: probing...",
                FontSize = 10,
                Foreground = BrushFromHex("#64748B"),
                Margin = new Thickness(0, 5, 0, 10)
            };
            sp.Children.Add(_possibleValuesHint);

            TextBlock sec2 = new TextBlock
            {
                Text = "GLOBAL KEYBOARD SHORTCUTS (CLICK BOX & PRESS KEYS)",
                FontSize = 9.8,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#A78BFA"),
                Margin = new Thickness(0, 0, 0, 5)
            };
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

            _hotkeyStatusLabel = new TextBlock
            {
                FontSize = 9.8,
                Foreground = BrushFromHex("#6EE7B7"),
                Margin = new Thickness(0, 3, 0, 8)
            };
            sp.Children.Add(_hotkeyStatusLabel);

            _closeToTrayToggle = CreateDarkToggleSwitch("Close button (✕) minimizes to System Tray", _config.MinimizeToTrayOnClose, out _setCloseToTrayVisual, delegate(bool c)
            {
                _config.MinimizeToTrayOnClose = c;
                _configService.Save(_config);
            });
            sp.Children.Add(_closeToTrayToggle);

            _oneByteMaskToggle = CreateDarkToggleSwitch("Use 1-Byte VCP mask (& 0xFF for LG/Dell/BenQ)", _config.UseOneByteInputMask, out _setOneByteMaskVisual, delegate(bool c)
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

            Grid bottomActions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border resetHkBtn = CreateCompactPillButton("Reset All Auto", "#334155", ResetSettingsDefaults);
            Grid.SetColumn(resetHkBtn, 0);
            bottomActions.Children.Add(resetHkBtn);

            Border quitBtn = CreateCompactPillButton("Exit App Completely", "#B91C1C", RequestRealExit);
            Grid.SetColumn(quitBtn, 2);
            bottomActions.Children.Add(quitBtn);

            sp.Children.Add(bottomActions);
            _settingsDrawerContent.Child = sp;
            return _settingsDrawerContent;
        }

        private UIElement BuildAboutDeveloperSection()
        {
            _aboutDrawerContent = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = BrushFromHex("#111A2E"),
                BorderBrush = BrushFromHex("#2E3E5C"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 11, 12, 11),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };

            StackPanel sp = new StackPanel();

            // Software Hero Header Row
            Grid appRow = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            appRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border heroLogo = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(ColorFromHex("#0E7490"), ColorFromHex("#7C5CFF"), 45.0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Path heroGlyph = new Path
            {
                Data = Geometry.Parse("M 4,6 L 20,6 L 20,16 L 4,16 Z M 9,19 L 15,19 M 12,16 L 12,19"),
                Stroke = Brushes.White,
                StrokeThickness = 1.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            heroLogo.Child = heroGlyph;
            Grid.SetColumn(heroLogo, 0);
            appRow.Children.Add(heroLogo);

            StackPanel appMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            appMeta.Children.Add(new TextBlock
            {
                Text = "Monitor Display Switcher v1.2.0",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12.2,
                Foreground = BrushFromHex("#F8FAFC")
            });
            appMeta.Children.Add(new TextBlock
            {
                Text = "Universal DDC/CI HDMI, DP & USB-C Switcher • AnyCPU",
                FontSize = 9.8,
                Foreground = BrushFromHex("#94A3B8"),
                Margin = new Thickness(0, 1, 0, 0)
            });
            Grid.SetColumn(appMeta, 1);
            appRow.Children.Add(appMeta);
            sp.Children.Add(appRow);

            // Inner Developer Card matching MHS Download Manager About Info
            Border devCard = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = BrushFromHex("#0D1425"),
                BorderBrush = BrushFromHex("#24324A"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 9, 11, 9),
                Margin = new Thickness(0, 0, 0, 9)
            };

            StackPanel devStack = new StackPanel();
            devStack.Children.Add(new TextBlock
            {
                Text = "DEVELOPER & AUTHOR INFORMATION",
                FontSize = 9.5,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#A78BFA"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            Grid devNameRow = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            devNameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            devNameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock devLbl = new TextBlock { Text = "Developer:", FontSize = 10.1, Foreground = BrushFromHex("#94A3B8") };
            TextBlock devVal = new TextBlock
            {
                Text = "Mehedi Hasan Shihab (Web & Software Developer)",
                FontSize = 10.1,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#F8FAFC"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(devLbl, 0);
            Grid.SetColumn(devVal, 1);
            devNameRow.Children.Add(devLbl);
            devNameRow.Children.Add(devVal);
            devStack.Children.Add(devNameRow);

            devStack.Children.Add(new TextBlock
            {
                Text = "Focused on building fast, clean, user-centric desktop tools and full-stack web applications.",
                FontSize = 9.6,
                Foreground = BrushFromHex("#64748B"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 7)
            });

            devStack.Children.Add(CreateDeveloperLinkRow("Portfolio:", "https://sshihabb007.github.io/", "https://sshihabb007.github.io/"));
            devStack.Children.Add(CreateDeveloperLinkRow("Email Me:", "sshihabb007@gmail.com", "mailto:sshihabb007@gmail.com?subject=Monitor%20Display%20Switcher"));
            devStack.Children.Add(CreateDeveloperLinkRow("GitHub:", "github.com/sshihabb007 (@sshihabb007)", "https://github.com/sshihabb007"));
            devStack.Children.Add(CreateDeveloperLinkRow("LinkedIn:", "linkedin.com/in/mehedi-hasan-shihab", "https://www.linkedin.com/in/mehedi-hasan-shihab"));
            devStack.Children.Add(CreateDeveloperLinkRow("Repository:", "github.com/sshihabb007/monitor-display-switcher", "https://github.com/sshihabb007/monitor-display-switcher"));

            devStack.Children.Add(new TextBlock
            {
                Text = "Copyright © 2026 Mehedi Hasan Shihab (sshihabb007) • MIT License",
                FontSize = 9.3,
                Foreground = BrushFromHex("#64748B"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            devCard.Child = devStack;
            sp.Children.Add(devCard);

            // Action Buttons Row (Portfolio, Email Me, GitHub, LinkedIn)
            WrapPanel actionsRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Border btnPort = CreateCompactPillButton("Visit Portfolio", "#7C5CFF", delegate { OpenExternalUri("https://sshihabb007.github.io/"); });
            btnPort.Margin = new Thickness(0, 0, 6, 0);
            Border btnMail = CreateCompactPillButton("Email Me", "#4F46E5", delegate { OpenExternalUri("mailto:sshihabb007@gmail.com?subject=Monitor%20Display%20Switcher"); });
            btnMail.Margin = new Thickness(0, 0, 6, 0);
            Border btnGh = CreateCompactPillButton("GitHub", "#1E293B", delegate { OpenExternalUri("https://github.com/sshihabb007"); });
            btnGh.Margin = new Thickness(0, 0, 6, 0);
            Border btnLi = CreateCompactPillButton("LinkedIn", "#0A66C2", delegate { OpenExternalUri("https://www.linkedin.com/in/mehedi-hasan-shihab"); });

            actionsRow.Children.Add(btnPort);
            actionsRow.Children.Add(btnMail);
            actionsRow.Children.Add(btnGh);
            actionsRow.Children.Add(btnLi);
            sp.Children.Add(actionsRow);

            _aboutDrawerContent.Child = sp;
            return _aboutDrawerContent;
        }

        private UIElement CreateDeveloperLinkRow(string labelText, string displayUrl, string targetUri)
        {
            Grid row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock
            {
                Text = labelText,
                FontSize = 9.7,
                Foreground = BrushFromHex("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            TextBlock link = new TextBlock
            {
                Text = displayUrl,
                FontSize = 9.7,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#818CF8"),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = targetUri
            };
            link.MouseEnter += delegate
            {
                link.Foreground = BrushFromHex("#38BDF8");
                link.TextDecorations = TextDecorations.Underline;
            };
            link.MouseLeave += delegate
            {
                link.Foreground = BrushFromHex("#818CF8");
                link.TextDecorations = null;
            };
            link.MouseLeftButtonUp += delegate
            {
                OpenExternalUri(targetUri);
            };
            Grid.SetColumn(link, 1);
            row.Children.Add(link);
            return row;
        }

        private static void OpenExternalUri(string uri)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(uri);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
            }
        }

        private Border CreateSchemePresetPill(string text, Action onClick)
        {
            Border b = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = BrushFromHex("#18243C"),
                BorderBrush = BrushFromHex("#2A3B59"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 5, 4),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = text,
                FontSize = 9.6,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                Foreground = BrushFromHex("#CBD5E1")
            };
            b.Child = t;
            b.MouseEnter += delegate { b.BorderBrush = BrushFromHex("#38BDF8"); t.Foreground = Brushes.White; };
            b.MouseLeave += delegate { b.BorderBrush = BrushFromHex("#2A3B59"); t.Foreground = BrushFromHex("#CBD5E1"); };
            b.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            return b;
        }

        private UIElement BuildHotkeyCaptureRow(string labelText, out TextBox boxOut, string initialValue, Action<string> onCaptured)
        {
            Grid row = new Grid { Margin = new Thickness(0, 3, 0, 2) };
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
            Grid footer = new Grid { Margin = new Thickness(2, 2, 2, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusFooterText = new TextBlock
            {
                Text = "Initializing universal DDC/CI engine...",
                Foreground = BrushFromHex("#64748B"),
                FontSize = 10.2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_statusFooterText, 0);
            footer.Children.Add(_statusFooterText);

            TextBlock credit = new TextBlock
            {
                Text = "Mehedi Hasan Shihab",
                Foreground = BrushFromHex("#475569"),
                FontSize = 9.8,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "About Developer — Mehedi Hasan Shihab"
            };
            credit.MouseLeftButtonUp += delegate { ToggleAboutDrawer(); };
            Grid.SetColumn(credit, 1);
            footer.Children.Add(credit);
            return footer;
        }

        private TextBlock CreateSmallLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 10.8,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private TextBox CreateDarkTextBox(string initial)
        {
            return new TextBox
            {
                Text = initial,
                Background = BrushFromHex("#0B1120"),
                Foreground = BrushFromHex("#F8FAFC"),
                CaretBrush = Brushes.White,
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 3, 5, 3),
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                TextAlignment = TextAlignment.Center
            };
        }

        private FrameworkElement CreateDarkToggleSwitch(
            string label,
            bool initial,
            out Action<bool> setStateVisualOut,
            Action<bool> onChanged)
        {
            Grid row = new Grid { Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border pillTrack = new Border
            {
                Width = 30,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Ellipse knob = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            pillTrack.Child = knob;
            Grid.SetColumn(pillTrack, 0);
            row.Children.Add(pillTrack);

            TextBlock txt = new TextBlock { Text = label, Foreground = BrushFromHex("#CBD5E1"), FontSize = 10.8, VerticalAlignment = VerticalAlignment.Center };
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
            Border b = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = BrushFromHex(hexColor),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontFamily = new FontFamily("Segoe UI Semibold")
            };
            b.Child = t;
            b.MouseEnter += delegate { b.Opacity = 0.88; };
            b.MouseLeave += delegate { b.Opacity = 1.0; };
            b.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            return b;
        }

        private void RecomputeActiveMonitorMap()
        {
            if (_selectedMonitor == null)
            {
                _activeMap = new ResolvedMonitorInputMap();
                return;
            }

            MonitorInputProfile saved = ConfigService.GetProfileForMonitor(_config, _selectedMonitor.StableSelectionKey);
            byte vcpToQuery = (saved != null && saved.IsUserCustomized && saved.InputVcpCode != 0)
                ? saved.InputVcpCode
                : _selectedMonitor.PreferredInputVcpCode;

            List<byte> discrete = new List<byte>();
            if (_selectedMonitor.Capabilities != null)
            {
                discrete = _selectedMonitor.Capabilities.GetValuesForVcp(vcpToQuery);
                if (discrete.Count == 0)
                {
                    discrete = _selectedMonitor.Capabilities.GetInputSelectValues();
                }
            }

            _activeMap = InputProfileResolver.Resolve(
                _selectedMonitor.ManufacturerCode,
                _selectedMonitor.PreferredInputVcpCode,
                discrete,
                _activeInputCode,
                _activeMaxInputCode,
                saved);
        }

        private void ApplyResolvedMapAndConfigToUi()
        {
            Topmost = _config.AlwaysOnTop;

            _hdmiCardTitle.Text = _activeMap.HdmiLabel;
            _dpCardTitle.Text = _activeMap.DpLabel;
            _hdmiCodeBadge.Text = string.Format("VCP {0:X2} • Code {1}", _activeMap.InputVcpCode, _activeMap.HdmiValue);
            _dpCodeBadge.Text = string.Format("VCP {0:X2} • Code {1}", _activeMap.InputVcpCode, _activeMap.DpValue);

            _toggleBtnLabel.Text = string.Format("⇄   Quick Toggle ({0} ⇄ {1})", _activeMap.HdmiLabel, _activeMap.DpLabel);

            _hdmiHotkeyHint.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.HdmiHotkey)) ? _config.HdmiHotkey : "Click to Switch";
            _dpHotkeyHint.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.DpHotkey)) ? _config.DpHotkey : "Click to Switch";
            _toggleBtnHotkey.Text = (_config.HotkeysEnabled && !string.IsNullOrEmpty(_config.ToggleHotkey)) ? _config.ToggleHotkey : "";

            _schemeBadgeText.Text = _activeMap.DetectionSummary;
            _vcpHexInput.Text = _activeMap.InputVcpCode.ToString("X2");
            _hdmiCodeInput.Text = _activeMap.HdmiValue.ToString();
            _dpCodeInput.Text = _activeMap.DpValue.ToString();

            _hdmiHotkeyBox.Text = _config.HdmiHotkey;
            _dpHotkeyBox.Text = _config.DpHotkey;
            _toggleHotkeyBox.Text = _config.ToggleHotkey;

            if (_selectedMonitor != null)
            {
                string gpuText = !string.IsNullOrEmpty(_selectedMonitor.AdapterName) ? _selectedMonitor.AdapterName : _selectedMonitor.MonitorDeviceName;
                _monitorSubText.Text = string.Format("{0}  •  {1}", gpuText, _activeMap.DetectionSummary);

                if (_activeMap.DetectedOptions.Count > 0)
                {
                    List<string> labels = new List<string>();
                    for (int i = 0; i < _activeMap.DetectedOptions.Count; i++)
                    {
                        labels.Add(_activeMap.DetectedOptions[i].ShortChipLabel);
                    }
                    _possibleValuesHint.Text = "Hardware Capabilities: " + string.Join(", ", labels.ToArray());
                }
                else
                {
                    _possibleValuesHint.Text = "Hardware Capabilities: No discrete list reported (using smart scheme)";
                }
            }

            RebuildExtraPortsChips();
            UpdateToolbarHighlights();
            UpdateInputCardsVisualState();
            _trayManager.UpdateTrayVisual(_selectedMonitor != null ? _selectedMonitor.EdidMonitorName : null, _config, _activeMap, _activeInputCode);
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
            if (_aboutButton != null)
            {
                _aboutButton.Background = _aboutOpen ? BrushFromHex("#0E3A4C") : Brushes.Transparent;
                if (_aboutBadgeCircle != null)
                {
                    _aboutBadgeCircle.BorderBrush = _aboutOpen ? BrushFromHex("#22D3EE") : BrushFromHex("#94A3B8");
                }
                if (_aboutBadgeGlyph != null)
                {
                    _aboutBadgeGlyph.Foreground = _aboutOpen ? BrushFromHex("#67E8F9") : BrushFromHex("#94A3B8");
                }
            }
        }

        private bool IsSlotCurrentlyActive(bool isHdmiSlot)
        {
            if (!_activeInputCode.HasValue) return false;
            return isHdmiSlot
                ? (_activeInputCode.Value == _activeMap.HdmiValue)
                : (_activeInputCode.Value == _activeMap.DpValue);
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
            _statusFooterText.Text = "Auto-detecting displays & DDC/CI capabilities...";

            try
            {
                _monitors = await _ddcService.RefreshMonitorsAsync(probeCapabilities);
                if (_monitors.Count == 0)
                {
                    _selectedMonitor = null;
                    _monitorTitleText.Text = "No DDC/CI Display Found";
                    _monitorSubText.Text = "Check monitor cable or OSD DDC/CI setting";
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
            _activeInputCode = null;
            _activeMaxInputCode = null;
            if (mon == null) return;

            if (saveSelection)
            {
                _config.SelectedMonitorKey = mon.StableSelectionKey;
                _configService.Save(_config);
            }

            _monitorTitleText.Text = mon.DropdownLabel;
            _brightnessRow.IsEnabled = mon.SupportsBrightness;
            _brightnessRow.Opacity = mon.SupportsBrightness ? 1.0 : 0.45;
            _volumeRow.IsEnabled = mon.SupportsVolume;
            _volumeRow.Opacity = mon.SupportsVolume ? 1.0 : 0.45;

            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();

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
                MonitorInputProfile saved = ConfigService.GetProfileForMonitor(_config, _selectedMonitor.StableSelectionKey);
                byte? customVcp = (saved != null && saved.IsUserCustomized) ? (byte?)saved.InputVcpCode : null;

                DdcResult<MonitorLiveSnapshot> res = await _ddcService.ReadLiveSnapshotAsync(
                    _selectedMonitor.StableSelectionKey,
                    customVcp,
                    _config.UseOneByteInputMask,
                    _config.RetryOnI2cFailure,
                    includeQuickControls);

                if (res.IsSuccess)
                {
                    MonitorLiveSnapshot snap = res.Value;
                    _activeInputCode = snap.CurrentInputValue;
                    _activeMaxInputCode = snap.MaxInputValue;

                    if (snap.Brightness.HasValue)
                    {
                        uint bMax = snap.BrightnessMax.HasValue ? snap.BrightnessMax.Value : 100u;
                        _brightnessSlider.SetValueFromHardware(snap.Brightness.Value, bMax);
                        _brightnessValueText.Text = snap.Brightness.Value + "%";
                    }

                    if (snap.Volume.HasValue)
                    {
                        uint vMax = snap.VolumeMax.HasValue ? snap.VolumeMax.Value : 100u;
                        _volumeSlider.SetValueFromHardware(snap.Volume.Value, vMax);
                        _volumeValueText.Text = snap.Volume.Value + "%";
                    }

                    if (snap.IsAudioMuted.HasValue)
                    {
                        _isMuted = snap.IsAudioMuted.Value;
                        _muteGlyph.Text = _isMuted ? "🔇 Muted" : "🔊 Volume";
                        _muteGlyph.Foreground = _isMuted ? BrushFromHex("#F87171") : BrushFromHex("#CBD5E1");
                    }

                    RecomputeActiveMonitorMap();
                    SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                    _statusFooterText.Text = string.Format("Active: {0}  •  DDC/CI {1} ms", DescribeInputCode(_activeInputCode), snap.ReadLatencyMs);
                }
                else
                {
                    SetLiveBadge("STANDBY", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
                    _statusFooterText.Text = res.ErrorMessage;
                }

                ApplyResolvedMapAndConfigToUi();
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
            _statusFooterText.Text = string.Format("Switching {0} to {1} (VCP {2:X2} = {3})...", _selectedMonitor.FriendlyTitle, friendlyLabel, _activeMap.InputVcpCode, targetValue);

            DdcResult<uint> res = await _ddcService.SetInputSourceAsync(
                _selectedMonitor.StableSelectionKey,
                _activeMap.InputVcpCode,
                targetValue,
                _config.UseOneByteInputMask,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _activeInputCode = res.Value;
                RecomputeActiveMonitorMap();
                SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                _statusFooterText.Text = string.Format("Switched to {0} (Code {1})  •  {2} ms", friendlyLabel, _activeInputCode.Value, res.ElapsedMs);
            }
            else
            {
                SetLiveBadge("I2C WARN", "#331418", "#EF4444", "#F87171", "#FCA5A5");
                _statusFooterText.Text = res.ErrorMessage;
            }

            ApplyResolvedMapAndConfigToUi();
        }

        private async void TriggerToggleInput()
        {
            if (_selectedMonitor == null)
            {
                await RefreshMonitorsAndLiveStateAsync(false);
                if (_selectedMonitor == null) return;
            }

            SetLiveBadge("TOGGLING", "#2E2311", "#F59E0B", "#FBBF24", "#FDE68A");
            _statusFooterText.Text = string.Format("Toggling {0} ⇄ {1} over DDC/CI...", _activeMap.HdmiLabel, _activeMap.DpLabel);

            DdcResult<uint> res = await _ddcService.ToggleInputSourceAsync(
                _selectedMonitor.StableSelectionKey,
                _activeMap.InputVcpCode,
                _activeMap.HdmiValue,
                _activeMap.DpValue,
                _activeInputCode,
                _config.UseOneByteInputMask,
                _config.RetryOnI2cFailure);

            if (res.IsSuccess)
            {
                _activeInputCode = res.Value;
                RecomputeActiveMonitorMap();
                SetLiveBadge("READY", "#0F2922", "#10B981", "#34D399", "#6EE7B7");
                _statusFooterText.Text = string.Format("Switched to {0}  •  {1} ms", DescribeInputCode(_activeInputCode), res.ElapsedMs);
            }
            else
            {
                SetLiveBadge("I2C WARN", "#331418", "#EF4444", "#F87171", "#FCA5A5");
                _statusFooterText.Text = res.ErrorMessage;
            }

            ApplyResolvedMapAndConfigToUi();
        }

        private string DescribeInputCode(uint? code)
        {
            if (!code.HasValue) return "Unknown";
            if (code.Value == _activeMap.HdmiValue) return string.Format("{0} (Code {1})", _activeMap.HdmiLabel, code.Value);
            if (code.Value == _activeMap.DpValue) return string.Format("{0} (Code {1})", _activeMap.DpLabel, code.Value);
            for (int i = 0; i < _activeMap.DetectedOptions.Count; i++)
            {
                if (_activeMap.DetectedOptions[i].Code == code.Value)
                {
                    return _activeMap.DetectedOptions[i].ShortChipLabel;
                }
            }
            return string.Format("Input Code {0}", code.Value);
        }

        private void SwapCurrentMonitorHdmiAndDp()
        {
            if (_selectedMonitor == null) return;
            ConfigService.SaveCustomProfileForMonitor(
                _config,
                _selectedMonitor.StableSelectionKey,
                _activeMap.InputVcpCode,
                _activeMap.DpValue,
                _activeMap.HdmiValue,
                "HDMI",
                "DisplayPort");
            _configService.Save(_config);
            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();
            _statusFooterText.Text = string.Format("Swapped for {0}: HDMI = {1}, DP = {2}", _selectedMonitor.FriendlyTitle, _activeMap.HdmiValue, _activeMap.DpValue);
        }

        private void ApplyQuickSchemePreset(byte vcpCode, uint hdmiVal, uint dpVal)
        {
            if (_selectedMonitor == null) return;
            ConfigService.SaveCustomProfileForMonitor(
                _config,
                _selectedMonitor.StableSelectionKey,
                vcpCode,
                hdmiVal,
                dpVal,
                "HDMI",
                "DisplayPort");
            _configService.Save(_config);
            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();
            Task ignored = RefreshLiveSnapshotOnlyAsync(false);
        }

        private void ClearOverrideToAutoDetect()
        {
            if (_selectedMonitor != null)
            {
                ConfigService.ClearCustomProfileForMonitor(_config, _selectedMonitor.StableSelectionKey);
                _configService.Save(_config);
            }
            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();
            Task ignored = RefreshLiveSnapshotOnlyAsync(false);
            _statusFooterText.Text = "Reverted monitor profile to automatic hardware detection.";
        }

        private void ApplyCustomVcpCodesFromUi()
        {
            if (_selectedMonitor == null) return;
            byte vcpByte;
            uint hVal, dVal;
            if (!byte.TryParse(_vcpHexInput.Text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vcpByte) || vcpByte == 0)
            {
                _statusFooterText.Text = "Enter a valid hex VCP code (e.g. 60 or F4).";
                return;
            }
            if (!uint.TryParse(_hdmiCodeInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hVal) || hVal > 255 ||
                !uint.TryParse(_dpCodeInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dVal) || dVal > 255)
            {
                _statusFooterText.Text = "Enter valid integer input codes between 0 and 255.";
                return;
            }

            ConfigService.SaveCustomProfileForMonitor(
                _config,
                _selectedMonitor.StableSelectionKey,
                vcpByte,
                hVal,
                dVal,
                "HDMI",
                "DisplayPort");
            _configService.Save(_config);
            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();
            _statusFooterText.Text = string.Format("Saved profile for {0}: VCP {1:X2} (HDMI={2}, DP={3})", _selectedMonitor.FriendlyTitle, vcpByte, hVal, dVal);
        }

        private void ResetSettingsDefaults()
        {
            _config.MonitorProfiles.Clear();
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
            RecomputeActiveMonitorMap();
            ApplyResolvedMapAndConfigToUi();
            RefreshHotkeys();
            _statusFooterText.Text = "Restored universal auto-detection and default global shortcuts.";
        }

        private void SaveAndRefreshHotkeys()
        {
            _configService.Save(_config);
            ApplyResolvedMapAndConfigToUi();
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
                TriggerSwitchToInput(_activeMap.HdmiValue, _activeMap.HdmiLabel);
            }
            else if (action == HotkeyAction.SwitchToDp)
            {
                TriggerSwitchToInput(_activeMap.DpValue, _activeMap.DpLabel);
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
            uint vcpVal = nextMute ? 1u : 2u;
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
            _trayManager.UpdateTrayVisual(_selectedMonitor != null ? _selectedMonitor.EdidMonitorName : null, _config, _activeMap, _activeInputCode);
            _statusFooterText.Text = _config.AlwaysOnTop ? "Pinned window Always on Top" : "Always on Top disabled";
        }

        private void ToggleSettingsDrawer()
        {
            _settingsOpen = !_settingsOpen;
            if (_settingsOpen && _aboutOpen)
            {
                _aboutOpen = false;
                _aboutDrawerContent.Visibility = Visibility.Collapsed;
            }
            _settingsDrawerContent.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
            UpdateToolbarHighlights();
        }

        private void ToggleAboutDrawer()
        {
            _aboutOpen = !_aboutOpen;
            if (_aboutOpen && _settingsOpen)
            {
                _settingsOpen = false;
                _settingsDrawerContent.Visibility = Visibility.Collapsed;
            }
            _aboutDrawerContent.Visibility = _aboutOpen ? Visibility.Visible : Visibility.Collapsed;
            UpdateToolbarHighlights();
        }

        private void OpenAboutDeveloperView()
        {
            RestoreAndActivateWindow();
            if (!_aboutOpen)
            {
                ToggleAboutDrawer();
            }
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

                _settingsOpen = false;
                _settingsDrawerContent.Visibility = Visibility.Collapsed;
                _quickDrawerContent.Visibility = Visibility.Collapsed;
                _quickDrawerChevron.Text = "▾";
                _aboutOpen = true;
                _aboutDrawerContent.Visibility = Visibility.Visible;
                UpdateToolbarHighlights();
                await Task.Delay(120);
                UpdateLayout();
                SaveWindowPng(System.IO.Path.Combine(outputDir, "ui_about.png"));
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
            double scaleX = 1.0;
            double scaleY = 1.0;
            PresentationSource src = PresentationSource.FromVisual(visual);
            if (src != null && src.CompositionTarget != null)
            {
                scaleX = src.CompositionTarget.TransformToDevice.M11;
                scaleY = src.CompositionTarget.TransformToDevice.M22;
            }
            int pxW = Math.Max(1, (int)Math.Ceiling(ActualWidth * scaleX));
            int pxH = Math.Max(1, (int)Math.Ceiling(ActualHeight * scaleY));
            RenderTargetBitmap rtb = new RenderTargetBitmap(
                pxW,
                pxH,
                96.0 * scaleX,
                96.0 * scaleY,
                PixelFormats.Pbgra32);
            rtb.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (System.IO.FileStream fs = new System.IO.FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            {
                encoder.Save(fs);
            }
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
