using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace NextXLR;

/// <summary>Icon entry as a class (x:DataType needs a concrete, non-nested type).</summary>
public sealed class IconCatalogEntry(string id, string glyph)
{
    public string Id { get; } = id;
    public string Glyph { get; } = glyph;
}

public sealed partial class ChannelStrip : UserControl
{
    private static readonly SolidColorBrush NeutralSurface = new(Windows.UI.Color.FromArgb(255, 0x22, 0x27, 0x32));
    private static readonly SolidColorBrush NeutralIcon = new(Windows.UI.Color.FromArgb(255, 0xC4, 0xCC, 0xD8));
    private static readonly SolidColorBrush DarkIcon = new(Windows.UI.Color.FromArgb(255, 0x0E, 0x10, 0x15));

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ChannelViewModel), typeof(ChannelStrip),
        new PropertyMetadata(null, (d, _) => ((ChannelStrip)d).Bindings.Update()));

    public ChannelViewModel ViewModel
    {
        get => (ChannelViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public IReadOnlyList<IconCatalogEntry> Icons { get; } =
        IconCatalog.All.Select(e => new IconCatalogEntry(e.Id, e.Glyph)).ToList();

    private string? _pendingIcon;
    private bool _iconChanged;

    public ChannelStrip()
    {
        InitializeComponent();

        // While the user holds the fader, server updates for this channel are deferred.
        Fader.DragStarted += (_, _) => SetDragging(true);
        Fader.DragCompleted += (_, _) => SetDragging(false);
    }

    private void SetDragging(bool dragging)
    {
        if (ViewModel is { } vm) vm.IsDragging = dragging;
    }

    // ---- Customise flyout: custom label + icon picker ------------------------------------

    private void CustomizeFlyout_Opening(object sender, object e)
    {
        if (ViewModel is null || !ViewModel.CanEdit)
        {
            CustomizeFlyout.Hide();
            return;
        }
        LabelBox.Text = ViewModel.Name;
        _pendingIcon = ViewModel.IconId;
        _iconChanged = false;
        var current = ViewModel.IconId ?? ViewModel.DefaultIconId;
        IconGrid.SelectedItem = Icons.FirstOrDefault(i => i.Id == current);
    }

    private void IconGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanEdit) return;
        IconGrid.SelectedItem = e.ClickedItem;
        _pendingIcon = ((IconCatalogEntry)e.ClickedItem).Id;
        _iconChanged = true;
    }

    private void ResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanEdit) return;
        _pendingIcon = null;
        _iconChanged = true;
        IconGrid.SelectedItem = Icons.FirstOrDefault(i => i.Id == ViewModel.DefaultIconId);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Commit();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanEdit) return;
        CustomizeFlyout.ShowAt(CustomizeButton);
    }

    private void LabelBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) Commit();
    }

    private void Commit()
    {
        if (ViewModel is null || !ViewModel.CanEdit)
        {
            CustomizeFlyout.Hide();
            return;
        }
        ViewModel.Rename(LabelBox.Text);
        if (_iconChanged) ViewModel.SetIcon(_pendingIcon);
        CustomizeFlyout.Hide();
    }

    // ---- Inline volume editing (click percentage under slider) --------------------------

    private void LevelDisplay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        StartLevelInput();
    }

    private void LevelDisplay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StartLevelInput();
    }

    private void StartLevelInput()
    {
        if (ViewModel is null || ViewModel.IsFixed) return;
        LevelInputBox.Text = Math.Round(ViewModel.Volume * 100).ToString();
        LevelDisplayPanel.Visibility = Visibility.Collapsed;
        LevelInputBox.Visibility = Visibility.Visible;
        LevelInputBox.SelectAll();
        LevelInputBox.Focus(FocusState.Programmatic);
    }

    private void LevelInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitLevelInput();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelLevelInput();
            e.Handled = true;
        }
    }

    private void LevelInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (LevelInputBox.Visibility == Visibility.Visible)
        {
            CommitLevelInput();
        }
    }

    private void CommitLevelInput()
    {
        if (LevelInputBox.Visibility != Visibility.Visible) return;
        var text = LevelInputBox.Text.Trim().TrimEnd('%');
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var val) ||
            double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
        {
            var min = ViewModel?.MinVolumePercent ?? 0.0;
            var max = ViewModel?.MaxVolumePercent ?? 100.0;
            var clamped = Math.Clamp(val, min, max);
            if (ViewModel is not null)
            {
                ViewModel.Volume = clamped / 100.0;
            }
        }
        LevelInputBox.Visibility = Visibility.Collapsed;
        LevelDisplayPanel.Visibility = Visibility.Visible;
    }

    private void CancelLevelInput()
    {
        LevelInputBox.Visibility = Visibility.Collapsed;
        LevelDisplayPanel.Visibility = Visibility.Visible;
    }

    // ---- x:Bind helpers ------------------------------------------------------------------
    public Brush ChannelIconBackground(Brush? accent)
    {
        if (accent is SolidColorBrush sb && (sb.Color.R == 0x5E && sb.Color.G == 0x66 && sb.Color.B == 0x75))
            return NeutralSurface;
        return accent ?? NeutralSurface;
    }

    public Brush ChannelIconForeground(Brush? accent)
    {
        if (accent is SolidColorBrush sb && (sb.Color.R == 0x5E && sb.Color.G == 0x66 && sb.Color.B == 0x75))
            return NeutralIcon;
        return DarkIcon;
    }

    public string ChannelTooltip(bool canEdit, string name) => canEdit ? Loc.Get("MixerCustomize") : name;
    public string MuteGlyph(bool muted) => muted ? "\uE74F" : "\uE767";
    public string MuteText(bool muted) => Loc.Get(muted ? "MixerMuted" : "MixerMute");
    public string MuteName(string name) => Loc.Format("MixerMuteName", name);
    public string SubLabel(bool muted, string restore, bool metered, string peak, bool isFixed) =>
        isFixed ? Loc.Get("MixerMeterOnly")
        : muted ? restore : metered ? Loc.Format("MixerPeak", peak) : Loc.Get("MixerNoMeterShort");
    public string LevelText(bool isFixed, string level, string peak) => isFixed ? peak : level;
    public Visibility HideIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public string MeterTooltip(bool metered) => Loc.Get(metered ? "MixerMeterTooltip" : "MixerNoMeterTooltip");
    public string EditTooltip(bool isFixed) => isFixed ? Loc.Get("MixerMeterOnly") : Loc.Get("MixerClickToEdit");
}
