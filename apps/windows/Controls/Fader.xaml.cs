using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace NextXLR.Controls;

/// <summary>
/// Vertical studio fader (0–100) with integrated live level meter and dB scale ticks.
/// · Symmetrical 3-column studio assembly: LevelMeter (left) | Fader Track & Knob (center) | Scale Ticks (right).
/// · The fader knob is centered on the card's vertical axis.
/// · Grabbing the cap drags relative to where you grabbed it.
/// · Clicking the track glides the cap there, then keeps dragging.
/// · Hold Shift for fine control (×0.2). Wheel ±1 (Ctrl: ±0.1). Arrows ±1, PageUp/Down ±10.
/// · External changes (hardware fader, daemon broadcasts) glide smoothly instead of snapping.
/// </summary>
public sealed partial class Fader : UserControl
{
    private const double CapHeight = 34;
    private const double GlideTimeConstant = 0.06; // seconds; critically damped approach

    private static readonly SolidColorBrush StudioWhiteBrush = new(Windows.UI.Color.FromArgb(255, 0xF5, 0xF5, 0xF5));
    private static readonly SolidColorBrush TickMajorBrushDark = new(ColorHelper.FromArgb(255, 0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush TickMinorBrushDark = new(ColorHelper.FromArgb(255, 0x55, 0x55, 0x55));
    private static readonly SolidColorBrush TickMajorBrushLight = new(ColorHelper.FromArgb(255, 0x6E, 0x6E, 0x6E));
    private static readonly SolidColorBrush TickMinorBrushLight = new(ColorHelper.FromArgb(255, 0xB0, 0xB0, 0xB0));

    private double _shown;          // what is drawn (eases toward Value)
    private bool _dragging;
    private double _grabOffset;     // pointer Y − cap top at grab time
    private bool _fine;
    private double _lastY;
    private bool _animating;
    private readonly Stopwatch _clock = new();

    public event EventHandler? DragStarted;
    public event EventHandler? DragCompleted;
    public Action<double>? ValueChangedByUser;

    public Fader()
    {
        InitializeComponent();
        MeterGradient.Opacity = 0.0;
        MeterMask.Height = 1000;
        MeterHold.Opacity = 0.0;
        UpdateIndicatorBrush();

        ActualThemeChanged += (_, _) => { UpdateThemeStyling(); DrawTicks(); };
        Loaded += (_, _) => { UpdateThemeStyling(); DrawTicks(); };
        Root.PointerPressed += OnPressed;
        Root.PointerMoved += OnMoved;
        Root.PointerReleased += OnReleased;
        Root.PointerCaptureLost += (_, _) => EndDrag();
        Root.PointerWheelChanged += OnWheel;
        Root.SizeChanged += (_, _) => { DrawTicks(); Layout(); };
        MeterHost.SizeChanged += (_, _) => UpdateMeterLevels();
        KeyDown += OnKey;
        try
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        }
        catch { }
        Unloaded += (_, _) => StopAnimation();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Fader), new PropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Fader), new PropertyMetadata(0.0, OnRangeChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Fader), new PropertyMetadata(100.0, OnRangeChanged));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(Fader), new PropertyMetadata(null, OnAccentChanged));
    public static readonly DependencyProperty MeterLevelProperty = DependencyProperty.Register(
        nameof(MeterLevel), typeof(double), typeof(Fader), new PropertyMetadata(0.0, (d, _) => ((Fader)d).UpdateMeterLevels()));
    public static readonly DependencyProperty PeakHoldProperty = DependencyProperty.Register(
        nameof(PeakHold), typeof(double), typeof(Fader), new PropertyMetadata(0.0, (d, _) => ((Fader)d).UpdateMeterLevels()));
    public static readonly DependencyProperty IsMeteredProperty = DependencyProperty.Register(
        nameof(IsMetered), typeof(bool), typeof(Fader), new PropertyMetadata(true, (d, _) => ((Fader)d).UpdateMeteredState()));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Minimum–Maximum, 0.1 resolution.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Round(Math.Clamp(value, Minimum, Maximum), 1));
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double MeterLevel
    {
        get => (double)GetValue(MeterLevelProperty);
        set => SetValue(MeterLevelProperty, value);
    }

    public double PeakHold
    {
        get => (double)GetValue(PeakHoldProperty);
        set => SetValue(PeakHoldProperty, value);
    }

    public bool IsMetered
    {
        get => (bool)GetValue(IsMeteredProperty);
        set => SetValue(IsMeteredProperty, value);
    }

    public bool IsDragging => _dragging;

    public Brush CapIndicatorBrush
    {
        get
        {
            if (Accent is SolidColorBrush sb)
            {
                // When neutral (#5E6675), return high-visibility studio white so all channels are clearly visible
                if (sb.Color.R == 0x5E && sb.Color.G == 0x66 && sb.Color.B == 0x75)
                    return StudioWhiteBrush;
                return Accent;
            }
            return StudioWhiteBrush;
        }
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var f = (Fader)d;
        f.UpdateIndicatorBrush();
    }

    private void UpdateIndicatorBrush()
    {
        if (IndicatorLine is not null)
        {
            IndicatorLine.Background = CapIndicatorBrush;
        }
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var f = (Fader)d;
        f.Value = Math.Clamp(f.Value, f.Minimum, f.Maximum);
        f._shown = f.Value;
        f.Layout();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var f = (Fader)d;
        if (f._dragging || !f.IsLoaded) { f._shown = f.Value; f.Layout(); }
        else f.StartAnimation();   // glide to externally set values
    }

    // ---- Geometry -----------------------------------------------------------------------

    private double Travel => Math.Max(1, Root.ActualHeight - CapHeight);
    private double Range => Math.Max(0.001, Maximum - Minimum);
    private double ValueToTop(double v) => (1 - (Math.Clamp(v, Minimum, Maximum) - Minimum) / Range) * Travel;
    private double TopToValue(double top) => Minimum + (1 - top / Travel) * Range;

    private void Layout()
    {
        if (Root.ActualHeight <= 0) return;
        var top = ValueToTop(_shown);
        CapOffset.Y = top;

        var v = Math.Clamp(_shown / 100.0, 0.0, 1.0);
        string dbStr = v >= 0.999 ? "0.0 dB" : v <= 0.001 ? "-∞ dB" : $"{Math.Round(60.0 * Math.Log10(v), 1):0.0} dB";
        ToolTipService.SetToolTip(Cap, $"{dbStr} ({Math.Round(_shown)}%)");

        UpdateMeterLevels();
    }

    private void DrawTicks()
    {
        MeterHost.Margin = new Thickness(2, CapHeight / 2, 0, CapHeight / 2);
        Track.Margin = new Thickness(0, CapHeight / 2, 0, CapHeight / 2);
        Ticks.Children.Clear();
        if (Root.ActualHeight <= 0) return;

        var startY = CapHeight / 2;
        var travel = Travel;
        var w = Root.ActualWidth > 0 ? Root.ActualWidth : (double.IsNaN(Width) ? 48.0 : Width);

        var isDark = ActualTheme != ElementTheme.Light;
        var majorBrush = isDark ? TickMajorBrushDark : TickMajorBrushLight;
        var minorBrush = isDark ? TickMinorBrushDark : TickMinorBrushLight;

        // Calibrated scale ticks on the right (neutral grey) at 100%, 75%, 50%, 25%, 0%
        for (var i = 0; i <= 4; i++)
        {
            var y = startY + travel * i / 4.0;
            var major = (i % 2 == 0); // 100%, 50%, 0%
            var len = major ? 5.0 : 3.5;
            var brush = major ? majorBrush : minorBrush;
            var tickX = Math.Max(w - len - 1.5, (w / 2.0) + (Cap.Width / 2.0) + 1.5);

            Ticks.Children.Add(new Rectangle
            {
                Width = len,
                Height = 1,
                Fill = brush,
                Margin = new Thickness(tickX, y - 0.5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });
        }
    }

    private void UpdateMeteredState()
    {
        var on = IsMetered ? Visibility.Visible : Visibility.Collapsed;
        var off = IsMetered ? Visibility.Collapsed : Visibility.Visible;
        if (MeterGradient.Visibility != on) MeterGradient.Visibility = on;
        if (MeterMask.Visibility != on) MeterMask.Visibility = on;
        if (MeterHold.Visibility != on) MeterHold.Visibility = on;
        if (NoMeterGuide.Visibility != off) NoMeterGuide.Visibility = off;
    }

    private void UpdateThemeStyling()
    {
        bool isDark = ActualTheme != ElementTheme.Light;
        if (isDark)
        {
            // Gunmetal metallic studio fader cap
            if (CapBevel != null) CapBevel.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x50, 0x50, 0x50));
            if (CapBg?.GradientStops.Count >= 6)
            {
                CapBg.GradientStops[0].Color = ColorHelper.FromArgb(255, 0x5A, 0x5A, 0x5A);
                CapBg.GradientStops[1].Color = ColorHelper.FromArgb(255, 0x3C, 0x3C, 0x3C);
                CapBg.GradientStops[2].Color = ColorHelper.FromArgb(255, 0x26, 0x26, 0x26);
                CapBg.GradientStops[3].Color = ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x1E);
                CapBg.GradientStops[4].Color = ColorHelper.FromArgb(255, 0x2D, 0x2D, 0x2D);
                CapBg.GradientStops[5].Color = ColorHelper.FromArgb(255, 0x18, 0x18, 0x18);
            }

            if (GripRidgeTop1 != null) GripRidgeTop1.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x14, 0x14, 0x14));
            if (GripRidgeTop2 != null) GripRidgeTop2.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x4A, 0x4A, 0x4A));
            if (GripRidgeBottom1 != null) GripRidgeBottom1.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x14, 0x14, 0x14));
            if (GripRidgeBottom2 != null) GripRidgeBottom2.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x4A, 0x4A, 0x4A));

            if (Track != null)
            {
                Track.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x12, 0x13, 0x16));
                Track.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x29, 0x2C, 0x35));
            }

            if (MeterTrackBorder != null)
            {
                MeterTrackBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x12, 0x13, 0x16));
                MeterTrackBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0x29, 0x2C, 0x35));
            }
            if (MeterMask != null) MeterMask.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x12, 0x13, 0x16));
        }
        else
        {
            // Brushed Natural Aluminum (Silber)
            if (CapBevel != null) CapBevel.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0xB8, 0xBE, 0xC8));
            if (CapBg?.GradientStops.Count >= 6)
            {
                CapBg.GradientStops[0].Color = ColorHelper.FromArgb(255, 0xFA, 0xFA, 0xFC);
                CapBg.GradientStops[1].Color = ColorHelper.FromArgb(255, 0xE8, 0xEC, 0xF2);
                CapBg.GradientStops[2].Color = ColorHelper.FromArgb(255, 0xDC, 0xE1, 0xE8);
                CapBg.GradientStops[3].Color = ColorHelper.FromArgb(255, 0xD0, 0xD6, 0xDF);
                CapBg.GradientStops[4].Color = ColorHelper.FromArgb(255, 0xE2, 0xE6, 0xED);
                CapBg.GradientStops[5].Color = ColorHelper.FromArgb(255, 0xC4, 0xCA, 0xD4);
            }

            if (GripRidgeTop1 != null) GripRidgeTop1.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xA0, 0xA8, 0xB4));
            if (GripRidgeTop2 != null) GripRidgeTop2.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF));
            if (GripRidgeBottom1 != null) GripRidgeBottom1.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xA0, 0xA8, 0xB4));
            if (GripRidgeBottom2 != null) GripRidgeBottom2.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF));

            if (Track != null)
            {
                Track.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xEC, 0xEA, 0xE4));
                Track.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0xD8, 0xD5, 0xCC));
            }

            if (MeterTrackBorder != null)
            {
                MeterTrackBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xEC, 0xEA, 0xE4));
                MeterTrackBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0xD8, 0xD5, 0xCC));
            }
            if (MeterMask != null) MeterMask.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0xEC, 0xEA, 0xE4));
        }
    }

    private void UpdateMeterLevels()
    {
        if (!IsMetered) return;
        var h = Travel;
        if (h <= 0) return;

        var level = Math.Clamp(MeterLevel, 0, 1);
        var hold = Math.Clamp(PeakHold, 0, 1);

        if (level <= 0.001)
        {
            if (MeterGradient.Opacity != 0.0) MeterGradient.Opacity = 0.0;
            var targetH = h + 10;
            if (double.IsNaN(MeterMask.Height) || Math.Abs(MeterMask.Height - targetH) >= 0.2)
            {
                MeterMask.Height = targetH;
            }
        }
        else
        {
            if (MeterGradient.Opacity != 1.0) MeterGradient.Opacity = 1.0;
            var maskH = (1 - level) * h;
            if (double.IsNaN(MeterMask.Height) || Math.Abs(MeterMask.Height - maskH) >= 0.2)
            {
                MeterMask.Height = maskH;
            }
        }

        if (hold > 0.005)
        {
            var holdY = (1 - hold) * Math.Max(0, h - 2);
            if (double.IsNaN(MeterHoldOffset.Y) || Math.Abs(MeterHoldOffset.Y - holdY) >= 0.2)
            {
                MeterHoldOffset.Y = holdY;
            }
            if (MeterHold.Opacity != 0.9) MeterHold.Opacity = 0.9;
        }
        else
        {
            if (MeterHold.Opacity != 0.0) MeterHold.Opacity = 0.0;
        }
    }

    // ---- Smooth interpolation -------------------------------------------------------------

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, object e)
    {
        var dt = Math.Min(_clock.Elapsed.TotalSeconds, 0.05);
        _clock.Restart();
        var k = 1 - Math.Exp(-dt / GlideTimeConstant);
        _shown += (Value - _shown) * k;
        if (Math.Abs(Value - _shown) < 0.05) { _shown = Value; StopAnimation(); }
        Layout();
    }

    // ---- Input ----------------------------------------------------------------------------

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var y = e.GetCurrentPoint(Root).Position.Y;
        var capTop = ValueToTop(_shown);
        // On the cap: keep the grab point. On the track: centre the cap on the pointer.
        _grabOffset = y >= capTop && y <= capTop + CapHeight ? y - capTop : CapHeight / 2;
        _fine = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        _lastY = y;
        _dragging = true;
        StopAnimation();
        Root.CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
        DragStarted?.Invoke(this, EventArgs.Empty);
        if (_grabOffset == CapHeight / 2)
        {
            Value = TopToValue(y - _grabOffset);
            ValueChangedByUser?.Invoke(Value);
        }
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var y = e.GetCurrentPoint(Root).Position.Y;
        var fine = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        if (fine)
        {
            // Fine mode: pointer movement is scaled down 5×.
            Value -= (y - _lastY) / Travel * Range * 0.2;
        }
        else
        {
            // Leaving fine mode: re-anchor so the cap doesn't jump to the pointer.
            if (_fine) _grabOffset = y - ValueToTop(Value);
            Value = TopToValue(y - _grabOffset);
        }
        _fine = fine;
        _lastY = y;
        ValueChangedByUser?.Invoke(Value);
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        Root.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var hasModifier = (e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)) != 0;
        var isFocused = FocusState != FocusState.Unfocused;
        if (!hasModifier && !isFocused)
        {
            // Scrolling on page: do not change slider and let parent ScrollViewer handle wheel
            return;
        }

        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        var step = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0 ? 0.1 : 1;
        Value += Math.Sign(delta) * step;
        ValueChangedByUser?.Invoke(Value);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        double? next = e.Key switch
        {
            VirtualKey.Up or VirtualKey.Right => Value + 1,
            VirtualKey.Down or VirtualKey.Left => Value - 1,
            VirtualKey.PageUp => Value + 10,
            VirtualKey.PageDown => Value - 10,
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            _ => null,
        };
        if (next is { } v)
        {
            Value = v;
            ValueChangedByUser?.Invoke(Value);
            e.Handled = true;
        }
    }
}
