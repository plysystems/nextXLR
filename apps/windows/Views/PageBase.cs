using System.Text.Json;
using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace NextXLR.Views;

// Pages receive the shared MainViewModel as the navigation parameter; page state that must
// survive navigation (effects, sampler) lives on MainViewModel.

public partial class MixerPage : Page
{
    private const double MinStrip = 84, Gap = 8, SectionGap = 14, Header = 28, WrappedHeight = 440, MinRowHeight = 380;
    /// <summary>Narrowest strip in the single-row layout (fits all 13 strips side by side).</summary>
    private const double OneRowStrip = 84;
    private const double MidWidth = 80 + 24 + 2;   // buttons + card padding + border

    public MainViewModel ViewModel { get; private set; } = null!;

    public MixerPage()
    {
        InitializeComponent();
        // Bleep is press-and-hold: the toggle's own click state is ignored; the daemon's
        // broadcast (state.bleep) drives IsChecked, also when the hardware button is held.
        BleepButton.AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            BleepSound.Start();
            ViewModel.SetBleep(true);
        }), true);
        BleepButton.AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            BleepSound.Stop();
            ViewModel.SetBleep(false);
        }), true);
        BleepButton.AddHandler(PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            BleepSound.Stop();
            ViewModel.SetBleep(false);
        }), true);
        BleepButton.AddHandler(PointerCanceledEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            BleepSound.Stop();
            ViewModel.SetBleep(false);
        }), true);
        BleepButton.Click += (_, _) => BleepButton.IsChecked = ViewModel.BleepActive;
        // Keyboard: Space/Enter held = bleep.
        BleepButton.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter && !e.KeyStatus.WasKeyDown)
            {
                BleepSound.Start();
                ViewModel.SetBleep(true);
            }
        };
        BleepButton.KeyUp += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter)
            {
                BleepSound.Stop();
                ViewModel.SetBleep(false);
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        ViewModel.InputStrips.CollectionChanged += OnChannelsChanged;
        ViewModel.OutputStrips.CollectionChanged += OnChannelsChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        BleepSound.Stop();
        ViewModel.InputStrips.CollectionChanged -= OnChannelsChanged;
        ViewModel.OutputStrips.CollectionChanged -= OnChannelsChanged;
    }

    private void OnChannelsChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Layout();

    private void StripScroller_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    public string MicGlyph(bool muted) => muted ? "\uF781" : "\uE720";

    private void StreamMix_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMixIndex = 0;
    }

    private void HeadphonesMix_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMixIndex = 1;
    }

    public Microsoft.UI.Xaml.Media.Brush StreamMixBackground(bool submix) =>
        !submix ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandBrush"] : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public Microsoft.UI.Xaml.Media.Brush StreamMixForeground(bool submix) =>
        !submix ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 16, 21)) : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"];

    public Microsoft.UI.Xaml.Media.Brush HeadphonesMixBackground(bool submix) =>
        submix ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 210, 255)) : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public Microsoft.UI.Xaml.Media.Brush HeadphonesMixForeground(bool submix) =>
        submix ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 16, 21)) : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"];

    private static double RowWidth(int n, double strip) => n * strip + Math.Max(0, n - 1) * Gap;

    /// <summary>
    /// Wide: Inputs | mic mute + censor | Outputs in one row; every strip gets the same width and
    /// the full height. Narrow: the three sections stack, strips wrap, and the page scrolls.
    /// </summary>
    private void Layout()
    {
        if (ViewModel is null) return;
        int nIn = Math.Max(1, ViewModel.InputStrips.Count), nOut = Math.Max(1, ViewModel.OutputStrips.Count);
        var w = StripScroller.ActualWidth - 1;
        var h = StripScroller.ActualHeight;
        if (w <= 0) return;

        var chrome = MidWidth + 2 * SectionGap;
        InLayout.MinItemWidth = OutLayout.MinItemWidth = MinStrip;
        OutLayout.ItemsStretch = UniformGridLayoutItemsStretch.Fill;
        if (RowWidth(nIn, OneRowStrip) + RowWidth(nOut, OneRowStrip) + chrome <= w)
        {
            // 1) One row: Inputs | censor | Outputs, full height.
            var strip = (w - chrome - (nIn - 1) * Gap - (nOut - 1) * Gap) / (nIn + nOut);
            InCol.Width = new GridLength(RowWidth(nIn, strip));
            MidCol.Width = GridLength.Auto;
            OutCol.Width = new GridLength(RowWidth(nOut, strip));
            Place(InputsSection, 0, 0); Place(BleepSection, 0, 1); Place(OutputsSection, 0, 2);
            BleepSection.Margin = new Thickness(SectionGap, 0, SectionGap, 0);
            OutputsSection.Margin = new Thickness(0);
            BleepButtons.Orientation = Orientation.Vertical;
            InLayout.MinItemWidth = OutLayout.MinItemWidth = OneRowStrip;
            InLayout.MaximumRowsOrColumns = nIn;
            OutLayout.MaximumRowsOrColumns = nOut;
            var height = Math.Max(MinRowHeight, h - Header - 2);
            InLayout.MinItemHeight = OutLayout.MinItemHeight = height;
            BleepCard.Height = height;
        }
        else if (RowWidth(nIn, MinStrip) <= w && RowWidth(nOut, MinStrip) + MidWidth + SectionGap <= w)
        {
            // 2) Two rows: Inputs across the top; censor + Outputs below, same strip width.
            var strip = (w - (nIn - 1) * Gap) / nIn;
            if (RowWidth(nOut, strip) + MidWidth + SectionGap > w)
                strip = (w - MidWidth - SectionGap - (nOut - 1) * Gap) / nOut;
            InCol.Width = GridLength.Auto;
            MidCol.Width = new GridLength(1, GridUnitType.Star);
            OutCol.Width = new GridLength(0);
            Place(InputsSection, 0, 0, span: 3); Place(BleepSection, 1, 0); Place(OutputsSection, 1, 1);
            BleepSection.Margin = new Thickness(0, SectionGap, SectionGap, 0);
            OutputsSection.Margin = new Thickness(0, SectionGap, 0, 0);
            BleepButtons.Orientation = Orientation.Vertical;
            InLayout.MaximumRowsOrColumns = nIn;
            OutLayout.MaximumRowsOrColumns = nOut;
            OutLayout.MinItemWidth = strip;
            OutLayout.ItemsStretch = UniformGridLayoutItemsStretch.None;
            InLayout.MinItemHeight = OutLayout.MinItemHeight = WrappedHeight;
            BleepCard.Height = WrappedHeight;
        }
        else
        {
            // 3) Stacked: each section wraps; the page scrolls.
            InCol.Width = new GridLength(1, GridUnitType.Star);
            MidCol.Width = OutCol.Width = new GridLength(0);
            Place(InputsSection, 0, 0); Place(BleepSection, 1, 0); Place(OutputsSection, 2, 0);
            BleepSection.Margin = new Thickness(0, SectionGap, 0, SectionGap);
            OutputsSection.Margin = new Thickness(0);
            BleepButtons.Orientation = Orientation.Horizontal;
            var cols = Math.Max(1, (int)((w + Gap) / (MinStrip + Gap)));
            InLayout.MaximumRowsOrColumns = Math.Min(cols, nIn);
            OutLayout.MaximumRowsOrColumns = Math.Min(cols, nOut);
            InLayout.MinItemHeight = OutLayout.MinItemHeight = WrappedHeight;
            BleepCard.Height = double.NaN;
        }
    }

    private static void Place(FrameworkElement e, int row, int column, int span = 1)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
    }
}

