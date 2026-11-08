using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace NextXLR.Controls;

/// <summary>
/// Rotary dial (270° sweep) like the GoXLR's encoders.
/// Drag up/down (or left/right) to turn · mouse wheel · arrow keys / PageUp/PageDown / Home/End ·
/// double-click to reset to <see cref="DefaultValue"/>. Exposed to screen readers as a slider
/// (UIA RangeValue pattern).
/// </summary>
public sealed partial class Knob : UserControl
{
    private const double StartAngle = 135;    // degrees, clockwise from +x (bottom-left)
    private const double Sweep = 270;
    private const double DragPixelsForFullRange = 220;

    private Point? _dragOrigin;
    private double _dragStartValue;

    public Knob()
    {
        InitializeComponent();
        Dial.PointerPressed += OnPointerPressed;
        Dial.PointerMoved += OnPointerMoved;
        Dial.PointerReleased += OnPointerReleased;
        Dial.PointerCaptureLost += (_, _) => _dragOrigin = null;
        Dial.PointerWheelChanged += OnWheel;
        Dial.DoubleTapped += (_, _) => Value = DefaultValue;
        KeyDown += OnKeyDown;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        Loaded += (_, _) => Redraw();
    }

    // ---- Dependency properties -----------------------------------------------------------

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob), new PropertyMetadata(0.0, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Knob), new PropertyMetadata(0.0, (d, _) => ((Knob)d).Redraw()));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Knob), new PropertyMetadata(100.0, (d, _) => ((Knob)d).Redraw()));
    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(0.0));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(Knob), new PropertyMetadata(1.0));
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(Knob), new PropertyMetadata(""));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Knob), new PropertyMetadata("", (d, _) => ((Knob)d).Redraw()));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(Knob), new PropertyMetadata(null));
    /// <summary>Bipolar knobs (pitch, gender) draw the arc from the centre (12 o'clock).</summary>
    public static readonly DependencyProperty BipolarProperty = DependencyProperty.Register(
        nameof(Bipolar), typeof(bool), typeof(Knob), new PropertyMetadata(false, (d, _) => ((Knob)d).Redraw()));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, Clamp(value)); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool Bipolar { get => (bool)GetValue(BipolarProperty); set => SetValue(BipolarProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (Knob)d;
        k.Redraw();
        if (FrameworkElementAutomationPeer.FromElement(k) is KnobAutomationPeer peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
    }

    private double Clamp(double v) => Math.Clamp(Math.Round(v / Step) * Step, Minimum, Maximum);
    private double Fraction => Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;

    // ---- Drawing -------------------------------------------------------------------------

    private void Redraw()
    {
        if (TrackArc is null) return;
        const double size = 104, stroke = 7;
        var c = new Point(size / 2, size / 2);
        var r = size / 2 - stroke / 2 - 1;

        TrackArc.Data = Arc(c, r, StartAngle, Sweep);
        var t = Fraction;
        if (Bipolar)
        {
            var mid = StartAngle + Sweep / 2;
            var to = StartAngle + Sweep * t;
            ValueArc.Data = Math.Abs(to - mid) < 0.5 ? null : Arc(c, r, Math.Min(mid, to), Math.Abs(to - mid));
        }
        else
        {
            ValueArc.Data = t <= 0.001 ? null : Arc(c, r, StartAngle, Sweep * t);
        }
        // Pointer: 0 = straight up; the dial runs from -135° to +135°.
        PointerRotation.Angle = -135 + 270 * t;

        var shown = $"{Value:0}";
        if (Bipolar && Value > 0) shown = "+" + shown;
        ValueText.Text = string.IsNullOrEmpty(Unit) ? shown : $"{shown}{Unit}";
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        static Point At(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = At(c, r, startDeg), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = At(c, r, startDeg + sweepDeg),
            Size = new Size(r, r),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    // ---- Input ---------------------------------------------------------------------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragOrigin = e.GetCurrentPoint(this).Position;
        _dragStartValue = Value;
        Dial.CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragOrigin is not { } o) return;
        var p = e.GetCurrentPoint(this).Position;
        var delta = (o.Y - p.Y) + (p.X - o.X);   // up or right = increase
        Value = _dragStartValue + delta / DragPixelsForFullRange * (Maximum - Minimum);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragOrigin = null;
        Dial.ReleasePointerCapture(e.Pointer);
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var hasModifier = (e.KeyModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)) != 0;
        var isFocused = FocusState != FocusState.Unfocused;
        if (!hasModifier && !isFocused)
        {
            return;
        }

        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        Value += Math.Sign(delta) * Step;
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var page = Math.Max(Step, (Maximum - Minimum) / 10);
        double? next = e.Key switch
        {
            VirtualKey.Up or VirtualKey.Right => Value + Step,
            VirtualKey.Down or VirtualKey.Left => Value - Step,
            VirtualKey.PageUp => Value + page,
            VirtualKey.PageDown => Value - page,
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            _ => null,
        };
        if (next is { } v) { Value = v; e.Handled = true; }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new KnobAutomationPeer(this);

    /// <summary>Screen readers see the knob as a slider with min/max/value.</summary>
    private sealed partial class KnobAutomationPeer(Knob owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private Knob K => (Knob)Owner;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(Knob);
        protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? K.Header : base.GetNameCore();
        protected override object GetPatternCore(PatternInterface p) => p == PatternInterface.RangeValue ? this : base.GetPatternCore(p);
        public bool IsReadOnly => false;
        public double LargeChange => Math.Max(K.Step, (K.Maximum - K.Minimum) / 10);
        public double Maximum => K.Maximum;
        public double Minimum => K.Minimum;
        public double SmallChange => K.Step;
        public double Value => K.Value;
        public void SetValue(double value) => K.Value = value;
    }
}
