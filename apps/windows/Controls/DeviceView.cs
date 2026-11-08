using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace NextXLR.Controls;

/// <summary>Zone colours as the device shows them.</summary>
public readonly record struct ZoneColors(Color Active, Color Inactive, bool Gradient);

/// <summary>Everything the device picture shows, built from one daemon state broadcast.</summary>
public sealed record DeviceSnapshot(
    bool IsMini,
    IReadOnlyDictionary<string, ZoneColors> Zones,
    double[] Faders,          // 0..1 positions A–D
    string[] FaderNames,      // scribble strip labels
    bool[] Mutes,             // mute buttons A–D lit
    bool Cough,
    bool Bleep,
    bool FxEnabled,
    int Preset,               // 0..5
    bool Megaphone,
    bool Robot,
    bool Hardtune,
    double[] Encoders);       // 0..1: pitch, gender, reverb, echo

/// <summary>
/// Top-down picture of the GoXLR device model based on official hardware specs,
/// rendered with scalable vector elements.
/// </summary>
public sealed class DeviceView : UserControl
{
    private const double CanvasW = 1024, CanvasH = 575;

    private static readonly Color CanvasBg = Color.FromArgb(255, 0x15, 0x15, 0x19);
    private static readonly Color PanelBg = Color.FromArgb(255, 0x1A, 0x1D, 0x24);
    private static readonly Color PanelBorder = Color.FromArgb(255, 0x2A, 0x2E, 0x38);

    // Default colors matching Bild 1
    private static readonly Color DefaultCyan = Color.FromArgb(255, 0x00, 0xE5, 0xFF);
    private static readonly Color DefaultOrange = Color.FromArgb(255, 0xFF, 0x8A, 0x3D);
    private static readonly Color DefaultLime = Color.FromArgb(255, 0xC5, 0xFF, 0x48);
    private static readonly Color DefaultBlue = Color.FromArgb(255, 0x49, 0x53, 0xFF);
    private static readonly Color DefaultCoral = Color.FromArgb(255, 0xFF, 0x4F, 0x5E);
    private static readonly Color DefaultMagenta = Color.FromArgb(255, 0xFF, 0x40, 0xD0);

    private readonly Canvas _canvas = new();
    private bool? _builtMini;

    // Left Panel elements
    private readonly List<(Border Screen, TextBlock Number, FontIcon Icon, TextBlock Label)> _scribbles = [];
    private readonly List<Border> _faderBars = [];
    private readonly List<Border> _muteButtons = [];

    // Top-Right Panel (Effects)
    private readonly List<Border> _presetButtons = [];
    private readonly List<Ellipse> _encoderKnobs = [];
    private Border? _megaphone, _robot, _hardtune, _fx;

    // Bottom-Right Panel (Sampler + Cough/Bleep)
    private readonly List<Border> _bankButtons = [];
    private readonly List<Border> _samplerPads = [];
    private Border? _clearButton;
    private Border? _bleepButton, _muteButton;

