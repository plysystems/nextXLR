using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace NextXLR.Controls;

/// <summary>
/// Waveform overview with trim handles. <see cref="Peaks"/> are 0..255 bars; <see cref="Start"/>
/// and <see cref="End"/> (0..1) are the trim points — drag the handles (or anywhere near them),
/// or focus the control and use ←/→ (Shift = fine, Tab-free: Home/End pick the handle).
/// Outside the trimmed range the waveform is dimmed; <see cref="Cursor"/> shows playback.
/// With <see cref="IsEditable"/> false it's a static thumbnail (browser rows, pads).
/// </summary>
public sealed class WaveformView : UserControl
{
    private const double MinGap = 0.01, HandleGrab = 18;

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Microsoft.UI.Xaml.Shapes.Path _wave = new();
    private readonly Rectangle _dimLeft = new(), _dimRight = new();
    private readonly Rectangle _startHandle = new(), _endHandle = new();
    private readonly Rectangle _cursor = new() { Width = 2 };
    private int _dragging;   // 0 none, 1 start, 2 end
    private bool _endSelected;

    public static readonly DependencyProperty PeaksProperty = Register(nameof(Peaks), typeof(object), null);
    public static readonly DependencyProperty StartProperty = Register(nameof(Start), typeof(double), 0.0);
    public static readonly DependencyProperty EndProperty = Register(nameof(End), typeof(double), 1.0);
    public static readonly DependencyProperty CursorProperty = Register(nameof(Cursor), typeof(double), -1.0);
    public static readonly DependencyProperty IsEditableProperty = Register(nameof(IsEditable), typeof(bool), false);
    public static readonly DependencyProperty AccentProperty = Register(nameof(Accent), typeof(Brush), null);

    private static DependencyProperty Register(string name, Type type, object? def) =>
        DependencyProperty.Register(name, type, typeof(WaveformView), new PropertyMetadata(def, (d, _) => ((WaveformView)d).Render()));

    public object? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double Start { get => (double)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public double End { get => (double)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public double Cursor { get => (double)GetValue(CursorProperty); set => SetValue(CursorProperty, value); }
    public bool IsEditable { get => (bool)GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public WaveformView()
    {
        var dim = new SolidColorBrush(Color.FromArgb(170, 0x0E, 0x10, 0x15));
        _dimLeft.Fill = dim;
        _dimRight.Fill = dim;
        var handle = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xF4, 0xF7));
        foreach (var h in new[] { _startHandle, _endHandle }) { h.Width = 4; h.RadiusX = 2; h.RadiusY = 2; h.Fill = handle; }
        _cursor.Fill = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xC2, 0x3A));
        foreach (var e in new UIElement[] { _wave, _dimLeft, _dimRight, _cursor, _startHandle, _endHandle }) _canvas.Children.Add(e);
        Content = _canvas;
        SizeChanged += (_, _) => Render();

        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += (_, e) => { _dragging = 0; _canvas.ReleasePointerCapture(e.Pointer); };
        _canvas.PointerCaptureLost += (_, _) => _dragging = 0;
        KeyDown += OnKey;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Services.Loc.Get("SamplerWaveformName"));
    }

    private double ToUnit(double x) => ActualWidth > 0 ? Math.Clamp(x / ActualWidth, 0, 1) : 0;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEditable) return;
        Focus(FocusState.Pointer);
        var x = e.GetCurrentPoint(_canvas).Position.X;
        double sx = Start * ActualWidth, ex = End * ActualWidth;
        // Nearest handle; a click elsewhere moves whichever handle is closer.
        _dragging = Math.Abs(x - sx) <= Math.Abs(x - ex) ? 1 : 2;
        if (Math.Min(Math.Abs(x - sx), Math.Abs(x - ex)) > HandleGrab) Move(ToUnit(x));
        _endSelected = _dragging == 2;
        _canvas.CapturePointer(e.Pointer);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging == 0)
        {
            if (IsEditable) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            return;
        }
        Move(ToUnit(e.GetCurrentPoint(_canvas).Position.X));
    }

    private void Move(double v)
    {
        if (_dragging == 1) Start = Math.Min(v, End - MinGap);
        else if (_dragging == 2) End = Math.Max(v, Start + MinGap);
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (!IsEditable) return;
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var step = shift ? 0.002 : 0.01;
        switch (e.Key)
        {
            case VirtualKey.Home: _endSelected = false; break;
            case VirtualKey.End: _endSelected = true; break;
            case VirtualKey.Left: Nudge(-step); break;
            case VirtualKey.Right: Nudge(step); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Nudge(double d)
    {
        _dragging = _endSelected ? 2 : 1;
        Move((_endSelected ? End : Start) + d);
        _dragging = 0;
    }

    private void Render()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var accent = Accent ?? new SolidColorBrush(Color.FromArgb(255, 0x1F, 0xC7, 0x9A));
        var peaks = Peaks as byte[] ?? [];

        var group = new GeometryGroup();
        if (peaks.Length > 0)
        {
            var bars = (int)Math.Min(peaks.Length, Math.Max(1, w / 3));
            var bw = w / bars;
            for (int i = 0; i < bars; i++)
            {
                int from = peaks.Length * i / bars, to = Math.Max(from + 1, peaks.Length * (i + 1) / bars);
                byte max = 0;
                for (int j = from; j < to; j++) max = Math.Max(max, peaks[j]);
                var bh = Math.Max(1.5, max / 255.0 * h);
                group.Children.Add(new RectangleGeometry { Rect = new Rect(i * bw, (h - bh) / 2, Math.Max(1, bw - 1), bh) });
            }
        }
        _wave.Data = group;
        _wave.Fill = accent;

        double sx = Math.Clamp(Start, 0, 1) * w, ex = Math.Clamp(End, 0, 1) * w;
        Place(_dimLeft, 0, 0, sx, h);
        Place(_dimRight, ex, 0, Math.Max(0, w - ex), h);
        var edit = IsEditable ? Visibility.Visible : Visibility.Collapsed;
        _startHandle.Visibility = _endHandle.Visibility = edit;
        Place(_startHandle, Math.Max(0, sx - 2), 0, 4, h);
        Place(_endHandle, Math.Min(w - 4, ex - 2), 0, 4, h);
        _dimLeft.Visibility = _dimRight.Visibility = edit;
        _cursor.Visibility = Cursor >= 0 ? Visibility.Visible : Visibility.Collapsed;
        Place(_cursor, Math.Clamp(Cursor, 0, 1) * w - 1, 0, 2, h);
    }

    private static void Place(FrameworkElement e, double x, double y, double width, double height)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        e.Width = Math.Max(0, width);
        e.Height = Math.Max(0, height);
    }
}
