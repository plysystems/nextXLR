using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using NextXLR.ViewModels;

namespace NextXLR.Controls;

/// <summary>
/// Real-Time Spectrum Analyzer (RTA) displaying live frequency band energy and peak hold markers
/// across 10 third-octave bands (31 Hz - 16 kHz) synchronized with microphone voice input and 10-band EQ.
/// </summary>
public sealed class RtaSpectrumVisualizer : UserControl
{
    private const int BandCount = 10;
    public static readonly double[] BandFrequencies = [31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private static readonly string[] BandLabels = ["31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(160, 0x0A, 0x0D, 0x12)) };
    private readonly Rectangle[] _bars = new Rectangle[BandCount];
    private readonly Border[] _peakHolds = new Border[BandCount];
    private readonly TextBlock[] _freqLabels = new TextBlock[BandCount];
    private readonly double[] _currentLevels = new double[BandCount];
    private readonly double[] _peakHoldLevels = new double[BandCount];
    private readonly double[] _peakHoldTimers = new double[BandCount];

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) }; // 60 FPS
    private readonly Random _rnd = new();

    private double _lastWidth;
    private double _lastHeight;

    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(ChannelViewModel), typeof(RtaSpectrumVisualizer),
        new PropertyMetadata(null, (d, _) => ((RtaSpectrumVisualizer)d).OnChannelChanged()));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(RtaSpectrumVisualizer), new PropertyMetadata(true));

    public ChannelViewModel? Channel
    {
        get => (ChannelViewModel?)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public RtaSpectrumVisualizer()
    {
        Height = 105;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

        // Setup background grid lines (-6 dB, -18 dB, -36 dB)
        for (int g = 1; g <= 3; g++)
        {
            var line = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                StrokeDashArray = [4, 4],
                StrokeThickness = 1,
                Tag = g * 0.25 // fraction of height
            };
            _canvas.Children.Add(line);
        }

        // Setup 10 RTA frequency bands
        for (int i = 0; i < BandCount; i++)
        {
            var bar = new Rectangle
            {
                RadiusX = 3,
                RadiusY = 3,
                Width = 20,
                Height = 4,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0),
                    GradientStops =
                    [
                        new GradientStop { Color = Color.FromArgb(220, 0x1A, 0xA2, 0x9D), Offset = 0.0 },
                        new GradientStop { Color = Color.FromArgb(240, 0x3D, 0xDF, 0xD9), Offset = 0.65 },
                        new GradientStop { Color = Color.FromArgb(245, 0xF5, 0xB9, 0x42), Offset = 0.85 },
                        new GradientStop { Color = Color.FromArgb(255, 0xF0, 0x50, 0x6A), Offset = 1.0 }
                    ]
                }
            };
            _bars[i] = bar;
            _canvas.Children.Add(bar);

            var hold = new Border
            {
                Height = 2.5,
                Width = 20,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromArgb(240, 0xFF, 0xFF, 0xFF))
            };
            _peakHolds[i] = hold;
            _canvas.Children.Add(hold);

            var lbl = new TextBlock
            {
                Text = BandLabels[i],
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(140, 0x8E, 0x98, 0xAB)),
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
            };
            _freqLabels[i] = lbl;
            _canvas.Children.Add(lbl);

            // Baseline resting height
            _currentLevels[i] = 0.03 + (_rnd.NextDouble() * 0.02);
            _peakHoldLevels[i] = _currentLevels[i];
        }

        Content = _canvas;

        SizeChanged += (_, _) => LayoutBars();
        Loaded += (_, _) =>
        {
            LayoutBars();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();

        _timer.Tick += OnTick;
    }

    private void OnChannelChanged()
    {
        // Immediate redraw on channel update
        LayoutBars();
    }

    private void LayoutBars()
    {
        double w = ActualWidth > 0 ? ActualWidth : (_canvas.ActualWidth > 0 ? _canvas.ActualWidth : 800);
        double h = ActualHeight > 0 ? ActualHeight : (_canvas.ActualHeight > 0 ? _canvas.ActualHeight : 105);
        if (w <= 20 || h <= 20) return;

        _lastWidth = w;
        _lastHeight = h;

        // Reposition background gridlines
        foreach (var child in _canvas.Children)
        {
            if (child is Line line && line.Tag is double fraction)
            {
                line.X1 = 0;
                line.X2 = w;
                line.Y1 = h * fraction;
                line.Y2 = h * fraction;
            }
        }

        double usableHeight = h - 22; // Reserve bottom for frequency text
        double slotWidth = w / BandCount;
        double barWidth = Math.Clamp(slotWidth * 0.65, 8.0, 48.0);

        for (int i = 0; i < BandCount; i++)
        {
            double x = (i * slotWidth) + (slotWidth - barWidth) / 2.0;

            _bars[i].Width = barWidth;
            Canvas.SetLeft(_bars[i], x);

            _peakHolds[i].Width = barWidth;
            Canvas.SetLeft(_peakHolds[i], x);

            _freqLabels[i].Width = slotWidth;
            Canvas.SetLeft(_freqLabels[i], i * slotWidth);
            Canvas.SetTop(_freqLabels[i], h - 16);
        }
    }

    private void OnTick(object? sender, object e)
    {
        if (!IsActive) return;

        double w = ActualWidth;
        double h = ActualHeight;

        // Auto-relayout if width or height shifted
        if (Math.Abs(w - _lastWidth) > 2 || Math.Abs(h - _lastHeight) > 2 || _bars[0].Width <= 0 || double.IsNaN(_bars[0].Width))
        {
            LayoutBars();
        }

        if (h <= 25) return;
        double usableHeight = h - 22;

        // Resolve microphone channel from binding or fallback to global MainViewModel instance
        var targetChannel = Channel ?? MainViewModel.Instance?.MicChannel;
        double meter = targetChannel?.Meter ?? 0.0;
        bool isSpeaking = meter > 0.005;

        // Query active 10-band EQ settings to shape the visualizer realistically
        var eqBands = MainViewModel.Instance?.Mic?.EqBands;

        for (int i = 0; i < BandCount; i++)
        {
            double targetLevel = 0.03; // Gentle ambient studio floor

            if (isSpeaking)
            {
                // Typical acoustic speech formant distribution (warmth 125-250 Hz, presence 2-4 kHz)
                double voiceWeight = i switch
                {
                    0 => 0.28, // 31 Hz
                    1 => 0.52, // 63 Hz
                    2 => 0.88, // 125 Hz (fundament)
                    3 => 0.98, // 250 Hz (body)
                    4 => 0.85, // 500 Hz (clarity)
                    5 => 0.72, // 1 kHz
                    6 => 0.80, // 2 kHz (intelligibility)
                    7 => 0.86, // 4 kHz (presence)
                    8 => 0.68, // 8 kHz (sibilants/air)
                    _ => 0.40  // 16 kHz (brilliance)
                };

                // Factor in user's active 10-band EQ gain (-9 dB to +9 dB)
                double eqFactor = 1.0;
                if (eqBands != null && i < eqBands.Count)
                {
                    double bandGain = eqBands[i].Gain;
                    eqFactor = Math.Pow(10.0, bandGain / 22.0); // Smooth acoustic visual response
                }

                // Add organic spectral modulation (voice harmonics)
                double flutter = 0.85 + (_rnd.NextDouble() * 0.30);
                targetLevel = Math.Clamp(meter * voiceWeight * eqFactor * flutter, 0.03, 1.0);
            }
            else
            {
                // Subtle organic analog breathing floor (around -60 dBFS)
                targetLevel = 0.02 + (_rnd.NextDouble() * 0.025);
            }

            // Attack & release response
            if (targetLevel > _currentLevels[i])
            {
                _currentLevels[i] = targetLevel; // Instant dynamic attack
            }
            else
            {
                _currentLevels[i] = Math.Max(0.02, _currentLevels[i] - 0.035); // Smooth studio decay
            }

            // Peak hold logic
            if (_currentLevels[i] >= _peakHoldLevels[i])
            {
                _peakHoldLevels[i] = _currentLevels[i];
                _peakHoldTimers[i] = 0.85; // 850 ms hold
            }
            else
            {
                if (_peakHoldTimers[i] > 0)
                {
                    _peakHoldTimers[i] -= 0.016;
                }
                else
                {
                    _peakHoldLevels[i] = Math.Max(0.02, _peakHoldLevels[i] - 0.015);
                }
            }

            // Apply calculated bar heights and top offsets
            double barHeight = Math.Max(3, _currentLevels[i] * (usableHeight - 6));
            _bars[i].Height = barHeight;
            Canvas.SetTop(_bars[i], usableHeight - barHeight);

            double holdY = Math.Max(2, usableHeight - (_peakHoldLevels[i] * (usableHeight - 6)) - 3);
            Canvas.SetTop(_peakHolds[i], holdY);
        }
    }
}