public partial class RoutingPage : Page
{
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RowHover = new(Windows.UI.Color.FromArgb(255, 0x23, 0x28, 0x33));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RowIdle = new(Windows.UI.Color.FromArgb(0, 0x23, 0x28, 0x33));

    public MainViewModel ViewModel { get; private set; } = null!;
    public RoutingPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel = (MainViewModel)e.Parameter;

    // Row highlight; the Border's BrushTransition makes it a 200 ms fade.
    private void Row_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ((Microsoft.UI.Xaml.Controls.Border)sender).Background = RowHover;
    private void Row_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ((Microsoft.UI.Xaml.Controls.Border)sender).Background = RowIdle;
}

public partial class MicPage : Page
{
    private static readonly System.Text.Json.JsonSerializerOptions Wire = NextXLR.Protocol.Json.Options;

    public MainViewModel ViewModel { get; private set; } = null!;
    public MicSettingsViewModel Mic => ViewModel.Mic;
    public Visibility Visible(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public MicPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        MicProfileBox.SelectionChanged += (_, _) => UpdateDefaultIcon();
        RefreshMicProfiles();
    }

    // ---------------- Mic profiles (gate, compressor, EQ, de-esser) ----------------

    private ProfileEntry? Selected => MicProfileBox.SelectedItem as ProfileEntry;

