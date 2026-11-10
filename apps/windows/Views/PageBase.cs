using System.Diagnostics;
using System.Text.Json;
using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.UI;

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
        NavigationCacheMode = NavigationCacheMode.Required;
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

        BleepSlider.PointerWheelChanged += (s, e) =>
        {
            var hasModifier = (e.KeyModifiers & (Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift)) != 0;
            if (!hasModifier && BleepSlider.FocusState == FocusState.Unfocused)
            {
                return;
            }
            var delta = e.GetCurrentPoint(BleepSlider).Properties.MouseWheelDelta;
            ViewModel.BleepVolume = Math.Clamp(ViewModel.BleepVolume + Math.Sign(delta), 0, 100);
            e.Handled = true;
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

    public AutoDuckingService AutoDucking => AutoDuckingService.Instance;

    private void BothMixes_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMixIndex = 0;
    }

    private void StreamMix_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMixIndex = 1;
    }

    private void HeadphonesMix_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.ActiveMixIndex = 2;
    }

    private void MixerPage1_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SwitchMixerPage(1);
    }

    private void MixerPage2_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SwitchMixerPage(2);
    }

    private readonly SolidColorBrush ActiveBrandBg = new(Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xB8));
    private readonly SolidColorBrush ActiveHeadphonesBg = new(Windows.UI.Color.FromArgb(255, 0x58, 0xA6, 0xFF));
    private readonly SolidColorBrush ActiveTextFg = new(Windows.UI.Color.FromArgb(255, 14, 16, 21));
    private readonly SolidColorBrush TransparentBg = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    private readonly SolidColorBrush MutedTextFg = new(Windows.UI.Color.FromArgb(255, 0x8B, 0x93, 0xA3));

    public Microsoft.UI.Xaml.Media.Brush Page1Background(int activePage) => activePage == 1 ? ActiveBrandBg : TransparentBg;
    public Microsoft.UI.Xaml.Media.Brush Page1Foreground(int activePage) => activePage == 1 ? ActiveTextFg : MutedTextFg;
    public Microsoft.UI.Xaml.Media.Brush Page2Background(int activePage) => activePage == 2 ? ActiveBrandBg : TransparentBg;
    public Microsoft.UI.Xaml.Media.Brush Page2Foreground(int activePage) => activePage == 2 ? ActiveTextFg : MutedTextFg;
    public Microsoft.UI.Xaml.Media.Brush BothMixesBackground(int index) => index == 0 ? ActiveBrandBg : TransparentBg;
    public Microsoft.UI.Xaml.Media.Brush BothMixesForeground(int index) => index == 0 ? ActiveTextFg : MutedTextFg;
    public Microsoft.UI.Xaml.Media.Brush StreamMixBackground(int index) => index == 1 ? ActiveBrandBg : TransparentBg;
    public Microsoft.UI.Xaml.Media.Brush StreamMixForeground(int index) => index == 1 ? ActiveTextFg : MutedTextFg;
    public Microsoft.UI.Xaml.Media.Brush HeadphonesMixBackground(int index) => index == 2 ? ActiveHeadphonesBg : TransparentBg;
    public Microsoft.UI.Xaml.Media.Brush HeadphonesMixForeground(int index) => index == 2 ? ActiveTextFg : MutedTextFg;

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
        double effectiveStrip = ViewModel.ActiveMixIndex == 0 ? 104 : MinStrip;
        double effectiveOneRow = ViewModel.ActiveMixIndex == 0 ? 104 : OneRowStrip;
        InLayout.MinItemWidth = effectiveStrip;
        OutLayout.MinItemWidth = MinStrip;
        OutLayout.ItemsStretch = UniformGridLayoutItemsStretch.Fill;
        if (RowWidth(nIn, effectiveOneRow) + RowWidth(nOut, OneRowStrip) + chrome <= w)
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
            InLayout.MinItemWidth = effectiveOneRow;
            OutLayout.MinItemWidth = OneRowStrip;
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
    public RoutingPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        ViewModel.AppRouting.StartMonitoring();
        ViewModel.AppRouting.Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.AppRouting.StopMonitoring();
        base.OnNavigatedFrom(e);
    }

    private void RefreshAppRouting_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AppRouting.Refresh();
    }

    private void AutoAssignApps_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AppRouting.AutoAssignAll();
    }

    private void OpenWindowsMixer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AppRouting.OpenWindowsMixer();
    }

    // Row highlight; the Border's BrushTransition makes it a 200 ms fade.
    private void Row_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Border b) b.Background = RowHover;
    }

    private void Row_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Border b) b.Background = RowIdle;
    }
}