    // Mini model elements (matching media_1794107706083_5d3559f3.png)
    private readonly List<Border> _miniScribbles = [];
    private readonly List<TextBlock> _miniScribbleLabels = [];
    private readonly List<Border> _miniFaders = [];
    private readonly List<Border> _miniMutes = [];
    private Border? _miniBleep, _miniMute;

    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(object), typeof(DeviceView),
        new PropertyMetadata(null, (d, e) => ((DeviceView)d).Update(e.NewValue as DeviceSnapshot)));

    public object? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public static readonly DependencyProperty ForceMiniProperty = DependencyProperty.Register(
        nameof(ForceMini), typeof(bool), typeof(DeviceView),
        new PropertyMetadata(false, (d, _) => ((DeviceView)d).RebuildCurrent()));

    public bool ForceMini
    {
        get => (bool)GetValue(ForceMiniProperty);
        set => SetValue(ForceMiniProperty, value);
    }

    public DeviceView()
    {
        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _canvas,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "GoXLR Device View");
        Build(false);
        Update(null);
    }

    private void RebuildCurrent()
    {
        var isMini = ForceMini || (_currentSnapshot?.IsMini == true);
        Build(isMini);
        Update(_currentSnapshot);
    }

    private DeviceSnapshot? _currentSnapshot;

    public void Update(DeviceSnapshot? s)
    {
        try
        {
            _currentSnapshot = s;
            bool isMini = ForceMini || (s?.IsMini == true);

            if (_builtMini != isMini)
            {
                Build(isMini);
            }

        ZoneColors Z(string zone, Color fallbackActive, Color fallbackInactive)
        {
            if (s?.Zones != null && s.Zones.TryGetValue(zone, out var z)) return z;
            return new ZoneColors(fallbackActive, fallbackInactive, false);
        }

        void LightButton(Border? btn, ZoneColors zc, bool on)
        {
            if (btn == null) return;
            var col = (s != null && !on) ? zc.Inactive : zc.Active;
            btn.Background = new SolidColorBrush(col);
        }

        if (isMini)
        {
            string[] defaultMiniNames = ["MIC", "CHAT", "MUSIC", "SYSTEM"];
            for (int i = 0; i < 4; i++)
            {
                var letter = (char)('a' + i);
                var fz = Z($"fader_{letter}", DefaultOrange, Color.FromArgb(255, 0x80, 0x40, 0x15));
                var mz = Z($"mute_{letter}", DefaultLime, Color.FromArgb(255, 0x40, 0x60, 0x10));

                if (i < _miniScribbles.Count)
                {
                    _miniScribbles[i].Background = new SolidColorBrush(DefaultCyan);
                    if (s?.FaderNames != null && i < s.FaderNames.Length && !string.IsNullOrWhiteSpace(s.FaderNames[i]))
                    {
                        _miniScribbleLabels[i].Text = s.FaderNames[i].ToUpperInvariant();
                    }
                    else
                    {
                        _miniScribbleLabels[i].Text = defaultMiniNames[i];
                    }
                }

                if (i < _miniFaders.Count)
                {
                    var bar = _miniFaders[i];
                    if (fz.Gradient)
                    {
                        bar.Background = new LinearGradientBrush
                        {
                            StartPoint = new Windows.Foundation.Point(0, 1),
                            EndPoint = new Windows.Foundation.Point(0, 0),
                            GradientStops =
                            {
                                new GradientStop { Color = fz.Inactive, Offset = 0 },
                                new GradientStop { Color = fz.Active, Offset = 1 },
                            },
                        };
                    }
                    else
                    {
                        bar.Background = new SolidColorBrush(fz.Active);
                    }
                }

                if (i < _miniMutes.Count)
                {
                    var btn = _miniMutes[i];
                    bool isMuted = s?.Mutes != null && i < s.Mutes.Length && s.Mutes[i];
                    var mCol = isMuted ? mz.Active : (s != null ? mz.Inactive : mz.Active);
                    btn.Background = new SolidColorBrush(mCol);
                }
            }

            LightButton(_miniBleep, Z("bleep", DefaultMagenta, Color.FromArgb(255, 0x60, 0x10, 0x50)), s?.Bleep ?? true);
            LightButton(_miniMute, Z("cough", DefaultMagenta, Color.FromArgb(255, 0x60, 0x10, 0x50)), s?.Cough ?? true);
            return;
        }

        // 1. Left Panel (Faders, Scribbles, Mutes)
        string[] defaultNames = ["Mic", "Music", "Chat", "System"];
        for (int i = 0; i < 4; i++)
        {
            var letter = (char)('a' + i);
            var fz = Z($"fader_{letter}", DefaultOrange, Color.FromArgb(255, 0x80, 0x40, 0x15));
            var mz = Z($"mute_{letter}", DefaultLime, Color.FromArgb(255, 0x40, 0x60, 0x10));

            // Scribble Display
            if (i < _scribbles.Count)
            {
                var (screen, num, icon, lbl) = _scribbles[i];
                screen.Background = new SolidColorBrush(fz.Active);
                var textCol = Luma(fz.Active) > 0.55 ? Colors.Black : Colors.White;
                num.Foreground = new SolidColorBrush(textCol);
                icon.Foreground = new SolidColorBrush(textCol);
                lbl.Foreground = new SolidColorBrush(textCol);
                if (s?.FaderNames != null && i < s.FaderNames.Length && !string.IsNullOrWhiteSpace(s.FaderNames[i]))
                {
                    lbl.Text = s.FaderNames[i];
                }
                else
                {
                    lbl.Text = defaultNames[i];
                }
            }

            // Fader Bar
            if (i < _faderBars.Count)
            {
                var bar = _faderBars[i];
                if (fz.Gradient)
                {
                    bar.Background = new LinearGradientBrush
                    {
                        StartPoint = new Windows.Foundation.Point(0, 1),
                        EndPoint = new Windows.Foundation.Point(0, 0),
                        GradientStops =
                        {
                            new GradientStop { Color = fz.Inactive, Offset = 0 },
                            new GradientStop { Color = fz.Active, Offset = 1 },
                        },
                    };
                }
                else
                {
                    bar.Background = new SolidColorBrush(fz.Active);
                }
            }

            // Mute Button
            if (i < _muteButtons.Count)
            {
                var btn = _muteButtons[i];
                bool isMuted = s?.Mutes != null && i < s.Mutes.Length && s.Mutes[i];
                var mCol = isMuted ? mz.Active : (s != null ? mz.Inactive : mz.Active);
                btn.Background = new SolidColorBrush(mCol);
                if (btn.Child is FontIcon ico)
                {
                    ico.Foreground = new SolidColorBrush(Luma(mCol) > 0.55 ? Colors.Black : Colors.White);
                }
            }
        }

        // 2. Top-Right Panel (Effects)
        var pz = Z("effect_presets", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70));
        for (int i = 0; i < _presetButtons.Count; i++)
        {
            var btn = _presetButtons[i];
            bool active = s != null && s.Preset == i;
            var col = active ? pz.Active : (s != null ? pz.Inactive : pz.Active);
            btn.Background = new SolidColorBrush(col);
        }

        var encZ = Z("encoders", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70));
        foreach (var knob in _encoderKnobs)
        {
            knob.Fill = new SolidColorBrush(encZ.Active);
        }

        LightButton(_megaphone, Z("effect_megaphone", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70)), s?.Megaphone ?? true);
        LightButton(_robot, Z("effect_robot", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70)), s?.Robot ?? true);
        LightButton(_hardtune, Z("effect_hardtune", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70)), s?.Hardtune ?? true);
        LightButton(_fx, Z("effect_fx", DefaultBlue, Color.FromArgb(255, 0x20, 0x25, 0x70)), s?.FxEnabled ?? true);

        // 3. Bottom-Right Panel (Sampler + Cough/Bleep)
        var bz = Z("sampler_banks", DefaultCoral, Color.FromArgb(255, 0x60, 0x15, 0x20));
        for (int i = 0; i < _bankButtons.Count; i++)
        {
            _bankButtons[i].Background = new SolidColorBrush(bz.Active);
        }

        var padZ = Z("sampler_pads", DefaultCoral, Color.FromArgb(255, 0x60, 0x15, 0x20));
        foreach (var pad in _samplerPads)
        {
            pad.Background = new SolidColorBrush(padZ.Active);
        }
        if (_clearButton != null) _clearButton.Background = new SolidColorBrush(padZ.Active);

        LightButton(_bleepButton, Z("bleep", DefaultMagenta, Color.FromArgb(255, 0x60, 0x10, 0x50)), s?.Bleep ?? true);
        LightButton(_muteButton, Z("cough", DefaultMagenta, Color.FromArgb(255, 0x60, 0x10, 0x50)), s?.Cough ?? true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeviceView.Update failed: {ex}");
        }
    }

    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private void Build(bool mini)
    {
        _builtMini = mini;
        _canvas.Children.Clear();
        _scribbles.Clear();
        _faderBars.Clear();
        _muteButtons.Clear();
        _presetButtons.Clear();
        _encoderKnobs.Clear();
        _bankButtons.Clear();
        _samplerPads.Clear();
        _miniScribbles.Clear();
        _miniScribbleLabels.Clear();
        _miniFaders.Clear();
        _miniMutes.Clear();
        _miniBleep = _miniMute = null;
        _megaphone = _robot = _hardtune = _fx = _clearButton = _bleepButton = _muteButton = null;

        _canvas.Width = CanvasW;
        _canvas.Height = CanvasH;

        if (mini)
        {
            BuildMiniModel();
            return;
        }

        BuildFullGoXlr();
    }

    private void BuildMiniModel()
    {
        // Dark outer chassis matching Bild (media_1794107706083_5d3559f3.png)
        Add(new Border
        {
            Width = CanvasW,
            Height = CanvasH,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(CanvasBg),
            BorderBrush = new SolidColorBrush(PanelBorder),
            BorderThickness = new Thickness(2),
        }, 0, 0);

        // Device Chassis: 390 wide x 515 high, centered horizontally (devX = 317)
        // Y from 30 to 545. Bottom corners feature 45-degree chamfers cut into bottom-left and bottom-right.
        double devX = 317, devY = 30, devW = 390, devH = 515;
        double chamferSize = 60;
        double rTop = 16;

        var fig = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(devX + rTop, devY),
            IsClosed = true,
            IsFilled = true,
        };
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX + devW - rTop, devY) });
        fig.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(devX + devW, devY + rTop),
            Size = new Windows.Foundation.Size(rTop, rTop),
            SweepDirection = SweepDirection.Clockwise,
        });
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX + devW, devY + devH - chamferSize) });
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX + devW - chamferSize, devY + devH) });
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX + chamferSize, devY + devH) });
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX, devY + devH - chamferSize) });
        fig.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(devX, devY + rTop) });
        fig.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(devX + rTop, devY),
            Size = new Windows.Foundation.Size(rTop, rTop),
            SweepDirection = SweepDirection.Clockwise,
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);

        var chassis = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geo,
            Fill = new SolidColorBrush(Color.FromArgb(255, 0x11, 0x11, 0x15)),
            Stroke = new SolidColorBrush(PanelBorder),
            StrokeThickness = 2,
        };
        Add(chassis, 0, 0);

        // 4 Columns: colW = 72 each, gap = 14. Total width = 330.
        // Start X = 317 + (390 - 330) / 2 = 347.
        double colW = 72;
        double colGap = 14;
        double startX = 347;
        string[] defaultMiniLabels = ["MIC", "CHAT", "MUSIC", "SYSTEM"];

        for (int i = 0; i < 4; i++)
        {
            double colX = startX + i * (colW + colGap);

            // 1. Top Scribble screen: Cyan background with centered uppercase title
            var screenLabel = new TextBlock
            {
                Text = defaultMiniLabels[i],
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var screen = Add(new Border
            {
                Width = colW,
                Height = 36,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(DefaultCyan),
                Child = screenLabel,
            }, colX, devY + 20);
            _miniScribbles.Add(screen);
            _miniScribbleLabels.Add(screenLabel);

            // 2. Fader track/bar: Orange vertical rectangle
            var fader = Add(new Border
            {
                Width = colW,
                Height = 246,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(DefaultOrange),
            }, colX, devY + 70);
            _miniFaders.Add(fader);

            // 3. Mute button: Lime with speaker mute glyph \uE74F
            var icon = new FontIcon
            {
                Glyph = "\uE74F",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var muteBtn = Add(new Border
            {
                Width = colW,
                Height = 44,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(DefaultLime),
                Child = icon,
            }, colX, devY + 330);
            _miniMutes.Add(muteBtn);
        }

        // 4. Bottom Buttons: Left "!@#$?*", Right "MUTE" (Pink/Magenta #FF40D0)
        double btnY = devY + 390;
        double btnW = 82;
        double btnH = 42;

        _miniBleep = AddButton(380, btnY, btnW, btnH, "!@#$?*", 15, DefaultMagenta, radius: 3);
        _miniMute = AddButton(562, btnY, btnW, btnH, "MUTE", 15, DefaultMagenta, radius: 3);
    }

    private void BuildFullGoXlr()
    {
        // 0. Base Chassis Canvas (Dark background)
        Add(new Border
        {
            Width = CanvasW,
            Height = CanvasH,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(CanvasBg),
        }, 0, 0);

        // ==========================================
        // 1. LEFT PANEL (Faders, Scribbles, Mutes)
        // ==========================================
        Add(new Border
        {
            Width = 404,
            Height = 551,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(PanelBg),
            BorderBrush = new SolidColorBrush(PanelBorder),
            BorderThickness = new Thickness(1.5),
        }, 12, 12);

        // Right notch indicator on left panel (pointing right)
        var notch = new Polygon
        {
            Points =
            {
                new Windows.Foundation.Point(416, 275),
                new Windows.Foundation.Point(432, 287),
                new Windows.Foundation.Point(416, 299),
            },
            Fill = new SolidColorBrush(CanvasBg),
        };
        _canvas.Children.Add(notch);

        string[] defaultLabels = ["Mic", "Music", "Chat", "System"];
        string[] glyphs = ["\uE720", "\uE8D6", "\uE77B", "\uE7F4"];

        for (int i = 0; i < 4; i++)
        {
            double colX = 26 + i * 98;

            // Scribble Display (Top)
            var numText = new TextBlock
            {
                Text = $"{i + 1}",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.Black),
            };
            var icon = new FontIcon
            {
                Glyph = glyphs[i],
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var labelText = new TextBlock
            {
                Text = defaultLabels[i],
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Black),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var scribbleGrid = new Grid
            {
                Padding = new Thickness(4, 2, 4, 2),
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                },
            };

            var topRow = new Grid();
            topRow.Children.Add(numText);
            topRow.Children.Add(icon);
            scribbleGrid.Children.Add(topRow);

            Grid.SetRow(labelText, 1);
            labelText.VerticalAlignment = VerticalAlignment.Center;
            scribbleGrid.Children.Add(labelText);

            var scribbleBorder = Add(new Border
            {
                Width = 74,
                Height = 46,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(DefaultCyan),
                Child = scribbleGrid,
            }, colX, 30);
            _scribbles.Add((scribbleBorder, numText, icon, labelText));

            // Fader Bar (Center vertical bar)
            var faderBar = Add(new Border
            {
                Width = 74,
                Height = 300,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(DefaultOrange),
            }, colX, 150);
            _faderBars.Add(faderBar);

            // Mute Button (Bottom)
            var muteIco = new FontIcon
            {
                Glyph = "\uE74F",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var muteBtn = Add(new Border
            {
                Width = 74,
                Height = 54,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(DefaultLime),
                Child = muteIco,
            }, colX, 502);
            _muteButtons.Add(muteBtn);
        }

        // ==========================================
        // 2. TOP-RIGHT PANEL (Effects)
        // ==========================================
        Add(new Border
        {
            Width = 584,
            Height = 296,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(PanelBg),
            BorderBrush = new SolidColorBrush(PanelBorder),
            BorderThickness = new Thickness(1.5),
        }, 428, 12);

        // Presets 1 to 6 (Vertical column on the left of effects panel)
        for (int p = 0; p < 6; p++)
        {
            var pBtn = AddButton(444, 24 + p * 45, 72, 38, $"{p + 1}", 18, DefaultBlue);
            _presetButtons.Add(pBtn);
        }

        // 4 Encoders / Knobs (Center 2x2 grid)
        (string Label, double X, double Y)[] knobs =
        [
            ("REVERB", 608, 92),
            ("PITCH", 776, 92),
            ("ECHO", 608, 224),
            ("GENDER", 776, 224),
        ];

        foreach (var (lbl, cx, cy) in knobs)
        {
            var knobCircle = Add(new Ellipse
            {
                Width = 96,
                Height = 96,
                Fill = new SolidColorBrush(DefaultBlue),
            }, cx - 48, cy - 48);
            _encoderKnobs.Add(knobCircle);

            Add(new TextBlock
            {
                Text = lbl,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Colors.White),
                Width = 110,
                TextAlignment = TextAlignment.Center,
            }, cx - 55, cy + 52);
        }

        // 4 Effect buttons (Vertical column on the right of effects panel)
        _megaphone = AddButton(868, 24, 126, 50, "MEGAFON", 14, DefaultBlue);
        _robot = AddButton(868, 86, 126, 50, "ROBOT", 14, DefaultBlue);
        _hardtune = AddButton(868, 148, 126, 50, "HARDTUNE", 14, DefaultBlue);
        _fx = AddButton(868, 210, 126, 82, "FX", 18, DefaultBlue);

        // ==========================================
        // 3. BOTTOM-RIGHT PANEL (Sampler + Cough/Bleep)
        // ==========================================
        Add(new Border
        {
            Width = 584,
            Height = 239,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(PanelBg),
            BorderBrush = new SolidColorBrush(PanelBorder),
            BorderThickness = new Thickness(1.5),
        }, 428, 324);

        // Bank Buttons A, B, C (Left column)
        string[] bankNames = ["A", "B", "C"];
        for (int b = 0; b < 3; b++)
        {
            var bBtn = AddButton(444, 340 + b * 68, 72, 56, bankNames[b], 20, DefaultCoral);
            _bankButtons.Add(bBtn);
        }

        // Sampler Pads 1-4 & CLEAR in center
        _samplerPads.Add(AddButton(544, 340, 134, 98, "1", 20, DefaultCoral, radius: 12));
        _samplerPads.Add(AddButton(688, 340, 134, 98, "2", 20, DefaultCoral, radius: 12));
        _samplerPads.Add(AddButton(544, 448, 134, 100, "3", 20, DefaultCoral, radius: 12));
        _samplerPads.Add(AddButton(688, 448, 134, 100, "4", 20, DefaultCoral, radius: 12));

        // Center CLEAR button
        _clearButton = AddButton(626, 404, 114, 78, "CLEAR", 15, DefaultCoral, radius: 8);

        // Bleep & Cough/Mute (Far right column)
        _bleepButton = AddButton(868, 340, 126, 74, "!@#$?*", 20, DefaultMagenta, radius: 10);

        // MUTE button with chamfered bottom-right corner
        _muteButton = AddButton(868, 426, 126, 122, "MUTE", 20, DefaultMagenta, radiusBottomRight: 28);
    }

    private Border AddButton(double x, double y, double w, double h, string text, double fontSize, Color color,
                             double radius = 6, double radiusBottomRight = -1)
    {
        double rBr = radiusBottomRight >= 0 ? radiusBottomRight : radius;
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var border = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(radius, radius, rBr, radius),
            Background = new SolidColorBrush(color),
            Child = label,
        };
        return Add(border, x, y);
    }

    private T Add<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _canvas.Children.Add(e);
        return e;
    }
}