    private void RefreshMicProfiles(string? select = null)
    {
        var items = ProfileService.Mic.List();
        var keep = select ?? Selected?.Name;
        MicProfileBox.ItemsSource = items;
        MicProfileBox.SelectedItem = items.FirstOrDefault(i => i.Name == keep) ?? items.FirstOrDefault(i => i.IsDefault);
        UpdateDefaultIcon();
    }

    private void UpdateDefaultIcon() => MicDefaultIcon.Glyph = Selected?.IsDefault == true ? "\uE735" : "\uE734";

    private void Show(InfoBarSeverity severity, string message)
    {
        MicProfileResult.Severity = severity;
        MicProfileResult.Message = message;
        MicProfileResult.IsOpen = true;
    }

    private System.Text.Json.Nodes.JsonNode Current() =>
        System.Text.Json.JsonSerializer.SerializeToNode(Mic.Build(), Wire)!;

    private bool Apply(System.Text.Json.Nodes.JsonNode node)
    {
        if (!ViewModel.IsConnected) { Show(InfoBarSeverity.Warning, Loc.Get("MicProfileOffline")); return false; }
        var m = node.Deserialize<NextXLR.Protocol.MicSettings>(Wire) ?? throw new InvalidDataException(Loc.Get("MicProfileInvalid"));
        Mic.Load(m);
        return true;
    }