public partial class MicPage : Page
{
    private static readonly System.Text.Json.JsonSerializerOptions Wire = NextXLR.Protocol.Json.Options;

    public MainViewModel ViewModel { get; private set; } = null!;
    public MicSettingsViewModel Mic => ViewModel.Mic;
    public VstPluginService Vst => VstPluginService.Instance;
    public SafetyLimiterService SafetyLimiter => SafetyLimiterService.Instance;
    public Visibility Visible(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public MicPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            ViewModel = (MainViewModel)e.Parameter;
            VstPluginService.ActiveMicViewModel = Mic;
            MicProfileBox.SelectionChanged -= MicProfileBox_SelectionChanged;
            MicProfileBox.SelectionChanged += MicProfileBox_SelectionChanged;
            RefreshMicProfiles();
            if (Selected is { } defEntry)
            {
                try
                {
                    var (_, _, rawDoc) = ProfileService.Mic.ReadFull(defEntry.Path);
                    VstPluginService.Instance.LoadRackForProfile(defEntry.Name, rawDoc);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during MicPage.OnNavigatedTo: {ex}");
        }
    }

    private void MicProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDefaultIcon();
        if (Selected is { } entry)
        {
            try
            {
                var (_, _, rawDoc) = ProfileService.Mic.ReadFull(entry.Path);
                VstPluginService.Instance.LoadRackForProfile(entry.Name, rawDoc);
            }
            catch { }
        }
    }

    private void VstMasterToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
        {
            VstPluginService.Instance.IsMasterEnabled = ts.IsOn;
            Show(InfoBarSeverity.Informational, ts.IsOn
                ? "VST3/CLAP Master-Rack aktiviert."
                : "VST3/CLAP Master-Rack im Bypass (Clean Pass-Through).");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            VstPluginService.Instance.SaveRack();
            var profileNode = Current();
            if (Selected is { } entry)
            {
                ProfileService.Mic.Save(entry.Name, profileNode);
                VstPluginService.Instance.SaveRackForProfile(entry.Name);
            }
            else
            {
                var defName = ProfileService.Mic.DefaultName ?? "Standard";
                ProfileService.Mic.Save(defName, profileNode);
            }
        }
        catch { }
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

