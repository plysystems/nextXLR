using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NextXLR.Controls;

/// <summary>Vertical peak meter with hold marker. Level/Hold are 0..1; height follows the layout.</summary>
public sealed partial class LevelMeter : UserControl
{
    public LevelMeter()
    {
        InitializeComponent();
        Mask.Height = 0;
        Root.SizeChanged += (_, _) => { UpdateMeteredState(); UpdateLevels(); };
    }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new PropertyMetadata(0.0, (d, _) => ((LevelMeter)d).UpdateLevels()));
    public static readonly DependencyProperty HoldLevelProperty = DependencyProperty.Register(
        nameof(HoldLevel), typeof(double), typeof(LevelMeter), new PropertyMetadata(0.0, (d, _) => ((LevelMeter)d).UpdateLevels()));
    public static readonly DependencyProperty IsMeteredProperty = DependencyProperty.Register(
        nameof(IsMetered), typeof(bool), typeof(LevelMeter), new PropertyMetadata(true, (d, _) => ((LevelMeter)d).UpdateMeteredState()));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double HoldLevel { get => (double)GetValue(HoldLevelProperty); set => SetValue(HoldLevelProperty, value); }
    public bool IsMetered { get => (bool)GetValue(IsMeteredProperty); set => SetValue(IsMeteredProperty, value); }

    private void UpdateMeteredState()
    {
        var on = IsMetered ? Visibility.Visible : Visibility.Collapsed;
        var off = IsMetered ? Visibility.Collapsed : Visibility.Visible;
        if (Gradient.Visibility != on) Gradient.Visibility = on;
        if (Mask.Visibility != on) Mask.Visibility = on;
        if (Hold.Visibility != on) Hold.Visibility = on;
        if (NoMeter.Visibility != off) NoMeter.Visibility = off;
    }

    private void UpdateLevels()
    {
        if (!IsMetered) return;
        var h = Root.ActualHeight;
        if (h <= 0) return;
        var maskH = (1 - Math.Clamp(Level, 0, 1)) * h;
        var holdY = (1 - Math.Clamp(HoldLevel, 0, 1)) * (h - 2);
        if (double.IsNaN(Mask.Height) || Math.Abs(Mask.Height - maskH) >= 0.2)
        {
            Mask.Height = maskH;
        }
        if (double.IsNaN(HoldOffset.Y) || Math.Abs(HoldOffset.Y - holdY) >= 0.2)
        {
            HoldOffset.Y = holdY;
        }
        Hold.Opacity = (IsMetered && HoldLevel > 0.005) ? 0.85 : 0.0;
    }
}
