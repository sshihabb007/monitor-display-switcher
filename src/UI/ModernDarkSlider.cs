using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DisplayInputSwitcher.UI
{
    /// <summary>
    /// Sleek custom rounded dark slider with proportional accent fill bar and draggable thumb.
    /// Avoids legacy Aero slider chrome on dark backgrounds.
    /// </summary>
    public sealed class ModernDarkSlider : Grid
    {
        private readonly Border _trackBg;
        private readonly Border _trackFill;
        private readonly Ellipse _thumb;
        private readonly Canvas _overlayCanvas;

        private double _minimum = 0.0;
        private double _maximum = 100.0;
        private double _value = 50.0;
        private bool _isDragging;

        public event Action<uint> ValueCommittedByUser;
        public event Action<uint> ValuePreviewChanged;

        public ModernDarkSlider(Color accentStart, Color accentEnd)
        {
            Height = 24;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            _trackBg = new Border();
            _trackBg.Height = 6;
            _trackBg.CornerRadius = new CornerRadius(3);
            _trackBg.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            _trackBg.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            _trackBg.BorderThickness = new Thickness(1);
            _trackBg.VerticalAlignment = VerticalAlignment.Center;
            _trackBg.Margin = new Thickness(7, 0, 7, 0);

            _trackFill = new Border();
            _trackFill.Height = 6;
            _trackFill.CornerRadius = new CornerRadius(3);
            _trackFill.HorizontalAlignment = HorizontalAlignment.Left;
            _trackFill.VerticalAlignment = VerticalAlignment.Center;
            _trackFill.Margin = new Thickness(7, 0, 7, 0);
            _trackFill.Background = new LinearGradientBrush(accentStart, accentEnd, 0.0);

            _overlayCanvas = new Canvas();
            _overlayCanvas.VerticalAlignment = VerticalAlignment.Center;
            _overlayCanvas.Height = 24;
            _overlayCanvas.IsHitTestVisible = false;

            _thumb = new Ellipse();
            _thumb.Width = 14;
            _thumb.Height = 14;
            _thumb.Fill = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            _thumb.Stroke = new SolidColorBrush(accentEnd);
            _thumb.StrokeThickness = 2.0;

            _overlayCanvas.Children.Add(_thumb);

            Children.Add(_trackBg);
            Children.Add(_trackFill);
            Children.Add(_overlayCanvas);

            SizeChanged += delegate { UpdateVisualPosition(); };
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
        }

        public double Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = Math.Max(1.0, value);
                if (_value > _maximum) _value = _maximum;
                UpdateVisualPosition();
            }
        }

        public uint CurrentValue
        {
            get { return (uint)Math.Round(_value); }
        }

        public void SetValueFromHardware(uint val, uint max)
        {
            if (_isDragging) return;
            _maximum = Math.Max(1.0, (double)max);
            _value = Math.Max(_minimum, Math.Min(_maximum, (double)val));
            UpdateVisualPosition();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled) return;
            _isDragging = true;
            CaptureMouse();
            UpdateFromPoint(e.GetPosition(this).X, true);
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            UpdateFromPoint(e.GetPosition(this).X, false);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            ReleaseMouseCapture();
            UpdateFromPoint(e.GetPosition(this).X, true);
            e.Handled = true;
        }

        private void UpdateFromPoint(double x, bool commit)
        {
            double usable = Math.Max(1.0, ActualWidth - 14.0);
            double clamped = Math.Max(0.0, Math.Min(usable, x - 7.0));
            double ratio = clamped / usable;
            double next = Math.Round(_minimum + ratio * (_maximum - _minimum));
            bool changed = ((uint)next != (uint)Math.Round(_value));
            _value = next;
            UpdateVisualPosition();

            if (changed && ValuePreviewChanged != null)
            {
                ValuePreviewChanged((uint)next);
            }
            if ((changed || commit) && ValueCommittedByUser != null)
            {
                ValueCommittedByUser((uint)next);
            }
        }

        private void UpdateVisualPosition()
        {
            double usable = Math.Max(0.0, ActualWidth - 14.0);
            double ratio = (_maximum > _minimum) ? (_value - _minimum) / (_maximum - _minimum) : 0.0;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));

            double fillWidth = usable * ratio;
            _trackFill.Width = fillWidth;
            Canvas.SetLeft(_thumb, 7.0 + fillWidth - 7.0);
            Canvas.SetTop(_thumb, 5.0);
        }
    }
}