    private async void MicSave_Click(object sender, RoutedEventArgs e)
    {
        var inputName = MicProfileName.Text.Trim();
        string targetName;

        if (string.IsNullOrWhiteSpace(inputName))
        {
            if (Selected is not { } selected)
            {
                Show(InfoBarSeverity.Warning, Loc.Get("MicProfilePick"));
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Profil überschreiben?",
                Content = $"Möchten Sie das ausgewählte Profil '{selected.Name}' wirklich mit den aktuellen Einstellungen überschreiben?",
                PrimaryButtonText = "Überschreiben",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Primary
            };

            var res = await dialog.ShowAsync();
            if (res != ContentDialogResult.Primary) return;

            targetName = selected.Name;
        }
        else
        {
            targetName = inputName;
            var existing = ProfileService.Mic.List().FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Profil überschreiben?",
                    Content = $"Ein Profil mit dem Namen '{targetName}' existiert bereits. Möchten Sie es wirklich überschreiben?",
                    PrimaryButtonText = "Überschreiben",
                    CloseButtonText = "Abbrechen",
                    DefaultButton = ContentDialogButton.Primary
                };

                var res = await dialog.ShowAsync();
                if (res != ContentDialogResult.Primary) return;
            }
        }

        try
        {
            ProfileService.Mic.Save(targetName, Current());
            VstPluginService.Instance.SaveRackForProfile(targetName);
            MicProfileName.Text = "";
            RefreshMicProfiles(targetName);
            Show(InfoBarSeverity.Success, Loc.Format("MicProfileSaved", targetName));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private void MicLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) { Show(InfoBarSeverity.Informational, Loc.Get("MicProfilePick")); return; }
        try
        {
            var (name, profile, rawDoc) = ProfileService.Mic.ReadFull(entry.Path);
            if (Apply(profile))
            {
                VstPluginService.Instance.LoadRackForProfile(entry.Name, rawDoc);
                Show(InfoBarSeverity.Success, Loc.Format("MicProfileLoaded", name));
            }
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
            var (name, profile, rawDoc) = ProfileService.Mic.ReadFull(file.Path);
            ProfileService.Mic.Save(name, profile);
            if (rawDoc != null)
            {
                VstPluginService.Instance.LoadRackForProfile(name, rawDoc);
            }
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
        VstPluginService.Instance.DeleteProfileRack(entry.Name);
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

    public Brush RnnoiseBadgeBackground(bool enabled) =>
        enabled ? new SolidColorBrush(Color.FromArgb(50, 0x1F, 0xC7, 0x9A)) : (Brush)Application.Current.Resources["SurfaceL3Brush"];

    public Brush RnnoiseBadgeBorder(bool enabled) =>
        enabled ? (Brush)Application.Current.Resources["BrandBrush"] : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

    public Brush RnnoiseBadgeForeground(bool enabled) =>
        enabled ? (Brush)Application.Current.Resources["BrandBrush"] : (Brush)Application.Current.Resources["TextMutedBrush"];

    public InfoBarSeverity RnnoiseInfoSeverity(bool enabled) =>
        enabled ? InfoBarSeverity.Success : InfoBarSeverity.Informational;

    private void RnnoiseModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string model })
        {
            Mic.RnnoiseModel = model;
            Show(InfoBarSeverity.Informational, $"Neuronales KI-Modell gewechselt: {model}");
        }
    }

    private void OpenVstEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VstPluginItem plugin })
        {
            VstPluginService.OpenEditor(App.MainWindowInstance ?? new Window(), plugin);
        }
    }

    private void PluginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            var plugin = (sender as FrameworkElement)?.Tag as VstPluginItem ?? (sender as FrameworkElement)?.DataContext as VstPluginItem;
            if (plugin != null)
            {
                VstPluginService.SyncToHardware(plugin);
                VstPluginService.Instance.SaveRack();
            }
        }
        catch { }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var cb = sender as ComboBox;
            var plugin = cb?.Tag as VstPluginItem ?? cb?.DataContext as VstPluginItem;
            if (plugin != null && cb?.SelectedItem is string newPreset)
            {
                if (!string.IsNullOrWhiteSpace(newPreset) && newPreset != plugin.PresetName)
                {
                    plugin.PresetName = newPreset;
                    plugin.ApplyPreset(newPreset);
                    VstPluginService.SyncToHardware(plugin);
                    VstPluginService.Instance.SaveRack();
                }
            }
        }
        catch { }
    }

    private void ResetToIntegrated_Click(object sender, RoutedEventArgs e)
    {
        VstPluginService.Instance.ResetToIntegratedPlugins();
        Show(InfoBarSeverity.Success, "Standard Studio-Effektkette (6 integrierte DSPs) geladen und live auf GoXLR angewendet.");
    }

    private void SyncAllDSPs_Click(object sender, RoutedEventArgs e)
    {
        VstPluginService.SyncAllToHardware();
        Show(InfoBarSeverity.Success, "Alle Studio-DSPs erfolgreich mit dem GoXLR Hardware-DSP synchronisiert.");
    }

    private void AddSpecificPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            var plugin = VstPluginService.CreateIntegratedPlugin(tag);
            VstPluginService.Instance.AddPlugin(plugin);
            VstPluginService.SyncToHardware(plugin);
            Show(InfoBarSeverity.Success, $"{plugin.Name} zum Rack hinzugefügt und aktiviert.");
        }
    }

    private void AddIntegratedPlugins_Click(object sender, RoutedEventArgs e)
    {
        int added = VstPluginService.Instance.AddIntegratedPlugins();
        if (added > 0)
        {
            Show(InfoBarSeverity.Success, $"{added} integrierte NEXT : XLR Studio-DSPs zum Effekt-Rack hinzugefügt!");
        }
        else
        {
            Show(InfoBarSeverity.Informational, "Alle 6 integrierten Studio-DSPs befinden sich bereits im Effekt-Rack.");
        }
    }

    private void MoveVstUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VstPluginItem plugin })
        {
            VstPluginService.Instance.MovePluginUp(plugin);
        }
    }

    private void MoveVstDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VstPluginItem plugin })
        {
            VstPluginService.Instance.MovePluginDown(plugin);
        }
    }

    private void RemoveVst_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VstPluginItem plugin })
        {
            VstPluginService.Instance.RemovePlugin(plugin);
            Show(InfoBarSeverity.Informational, $"Plugin '{plugin.Name}' aus Rack entfernt.");
        }
    }

    private async void ScanVst_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SetBusy(true, "Scanne System nach VST3- & CLAP-Plugins...");
        Show(InfoBarSeverity.Informational, "Scanne System nach VST3- & CLAP-Audio-Plugins...");
        List<VstPluginItem> plugins;
        try
        {
            plugins = await Task.Run(() => VstPluginService.ScanInstalledPlugins());
        }
        finally
        {
            ViewModel.SetBusy(false);
        }
        var externalPlugins = plugins.Where(p => !p.Path.StartsWith("Builtin::")).ToList();

        if (externalPlugins.Count == 0)
        {
            Show(InfoBarSeverity.Informational, "Scan abgeschlossen: Keine externen VST3/CLAP-Dateien in den Standard-Verzeichnissen gefunden. Die 6 integrierten Studio-DSPs sind aktiv.");
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Gefundene VST3 / CLAP Plugins ({externalPlugins.Count})",
                PrimaryButtonText = "Ausgewähltes Plugin hinzufügen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Primary
            };

            var combo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = externalPlugins.Select(p => $"{p.Name} ({p.Format} • {p.Vendor})").ToList(),
                SelectedIndex = 0
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock { Text = "Wählen Sie ein auf dem System gefundenes Plugin aus, um es zum Mikrofon-Rack hinzuzufügen:", FontSize = 13, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(combo);
            dialog.Content = stack;

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary && combo.SelectedIndex >= 0)
            {
                var selected = externalPlugins[combo.SelectedIndex];
                if (!VstPluginService.Instance.RackPlugins.Any(r => r.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    VstPluginService.Instance.AddPlugin(selected);
                    VstPluginService.SyncToHardware(selected);
                    Show(InfoBarSeverity.Success, $"Plugin '{selected.Name}' ({selected.Format}) zum Rack hinzugefügt und aktiviert.");
                }
                else
                {
                    Show(InfoBarSeverity.Informational, $"Plugin '{selected.Name}' befindet sich bereits im Rack.");
                }
            }
        }
        catch
        {
            Show(InfoBarSeverity.Informational, $"Scan abgeschlossen: {externalPlugins.Count} externe Plugins gefunden.");
        }
    }

    private async void AddVst_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".vst3");
            picker.FileTypeFilter.Add(".clap");
            picker.FileTypeFilter.Add(".dll");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            string format = file.FileType.ToLowerInvariant() == ".clap" ? "CLAP" : "VST3";
            var item = new VstPluginItem
            {
                Name = file.DisplayName,
                Vendor = "Externes Plugin",
                Format = format,
                Path = file.Path,
                TagCode = format == "CLAP" ? "[CLAP]" : "[VST3]",
                TagColorHex = "#8B93A3",
                IsEnabled = true
            };
            VstPluginService.Instance.AddPlugin(item);
            VstPluginService.SyncToHardware(item);
            Show(InfoBarSeverity.Success, $"Plugin hinzugefügt: {item.Name} ({format})");
            VstPluginService.OpenEditor(App.MainWindowInstance ?? new Window(), item);
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, $"Fehler beim Laden des Plugins: {ex.Message}");
        }
    }
}