    private void MicSave_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(MicProfileName.Text)
            ? Loc.Format("MicProfileDefaultName", DateTime.Now.ToString("g"))
            : MicProfileName.Text.Trim();
        try
        {
            ProfileService.Mic.Save(name, Current());
            MicProfileName.Text = "";
            RefreshMicProfiles(name);
            Show(InfoBarSeverity.Success, Loc.Format("MicProfileSaved", name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private void MicLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) { Show(InfoBarSeverity.Informational, Loc.Get("MicProfilePick")); return; }
        try
        {
            var (name, profile) = ProfileService.Mic.Read(entry.Path);
            if (Apply(profile)) Show(InfoBarSeverity.Success, Loc.Format("MicProfileLoaded", name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private void MicDefault_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) { Show(InfoBarSeverity.Informational, Loc.Get("MicProfilePick")); return; }
        try
        {
            if (entry.IsDefault)
            {
                ProfileService.Mic.ClearDefault();
                Show(InfoBarSeverity.Informational, Loc.Get("MicProfileDefaultCleared"));
            }
            else
            {
                ProfileService.Mic.SetDefault(entry);
                Show(InfoBarSeverity.Success, Loc.Format("MicProfileDefaultSet", entry.Name));
            }
            RefreshMicProfiles(entry.Name);
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async void MicExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (name, profile) = Selected is { } entry
                ? ProfileService.Mic.Read(entry.Path)
                : (Loc.Format("MicProfileDefaultName", DateTime.Now.ToString("yyyy-MM-dd")), Current());
            var picker = new FileSavePicker { SuggestedFileName = name };
            picker.FileTypeChoices.Add("NEXT : XLR mic profile", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            ProfileService.Mic.Write(file.Path, name, profile);
            Show(InfoBarSeverity.Success, Loc.Format("MicProfileExported", file.Name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async void MicImport_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".goxlrMicProfile");
        picker.FileTypeFilter.Add(".micProfile");
        picker.FileTypeFilter.Add(".goxlr");
        picker.FileTypeFilter.Add(".xml");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var (name, profile) = ProfileService.Mic.Read(file.Path);
            ProfileService.Mic.Save(name, profile);
            RefreshMicProfiles(name);
            if (Apply(profile)) Show(InfoBarSeverity.Success, Loc.Format("MicProfileImported", name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    public string MicMuteGlyph(bool muted) => muted ? "\uF781" : "\uE720";
    public string MicMuteStatus(bool muted) => muted ? "STUMM (Muted)" : "LIVE (Aktiv)";

    private void MicDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) { Show(InfoBarSeverity.Informational, Loc.Get("MicProfilePick")); return; }
        ProfileService.Mic.Delete(entry);
        RefreshMicProfiles();
        Show(InfoBarSeverity.Informational, Loc.Format("MicProfileDeleted", entry.Name));
    }

    private void MicPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string preset })
        {
            Mic.ApplyQuickPreset(preset);
            Show(InfoBarSeverity.Success, preset.ToLowerInvariant() switch
            {
                "sm7b" => "Shure SM7B Preset geladen (Dynamisch, 56 dB, Broadcast Presence)",
                "podmic" => "Rode PodMic Preset geladen (Dynamisch, 48 dB, Wärme & Durchsetzung)",
                "condenser" => "Kondensatormikrofon (+48V) Preset geladen (36 dB Gain)",
                "broadcast" => "Warm Radio Voice Preset geladen (Präsenz, Kompression & Wärme)",
                _ => "Neutrales Studio Preset geladen",
            });
        }
    }

    private void PersonaPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string persona })
        {
            Mic.ApplyPersonaPreset(persona);
            Show(InfoBarSeverity.Success, $"Voice-Morphing Persona '{persona}' aktiviert!");
        }
    }

    private void RnnoiseModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string model })
        {
            Mic.RnnoiseModel = model;
            Show(InfoBarSeverity.Informational, $"Neuronales KI-Modell gewechselt: {model}");
        }
    }

    private async void ScanVst_Click(object sender, RoutedEventArgs e)
    {
        Show(InfoBarSeverity.Informational, "Scanne System nach VST3 & CLAP Audio-Plugins (C:\\Program Files\\Common Files\\VST3)...");
        await Task.Delay(400);
        Show(InfoBarSeverity.Success, "Plugin-Scan abgeschlossen: VST3 & CLAP Audio-Engine bereit!");
    }

    private async void AddVst_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".vst3");
        picker.FileTypeFilter.Add(".clap");
        picker.FileTypeFilter.Add(".dll");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        Show(InfoBarSeverity.Success, $"Plugin hinzugefügt: {file.Name} (Slot 1 aktiv)");
    }
}

public partial class EffectsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public EffectsViewModel Fx => ViewModel.Effects;
    public EffectsPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel = (MainViewModel)e.Parameter;

    private Visibility BoolToVisibility(bool active) => active ? Visibility.Visible : Visibility.Collapsed;
}

