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

        if (EditMenuButton != null)
        {
            CursorHelper.AttachHandCursor(EditMenuButton);
            EditMenuButton.Loaded += (_, _) => CursorHelper.AttachHandCursor(EditMenuButton);
        }
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
        CustomizeFlyout.ShowAt(EditMenuButton);
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

    private void PhonesLevelDisplay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsFixed) return;
        PhonesLevelInputBox.Text = Math.Round(ViewModel.SubmixVolume * 100).ToString();
        PhonesLevelDisplayPanel.Visibility = Visibility.Collapsed;
        PhonesLevelInputBox.Visibility = Visibility.Visible;
        PhonesLevelInputBox.SelectAll();
        PhonesLevelInputBox.Focus(FocusState.Programmatic);
    }

    private void PhonesLevelInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitPhonesLevelInput();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            PhonesLevelInputBox.Visibility = Visibility.Collapsed;
            PhonesLevelDisplayPanel.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void PhonesLevelInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (PhonesLevelInputBox.Visibility == Visibility.Visible)
        {
            CommitPhonesLevelInput();
        }
    }

    private void CommitPhonesLevelInput()
    {
        if (PhonesLevelInputBox.Visibility != Visibility.Visible) return;
        var text = PhonesLevelInputBox.Text.Trim().TrimEnd('%');
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var val) ||
            double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
        {
            var clamped = Math.Clamp(val, 0.0, 100.0);
            if (ViewModel is not null)
            {
                ViewModel.SubmixVolumePercent = clamped;
            }
        }
        PhonesLevelInputBox.Visibility = Visibility.Collapsed;
        PhonesLevelDisplayPanel.Visibility = Visibility.Visible;
    }

    private void StreamLevelDisplay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsFixed) return;
        StreamLevelInputBox.Text = Math.Round(ViewModel.Volume * 100).ToString();
        StreamLevelDisplayPanel.Visibility = Visibility.Collapsed;
        StreamLevelInputBox.Visibility = Visibility.Visible;
        StreamLevelInputBox.SelectAll();
        StreamLevelInputBox.Focus(FocusState.Programmatic);
    }

    private void StreamLevelInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitStreamLevelInput();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            StreamLevelInputBox.Visibility = Visibility.Collapsed;
            StreamLevelDisplayPanel.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void StreamLevelInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (StreamLevelInputBox.Visibility == Visibility.Visible)
        {
            CommitStreamLevelInput();
        }
    }

    private void CommitStreamLevelInput()
    {
        if (StreamLevelInputBox.Visibility != Visibility.Visible) return;
        var text = StreamLevelInputBox.Text.Trim().TrimEnd('%');
        if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var val) ||
            double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
        {
            var clamped = Math.Clamp(val, 0.0, 100.0);
            if (ViewModel is not null)
            {
                ViewModel.StreamVolumePercent = clamped;
            }
        }
        StreamLevelInputBox.Visibility = Visibility.Collapsed;
        StreamLevelDisplayPanel.Visibility = Visibility.Visible;
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
        (Brush)Application.Current.Resources["GlassCardBorderBrush"];

    public Brush TrackBorderStream(bool hasHardwareFader) =>
        (Brush)Application.Current.Resources["GlassCardBorderBrush"];

    public Brush SingleTrackBorder(bool hasHardwareFader, bool isSubmixActive, Brush? accent) =>
        (Brush)Application.Current.Resources["GlassCardBorderBrush"];

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

    public Visibility ShowSoloButton(bool isOutput) => isOutput ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DualMixItemVisibility(bool showDual) => showDual ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CanEditVisibility(bool canEdit) => canEdit ? Visibility.Visible : Visibility.Collapsed;

    public string MuteMenuText(bool muted) => muted ? "Stummschaltung aufheben" : "Stummschalten";
    public string HideMenuText(bool isHidden) => isHidden ? "Kanal wieder einblenden" : "Kanal ausblenden";
    public string HideMenuGlyph(bool isHidden) => isHidden ? "\uE7B3" : "\uE890";
    public double HiddenChannelOpacity(bool isHidden) => isHidden ? 0.6 : 1.0;
    public Visibility HiddenBadgeVisibility(bool isHidden) => isHidden ? Visibility.Visible : Visibility.Collapsed;

    private void ResetToUnity_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetToUnity();
    }

    private void CopyPhonesToStream_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CopyPhonesToStream();
    }

    private void CopyStreamToPhones_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CopyStreamToPhones();
    }

    private void ToggleMute_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMuted = !ViewModel.ActiveMuted;
    }

    private void ToggleHideChannel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsUserHidden = !ViewModel.IsUserHidden;
        }
    }

    public string ChannelTooltip(bool canEdit, string name) => canEdit ? Loc.Get("MixerCustomizeTitle.Text") : name;
    public string MuteGlyph(bool muted) => muted ? "\uE74F" : "\uE767";
    public string MuteText(bool muted) => Loc.Get(muted ? "MixerMuted" : "MixerMute");
    public string MuteName(string name) => Loc.Format("MixerMuteName", name);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> PeakStringCache = new();
    private static string FormatPeak(string peak) => PeakStringCache.GetOrAdd(peak, p => Loc.Format("MixerPeak", p));

    public string SubLabel(bool muted, string restore, bool metered, string peak, bool isFixed, string volumeDb) =>
        isFixed ? Loc.Get("MixerMeterOnly")
        : muted ? restore : volumeDb;
    public string LevelText(bool isFixed, string volumeLabel, string peak) => isFixed ? peak : volumeLabel;
    public Visibility HideIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowIfDual(bool showDual, bool isFixed, bool isOutput) => (showDual && !isFixed && !isOutput) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowIfSingle(bool showDual, bool isFixed, bool isOutput) => (!showDual || isFixed || isOutput) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowSingleBadge(bool hasFader, bool showDual) => (hasFader && !showDual) ? Visibility.Visible : Visibility.Collapsed;
    public string MeterTooltip(bool metered) => Loc.Get(metered ? "MixerMeterTooltip" : "MixerNoMeterTooltip");
    public string EditTooltip(bool isFixed) => isFixed ? Loc.Get("MixerMeterOnly") : Loc.Get("MixerClickToEdit");
}