public partial class EffectsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public EffectsViewModel Fx => ViewModel.Effects;
    public EffectsPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel = (MainViewModel)e.Parameter;

    private Visibility BoolToVisibility(bool active) => active ? Visibility.Visible : Visibility.Collapsed;
}

public partial class SamplerPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public SamplerViewModel Sampler => ViewModel.Sampler;
    public SamplerPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }
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

    public bool NotProcessing(bool b) => !b;

    private void ResetTrim_Click(object sender, RoutedEventArgs e) => Sampler.SelectedPad?.ResetTrim();

    private async void NormalizePad_Click(object sender, RoutedEventArgs e)
    {
        if (Sampler.SelectedPad is { } pad)
        {
            await pad.NormalizeAsync();
        }
    }

    private void AutoTrimPad_Click(object sender, RoutedEventArgs e)
    {
        Sampler.SelectedPad?.AutoTrim();
    }

    /// <summary>Plays the trimmed region on the pad's own voice (toggle).</summary>
    private void PreviewPad_Click(object sender, RoutedEventArgs e)
    {
        if (Sampler.SelectedPad is not { } pad) return;
        var voice = Sampler.Voice(Sampler.ActivePage, pad.Index);
        if (voice.IsPlaying) { voice.Stop(); return; }
        if (Sampler.Store.Find(pad.Config.SampleId) is { } s) voice.Play(s.File, pad.Config.Start, pad.Config.End, pad.Config.GainDb);
    }
}

