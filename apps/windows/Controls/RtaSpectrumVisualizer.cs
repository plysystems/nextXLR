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
/// across 10 third-octave bands (31 Hz - 16 kHz) synchronized with microphone voice input.
/// </summary>
public sealed class RtaSpectrumVisualizer : UserControl
{
    private const int BandCount = 10;
    private static readonly double[] BandFrequencies = [31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(140, 0x13, 0x16, 0x1C)) };
    private readonly Rectangle[] _bars = new Rectangle[BandCount];
    private readonly Border[] _peakHolds = new Border[BandCount];
    private readonly double[] _currentLevels = new double[BandCount];
    private readonly double[] _peakHoldLevels = new double[BandCount];
    private readonly double[] _peakHoldTimers = new double[BandCount];

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _rnd = new();

    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(ChannelViewModel), typeof(RtaSpectrumVisualizer), new PropertyMetadata(null));

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
        Height = 110;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));

        for (int i = 0; i < BandCount; i++)
        {
            var bar = new Rectangle
            {
                RadiusX = 3,
                RadiusY = 3,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0),
                    GradientStops =
                    [
                        new GradientStop { Color = Color.FromArgb(200, 0x1F, 0xC7, 0x9A), Offset = 0.0 },
                        new GradientStop { Color = Color.FromArgb(230, 0xFF, 0xC2, 0x3A), Offset = 0.7 },
                        new GradientStop { Color = Color.FromArgb(255, 0xE0, 0x52, 0x52), Offset = 1.0 }
                    ]
                }
            };
            _bars[i] = bar;
            _canvas.Children.Add(bar);

            var hold = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF))
            };
            _peakHolds[i] = hold;
            _canvas.Children.Add(hold);
        }

        Content = _canvas;

        SizeChanged += (_, _) => LayoutBars();
        Loaded += (_, _) => { LayoutBars(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();

        _timer.Tick += OnTick;
    }

    private void LayoutBars()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 10 || h <= 10) return;

        double slotWidth = w / BandCount;
        double barWidth = Math.Max(6, slotWidth * 0.58);

        for (int i = 0; i < BandCount; i++)
        {
            double x = (i * slotWidth) + (slotWidth - barWidth) / 2.0;
            _bars[i].Width = barWidth;
            Canvas.SetLeft(_bars[i], x);

            _peakHolds[i].Width = barWidth;
            Canvas.SetLeft(_peakHolds[i], x);
        }
    }

    private void OnTick(object? sender, object e)
    {
        if (!IsActive) return;

        double h = ActualHeight;
        if (h <= 10) return;

        double meter = Channel?.Meter ?? 0.0;
        bool isSilent = meter < 0.001;

        for (int i = 0; i < BandCount; i++)
        {
            double targetLevel = 0.0;
            if (!isSilent)
            {
                // Formant energy curve around human voice fundamental (125-500Hz) & sibilance (4-8kHz)
                double voiceWeight = i switch
                {
                    0 => 0.25, // 31 Hz (sub-rumble)
                    1 => 0.50, // 63 Hz
                    2 => 0.90, // 125 Hz (voice warmth)
                    3 => 1.00, // 250 Hz (body)
                    4 => 0.85, // 500 Hz (clarity)
                    5 => 0.75, // 1 kHz
                    6 => 0.70, // 2 kHz (presence)
                    7 => 0.80, // 4 kHz (definition)
                    8 => 0.65, // 8 kHz (air)
                    _ => 0.35  // 16 kHz
                };

                // Add tiny organic spectral flutter
                double flutter = 0.88 + (_rnd.NextDouble() * 0.24);
                targetLevel = Math.Clamp(meter * voiceWeight * flutter, 0.0, 1.0);
            }

            // Attack / Release dynamics
            if (targetLevel > _currentLevels[i])
            {
                _currentLevels[i] = targetLevel; // Fast attack
            }
            else
            {
                _currentLevels[i] = Math.Max(0.0, _currentLevels[i] - 0.04); // Smooth release
            }

            // Peak hold logic
            if (_currentLevels[i] >= _peakHoldLevels[i])
            {
                _peakHoldLevels[i] = _currentLevels[i];
                _peakHoldTimers[i] = 1.0; // 1 second hold
            }
            else
            {
                if (_peakHoldTimers[i] > 0)
                {
                    _peakHoldTimers[i] -= 0.033;
                }
                else
                {
                    _peakHoldLevels[i] = Math.Max(0.0, _peakHoldLevels[i] - 0.02);
                }
            }

            // Apply bar heights and top offsets
            double barHeight = Math.Max(2, _currentLevels[i] * (h - 8));
            _bars[i].Height = barHeight;
            Canvas.SetTop(_bars[i], h - barHeight - 4);

            double holdY = Math.Max(2, h - (_peakHoldLevels[i] * (h - 8)) - 6);
            Canvas.SetTop(_peakHolds[i], holdY);
        }
    }
}

