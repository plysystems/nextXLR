using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using NextXLR.ViewModels;

namespace NextXLR.Controls;

/// <summary>
/// Live scrolling compression and gain-reduction history graph.
/// Displays input level, compressor threshold, output level and the dynamic gain reduction (dB) in real time.
/// </summary>
public sealed class CompressionHistoryGraph : UserControl
{
    private const int HistoryCount = 100;
    private const double FloorDb = -48.0;
    private const double CeilingDb = 0.0;

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(255, 0x13, 0x16, 0x1C)) };
    private readonly Microsoft.UI.Xaml.Shapes.Path _grPath = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _inPath = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _outPath = new();
    private readonly Line _thresholdLine = new();
    private readonly TextBlock _thresholdLabel = new();
    private readonly TextBlock _inLabel = new();
    private readonly TextBlock _grLabel = new();
    private readonly TextBlock _outLabel = new();
    private readonly List<Line> _gridLines = [];
    private readonly List<TextBlock> _gridLabels = [];

    private readonly struct PointData(double inDb, double outDb, double grDb)
    {
        public readonly double InDb = inDb;
        public readonly double OutDb = outDb;
        public readonly double GrDb = grDb;
    }

    private readonly Queue<PointData> _history = new(HistoryCount);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 FPS

    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(ChannelViewModel), typeof(CompressionHistoryGraph), new PropertyMetadata(null));

    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(double), typeof(CompressionHistoryGraph), new PropertyMetadata(-20.0, (d, _) => ((CompressionHistoryGraph)d).UpdateThresholdLine()));

    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(CompressionHistoryGraph), new PropertyMetadata(4.0));

    public static readonly DependencyProperty MakeupProperty = DependencyProperty.Register(
        nameof(Makeup), typeof(double), typeof(CompressionHistoryGraph), new PropertyMetadata(0.0));

    public ChannelViewModel? Channel
    {
        get => (ChannelViewModel?)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public double Threshold
    {
        get => (double)GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public double Makeup
    {
        get => (double)GetValue(MakeupProperty);
        set => SetValue(MakeupProperty, value);
    }

    public CompressionHistoryGraph()
    {
        Height = 150;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x30, 0x3C));

        // Fill initial history with floor values
        for (int i = 0; i < HistoryCount; i++)
        {
            _history.Enqueue(new PointData(FloorDb, FloorDb, 0.0));
        }

        // Setup visuals
        _grPath.Fill = new SolidColorBrush(Color.FromArgb(90, 0xE0, 0x52, 0x52));
        _grPath.Stroke = new SolidColorBrush(Color.FromArgb(255, 0xE0, 0x52, 0x52));
        _grPath.StrokeThickness = 1.5;

        _inPath.Stroke = new SolidColorBrush(Color.FromArgb(220, 0x1F, 0xC7, 0x9A)); // Cyan/Brand
        _inPath.StrokeThickness = 2.0;

        _outPath.Stroke = new SolidColorBrush(Color.FromArgb(240, 0xF2, 0xF4, 0xF7)); // White
        _outPath.StrokeThickness = 1.2;

        _thresholdLine.Stroke = new SolidColorBrush(Color.FromArgb(220, 0xFF, 0xC2, 0x3A)); // Yellow
        _thresholdLine.StrokeDashArray = [4, 4];
        _thresholdLine.StrokeThickness = 1.5;

        _thresholdLabel.Foreground = new SolidColorBrush(Color.FromArgb(220, 0xFF, 0xC2, 0x3A));
        _thresholdLabel.FontSize = 10;
        _thresholdLabel.FontFamily = new FontFamily("Consolas");

        _inLabel.FontSize = 11;
        _inLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _inLabel.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x1F, 0xC7, 0x9A));
        _inLabel.Text = "IN: -∞";
        Canvas.SetLeft(_inLabel, 10);
        Canvas.SetTop(_inLabel, 6);

        _grLabel.FontSize = 11;
        _grLabel.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        _grLabel.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x6E, 0x6E));
        _grLabel.Text = "GR: 0.0 dB";
        Canvas.SetLeft(_grLabel, 110);
        Canvas.SetTop(_grLabel, 6);

        _outLabel.FontSize = 11;
        _outLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _outLabel.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xF4, 0xF7));
        _outLabel.Text = "OUT: -∞";
        Canvas.SetLeft(_outLabel, 220);
        Canvas.SetTop(_outLabel, 6);

        _canvas.Children.Add(_grPath);
        _canvas.Children.Add(_inPath);
        _canvas.Children.Add(_outPath);
        _canvas.Children.Add(_thresholdLine);
        _canvas.Children.Add(_thresholdLabel);
        _canvas.Children.Add(_inLabel);
        _canvas.Children.Add(_grLabel);
        _canvas.Children.Add(_outLabel);

        Content = _canvas;

        SizeChanged += (_, _) => SetupGrid();
        Loaded += (_, _) => { SetupGrid(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();

        _timer.Tick += OnTick;
    }

    private void SetupGrid()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        // Clean old grid
        foreach (var l in _gridLines) _canvas.Children.Remove(l);
        foreach (var t in _gridLabels) _canvas.Children.Remove(t);
        _gridLines.Clear();
        _gridLabels.Clear();

        double[] dbs = [0.0, -6.0, -12.0, -18.0, -24.0, -36.0];
        foreach (var db in dbs)
        {
            double y = DbToY(db, h);
            var line = new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                StrokeThickness = 1
            };
            _gridLines.Add(line);
            _canvas.Children.Insert(0, line);

            var label = new TextBlock
            {
                Text = $"{db:0} dB",
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255))
            };
            Canvas.SetLeft(label, Math.Max(0, w - 46));
            Canvas.SetTop(label, Math.Max(2, y - 12));
            _gridLabels.Add(label);
            _canvas.Children.Add(label);
        }

        UpdateThresholdLine();
    }

    private void UpdateThresholdLine()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        double y = DbToY(Threshold, h);
        _thresholdLine.X1 = 0;
        _thresholdLine.X2 = w;
        _thresholdLine.Y1 = y;
        _thresholdLine.Y2 = y;

        _thresholdLabel.Text = $"Thresh: {Threshold:0} dB";
        Canvas.SetLeft(_thresholdLabel, Math.Max(0, w - 110));
        Canvas.SetTop(_thresholdLabel, Math.Clamp(y - 13, 4, h - 16));
    }

    private void OnTick(object? sender, object e)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        // Current meter level
        double meter = Channel?.Meter ?? 0.0;
        double inDb = meter > 0.0005 ? Math.Max(FloorDb, 20.0 * Math.Log10(meter)) : FloorDb;

        double thresh = Threshold;
        double ratio = Math.Max(1.0, Ratio);
        double makeup = Makeup;

        double excess = Math.Max(0.0, inDb - thresh);
        double grDb = excess * (1.0 - (1.0 / ratio));
        double outDb = Math.Clamp(inDb - grDb + makeup, FloorDb, CeilingDb + 6.0);

        _history.Dequeue();
        _history.Enqueue(new PointData(inDb, outDb, grDb));

        // Update Labels
        _inLabel.Text = inDb <= FloorDb ? "IN: -∞" : $"IN: {inDb:0.0} dB";
        _grLabel.Text = grDb > 0.1 ? $"GR: -{grDb:0.0} dB" : "GR: 0.0 dB";
        _outLabel.Text = outDb <= FloorDb ? "OUT: -∞" : $"OUT: {outDb:0.0} dB";

        // Redraw curves
        RenderHistory(w, h);
    }

    private void RenderHistory(double w, double h)
    {
        var points = _history.ToArray();
        int count = points.Length;
        if (count < 2) return;

        double stepX = w / (count - 1);

        // Build In Line
        var inGeom = new PathGeometry();
        var inFig = new PathFigure { StartPoint = new Point(0, DbToY(points[0].InDb, h)), IsFilled = false, IsClosed = false };
        for (int i = 1; i < count; i++)
        {
            inFig.Segments.Add(new LineSegment { Point = new Point(i * stepX, DbToY(points[i].InDb, h)) });
        }
        inGeom.Figures.Add(inFig);
        _inPath.Data = inGeom;

        // Build Out Line
        var outGeom = new PathGeometry();
        var outFig = new PathFigure { StartPoint = new Point(0, DbToY(points[0].OutDb, h)), IsFilled = false, IsClosed = false };
        for (int i = 1; i < count; i++)
        {
            outFig.Segments.Add(new LineSegment { Point = new Point(i * stepX, DbToY(points[i].OutDb, h)) });
        }
        outGeom.Figures.Add(outFig);
        _outPath.Data = outGeom;

        // Build GR Filled Shape (descending from ceiling)
        var grGeom = new PathGeometry();
        var grFig = new PathFigure { StartPoint = new Point(0, 0), IsFilled = true, IsClosed = true };
        for (int i = 0; i < count; i++)
        {
            double grY = Math.Min(h * 0.7, (points[i].GrDb / 24.0) * h);
            grFig.Segments.Add(new LineSegment { Point = new Point(i * stepX, grY) });
        }
        grFig.Segments.Add(new LineSegment { Point = new Point(w, 0) });
        grGeom.Figures.Add(grFig);
        _grPath.Data = grGeom;
    }

    private static double DbToY(double db, double h)
    {
        double norm = (Math.Clamp(db, FloorDb, CeilingDb) - FloorDb) / (CeilingDb - FloorDb); // 0 at FloorDb, 1 at CeilingDb
        return (1.0 - norm) * (h - 24) + 16; // Invert for screen coords
    }
}