public partial class VirtualChannelsPage : Page
{
    public VirtualChannelsService Service => VirtualChannelsService.Instance;
    public string VirtualChannelsCountText => $"{Service.Channels.Count} virtuelle Kabel aktiv";

    public VirtualChannelsPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    private void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        string name = ChannelNameBox?.Text?.Trim() ?? "";
        string target = (TargetChannelCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Music";
        int buffer = 128;
        if (BufferSizeCombo?.SelectedIndex == 0) buffer = 64;
        else if (BufferSizeCombo?.SelectedIndex == 2) buffer = 256;
        else if (BufferSizeCombo?.SelectedIndex == 3) buffer = 512;

        Service.CreateChannel(name, target, 48000, buffer);
        if (ChannelNameBox != null) ChannelNameBox.Text = "";
    }

    private void DeleteChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VirtualChannelItem item })
        {
            Service.RemoveChannel(item);
        }
    }

    private void ReSync_Click(object sender, RoutedEventArgs e)
    {
        Service.Load();
    }
}

public partial class LightingPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    public LightingPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        DevicePreview.ForceMini = ViewModel.Hardware.IsMini;
        UpdateModelBadge();
        ViewModel.Hardware.PropertyChanged += Hardware_PropertyChanged;
        ViewModel.Lighting.RefreshCustomThemes();
        UpdateCustomThemesVisibility();
    }

    private void UpdateCustomThemesVisibility()
    {
        if (CustomThemesSection != null && ViewModel?.Lighting?.CustomThemes != null)
        {
            CustomThemesSection.Visibility = ViewModel.Lighting.CustomThemes.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.Hardware.PropertyChanged -= Hardware_PropertyChanged;
        }
    }

    private void Hardware_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HardwareState.IsMini) || e.PropertyName == nameof(HardwareState.HasDevice))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DevicePreview.ForceMini = ViewModel.Hardware.IsMini;
                UpdateModelBadge();
            });
        }
    }

    private void UpdateModelBadge()
    {
        if (ModelBadgeText != null && ViewModel != null)
        {
            ModelBadgeText.Text = ViewModel.Hardware.HasDevice
                ? (ViewModel.Hardware.IsMini ? "Erkanntes Gerät: GoXLR Mini (Beleuchtung angepasst)" : "Erkanntes Gerät: GoXLR (Vollversion)")
                : "Gerät: GoXLR (Standard-Vorschau)";
        }
    }

    public string LayoutCaption(bool hasDevice, bool isMini) =>
        Loc.Get(!hasDevice ? "LightLayoutNone" : isMini ? "LightLayoutMini" : "LightLayoutFull");

    public Visibility VisibleIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VisibleIfHasCustom(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ShowThemeResult(InfoBarSeverity severity, string message)
    {
        ThemeResultInfo.Severity = severity;
        ThemeResultInfo.Message = message;
        ThemeResultInfo.IsOpen = true;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.Tag is LightingTheme t)
            {
                ViewModel.Lighting.ApplyTheme(t);
                ShowThemeResult(InfoBarSeverity.Informational, $"Design '{t.Name}' wurde angewendet.");
            }
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Fehler beim Anwenden des Designs: {ex.Message}");
        }
    }

    private void CustomTheme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if ((sender as FrameworkElement)?.Tag is CustomLightingTheme t)
            {
                ViewModel.Lighting.ApplyCustomTheme(t);
                ShowThemeResult(InfoBarSeverity.Success, $"Eigenes Theme '{t.Name}' wurde auf das GoXLR angewendet.");
            }
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Fehler beim Anwenden des Designs: {ex.Message}");
        }
    }

    private void SaveThemeConfirm_Click(object sender, RoutedEventArgs e)
    {
        var name = ThemeNameInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowThemeResult(InfoBarSeverity.Warning, "Bitte gib einen Namen für dein Theme ein.");
            return;
        }

        if (CustomThemeService.IsReservedName(name))
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Der Name '{name}' ist für ein integriertes Standard-Design reserviert. Bitte wähle einen anderen Namen.");
            return;
        }

        try
        {
            var theme = ViewModel.Lighting.SaveCurrentAsTheme(name);
            SaveThemeFlyout.Hide();
            ThemeNameInput.Text = "";
            UpdateCustomThemesVisibility();
            ShowThemeResult(InfoBarSeverity.Success, $"Theme '{theme.Name}' wurde erfolgreich gespeichert!");
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Fehler beim Speichern des Themes: {ex.Message}");
        }
    }

    private void ThemeNameInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SaveThemeConfirm_Click(sender, e);
        }
    }

    private async void ExportCustomTheme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CustomLightingTheme t) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"{t.Name}.nextxlr-theme" };
            picker.FileTypeChoices.Add("NEXT : XLR Theme", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            CustomThemeService.Export(t, file.Path);
            ShowThemeResult(InfoBarSeverity.Success, $"Theme '{t.Name}' wurde erfolgreich nach '{file.Name}' exportiert!");
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Export fehlgeschlagen: {ex.Message}");
        }
    }

    private void DeleteCustomTheme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CustomLightingTheme t) return;

        try
        {
            ViewModel.Lighting.DeleteCustomTheme(t);
            UpdateCustomThemesVisibility();
            ShowThemeResult(InfoBarSeverity.Informational, $"Theme '{t.Name}' wurde gelöscht.");
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Löschen fehlgeschlagen: {ex.Message}");
        }
    }

    private async void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var imported = CustomThemeService.Import(file.Path);
            ViewModel.Lighting.RefreshCustomThemes();
            ViewModel.Lighting.ApplyCustomTheme(imported);
            UpdateCustomThemesVisibility();
            ShowThemeResult(InfoBarSeverity.Success, $"Theme '{imported.Name}' wurde erfolgreich importiert und angewendet!");
        }
        catch (Exception ex)
        {
            ShowThemeResult(InfoBarSeverity.Error, $"Import fehlgeschlagen: {ex.Message}");
        }
    }

    private void GtaVi_Click(object sender, RoutedEventArgs e) => ViewModel.Lighting.ApplyTheme(ViewModel.Lighting.GtaVi);
}