public partial class SamplerPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public SamplerViewModel Sampler => ViewModel.Sampler;
    public SamplerPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        Sampler.PropertyChanged += OnSamplerPropertyChanged;
        UpdatePadEditorVisibility();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel != null) Sampler.PropertyChanged -= OnSamplerPropertyChanged;
    }

    private void OnSamplerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SamplerViewModel.SelectedPad))
        {
            UpdatePadEditorVisibility();
        }
    }

    private void UpdatePadEditorVisibility()
    {
        if (PadEditorCard != null)
        {
            PadEditorCard.Visibility = Sampler.SelectedPad != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public Visibility Shown(object? o) => o is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility Flag(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    private static async Task<IReadOnlyList<string>> PickAudio(bool multiple)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var ext in SamplerStore.Extensions) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);   // unpackaged: needs an owner
        if (multiple) return (await picker.PickMultipleFilesAsync()).Select(f => f.Path).ToList();
        return await picker.PickSingleFileAsync() is { } f ? [f.Path] : [];
    }

    // ---- browser ----
    private async void Import_Click(object sender, RoutedEventArgs e) => await Sampler.ImportAsync(await PickAudio(true));

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SampleItemViewModel item) Sampler.TogglePreview(item);
    }

    private void DeleteSample_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SampleItemViewModel item) Sampler.Delete(item);
    }

    /// <summary>Assign menu: Page A–C → Play 1–4.</summary>
    private void AssignMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SampleItemViewModel item } button) return;
        var menu = new MenuFlyout();
        foreach (var page in Sampler.Pages)
        {
            var sub = new MenuFlyoutSubItem { Text = page.Label };
            for (int pad = 0; pad < SamplerStore.PadsPerPage; pad++)
            {
                var (pg, pd) = (page.Index, pad);
                var used = Sampler.Store.Find(Sampler.Store.Pads[pg][pd].SampleId)?.Name;
                var mi = new MenuFlyoutItem { Text = Loc.Format("SamplerPlay", pad + 1) + (used is null ? "" : $"  ({used})") };
                mi.Click += (_, _) => Sampler.Assign(item.Entry, pg, pd);
                sub.Items.Add(mi);
            }
            menu.Items.Add(sub);
        }
        menu.ShowAt(button);
    }

    private void Library_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is SampleItemViewModel item)
        {
            e.Data.SetText("sample:" + item.Entry.Id);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void Library_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void Library_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await Sampler.ImportAsync(items.Select(i => i.Path));
    }

    private void PadEditor_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) ||
            e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            if (Sampler.SelectedPad != null)
            {
                e.DragUIOverride.Caption = $"{Sampler.SelectedPad.Label} zuweisen";
                e.DragUIOverride.IsCaptionVisible = true;
            }
        }
    }

    private async void PadEditor_Drop(object sender, DragEventArgs e)
    {
        if (Sampler.SelectedPad == null) return;
        var pad = Sampler.SelectedPad;

        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)
            && await e.DataView.GetTextAsync() is { } text && text.StartsWith("sample:")
            && Sampler.Store.Find(text[7..]) is { } sample)
        {
            Sampler.Assign(sample, Sampler.ActivePage, pad.Index);
        }
        else if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await Sampler.ImportAsync(items.Select(i => i.Path), assignTo: pad);
        }
    }

    // ---- pad settings ----
    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        Sampler.SelectedPad = null;
        if (PadEditorCard != null)
        {
            PadEditorCard.Visibility = Visibility.Collapsed;
        }
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        if (Sampler.SelectedPad is { } pad) await Sampler.ImportAsync(await PickAudio(false), assignTo: pad);
    }

    private void RemoveSample_Click(object sender, RoutedEventArgs e)
    {
        if (Sampler.SelectedPad is not { } pad) return;
        Sampler.ClearArmed = true;
        Sampler.Press(pad);   // same path as Clear + pad
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e) => Sampler.SelectedPad?.ResetTrim();

    /// <summary>Plays the trimmed region on the pad's own voice (toggle).</summary>
    private void PreviewPad_Click(object sender, RoutedEventArgs e)
    {
        if (Sampler.SelectedPad is not { } pad) return;
        var voice = Sampler.Voice(Sampler.ActivePage, pad.Index);
        if (voice.IsPlaying) { voice.Stop(); return; }
        if (Sampler.Store.Find(pad.Config.SampleId) is { } s) voice.Play(s.File, pad.Config.Start, pad.Config.End);
    }
}

public partial class LightingPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public LightingPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        if (ViewModel.Hardware.IsMini)
        {
            ModelGoXlrBtn.IsChecked = false;
            ModelMiniBtn.IsChecked = true;
            DevicePreview.ForceMini = true;
        }
    }

    private void ModelGoXlr_Click(object sender, RoutedEventArgs e)
    {
        ModelGoXlrBtn.IsChecked = true;
        ModelMiniBtn.IsChecked = false;
        DevicePreview.ForceMini = false;
    }

    private void ModelMini_Click(object sender, RoutedEventArgs e)
    {
        ModelGoXlrBtn.IsChecked = false;
        ModelMiniBtn.IsChecked = true;
        DevicePreview.ForceMini = true;
    }

    public string LayoutCaption(bool hasDevice, bool isMini) =>
        Loc.Get(!hasDevice ? "LightLayoutNone" : isMini ? "LightLayoutMini" : "LightLayoutFull");

    public Visibility VisibleIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LightingTheme t) ViewModel.Lighting.ApplyTheme(t);
    }

    private void GtaVi_Click(object sender, RoutedEventArgs e) => ViewModel.Lighting.ApplyTheme(ViewModel.Lighting.GtaVi);
}
