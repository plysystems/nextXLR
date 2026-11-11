using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI;
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

    public void SetViewModel(ChannelViewModel? vm)
    {
        if (ReferenceEquals(ViewModel, vm)) return;
        ViewModel = vm!;
        if (DataContext != vm) DataContext = vm;
    }

    public IReadOnlyList<IconCatalogEntry> Icons { get; } =
        IconCatalog.All.Select(e => new IconCatalogEntry(e.Id, e.Glyph)).ToList();

    private string? _pendingIcon;
    private bool _iconChanged;

    public ChannelStrip()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChannelViewModel vm && !ReferenceEquals(ViewModel, vm))
            {
                ViewModel = vm;
            }
        };

        // While the user holds any fader, server updates for this channel are deferred.
        StreamFader.ValueChangedByUser = v =>
        {
            if (ViewModel is { } vm) vm.StreamVolumePercent = v;
        };
        StreamFader.DragStarted += (_, _) => SetDragging(true);
        StreamFader.DragCompleted += (_, _) => SetDragging(false);

        PhonesFader.ValueChangedByUser = v =>
        {
            if (ViewModel is { } vm) vm.SubmixVolumePercent = v;
        };
        PhonesFader.DragStarted += (_, _) => SetDragging(true);
        PhonesFader.DragCompleted += (_, _) => SetDragging(false);

        Fader.ValueChangedByUser = v =>
        {
            if (ViewModel is { } vm) vm.VolumePercent = v;
        };
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
                ViewModel.VolumePercent = clamped;
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

    public Brush TrackBorderPhones(bool hasHardwareFader) =>
        hasHardwareFader ? (Brush)Application.Current.Resources["HeadphoneBrush"] : (Brush)Application.Current.Resources["SurfaceStrokeSubtleBrush"];

    public Brush TrackBorderStream(bool hasHardwareFader) =>
        hasHardwareFader ? (Brush)Application.Current.Resources["BrandBrush"] : (Brush)Application.Current.Resources["SurfaceStrokeSubtleBrush"];

    public Brush SingleTrackBorder(bool hasHardwareFader, bool isSubmixActive, Brush? accent)
    {
        if (!hasHardwareFader)
            return (Brush)Application.Current.Resources["SurfaceStrokeSubtleBrush"];
        return isSubmixActive
            ? (Brush)Application.Current.Resources["HeadphoneBrush"]
            : (Brush)Application.Current.Resources["BrandBrush"];
    }

    public Brush SingleFaderAccent(bool isSubmixActive, Brush? accent)
    {
        return isSubmixActive
            ? (Brush)Application.Current.Resources["HeadphoneBrush"]
            : (accent ?? (Brush)Application.Current.Resources["BrandBrush"]);
    }

    public string SingleMixGlyph(bool isOutput, bool isSubmixActive) =>
        isOutput ? "\uE767" : (isSubmixActive ? "\uE7F6" : "\uE774");

    public string SingleMixBadgeText(bool isOutput, bool isSubmixActive) =>
        isOutput ? "OUTPUT" : (isSubmixActive ? "PHONES" : "STREAM");

    public Brush SingleMixForeground(bool isOutput, bool isSubmixActive) =>
        (isOutput || isSubmixActive)
            ? (Brush)Application.Current.Resources["HeadphoneBrush"]
            : (Brush)Application.Current.Resources["BrandBrush"];

    public Visibility SingleFaderBadgeVisibility(bool hasFader) =>
        hasFader ? Visibility.Visible : Visibility.Collapsed;

    public string ChannelTooltip(bool canEdit, string name) => canEdit ? Loc.Get("MixerCustomizeTitle.Text") : name;
    public string MuteGlyph(bool muted) => muted ? "\uE74F" : "\uE767";
    public string MuteText(bool muted) => Loc.Get(muted ? "MixerMuted" : "MixerMute");
    public string MuteName(string name) => Loc.Format("MixerMuteName", name);
    public string SubLabel(bool muted, string restore, bool metered, string peak, bool isFixed) =>
        isFixed ? Loc.Get("MixerMeterOnly")
        : muted ? restore : metered ? Loc.Format("MixerPeak", peak) : Loc.Get("MixerNoMeterShort");
    public string LevelText(bool isFixed, string level, string peak) => isFixed ? peak : level;
    public Visibility HideIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ShowIfDual(bool showDual, bool isFixed, bool isOutput) => (showDual && !isFixed && !isOutput) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowIfSingle(bool showDual, bool isFixed, bool isOutput) => (!showDual || isFixed || isOutput) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowSingleBadge(bool hasFader, bool showDual) => (hasFader && !showDual) ? Visibility.Visible : Visibility.Collapsed;
    public string MeterTooltip(bool metered) => Loc.Get(metered ? "MixerMeterTooltip" : "MixerNoMeterTooltip");
    public string EditTooltip(bool isFixed) => isFixed ? Loc.Get("MixerMeterOnly") : Loc.Get("MixerClickToEdit");
}
