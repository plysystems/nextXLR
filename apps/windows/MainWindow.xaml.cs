using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NextXLR.Services;
using NextXLR.ViewModels;
using NextXLR.Views;
using Windows.Graphics;

namespace NextXLR;

public sealed partial class MainWindow : Window
{
    private readonly PipeTransport _transport = new();
    private static readonly SolidColorBrush Online = new(ColorHelper.FromArgb(255, 0x22, 0xD3, 0xA6));
    private static readonly SolidColorBrush Offline = new(ColorHelper.FromArgb(255, 0xEF, 0x44, 0x58));

    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["mixer"] = typeof(MixerPage),
        ["mic"] = typeof(MicPage),
        ["routing"] = typeof(RoutingPage),
        ["effects"] = typeof(EffectsPage),
        ["sampler"] = typeof(SamplerPage),
        ["lighting"] = typeof(LightingPage),
        ["virtual"] = typeof(VirtualChannelsPage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainViewModel ViewModel { get; }

    /// <summary>Global reference for single-instance wake-up</summary>
    public static MainWindow? CurrentInstance { get; private set; }

    /// <summary>HWND for pickers in an unpackaged app (FileOpenPicker needs an owner window).</summary>
    public static nint Handle { get; private set; }

    private TrayIconService? _trayService;
    private bool _isExplicitExit = false;

    public MainWindow()
    {
        CurrentInstance = this;
        ViewModel = new MainViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), _transport);
        InitializeComponent();

        Title = Loc.Get("AppTitle");
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
            }
        }
        catch { }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1440, 920));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico"));

        ViewModel.Hardware.PropertyChanged += OnHardwareChanged;
        Nav.SelectedItem = MixerItem;

        // Tray Icon Service
        _trayService = new TrayIconService(
            Handle,
            onRestore: RestoreWindow,
            onNavigate: NavigateToPage,
            onExit: ExitApplication
        );

        // Minimize / Close to Tray
        AppWindow.Closing += async (sender, args) =>
        {
            var settings = AppSettings.Load();
            if (settings.MinimizeToTray && !_isExplicitExit)
            {
                args.Cancel = true;
                ViewModel.SetMetersActive(false);
                AppWindow.Hide();
                return;
            }

            if (!_isExplicitExit)
            {
                args.Cancel = true;
                var canExit = await ConfirmExitWithUnsavedChangesAsync();
                if (canExit)
                {
                    ExitApplicationDirect();
                }
            }
        };

        AppWindow.Changed += (sender, args) =>
        {
            if (args.DidPresenterChange && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                var settings = AppSettings.Load();
                if (settings.MinimizeToTray && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                {
                    ViewModel.SetMetersActive(false);
                    AppWindow.Hide();
                }
            }
        };

        RootWindowGrid.Loaded += (_, _) =>
        {
            try
            {
                if (!ViewModel.IsConnected)
                {
                    ViewModel.SetBusy(true, "Verbinde mit GoXLR Audio-Engine...");
                }

                var settings = AppSettings.Load();
                if (!settings.HasCompletedTour)
                {
                    StartTour();
                }

                // Check if GoXLR driver is installed so Windows audio endpoints work
                if (!DriverInstaller.IsDriverInstalled())
                {
                    DriverMissingBar.IsOpen = true;
                }

                // Autostart Bluetooth / Wireless Headset routing if enabled
                if (settings.EnableBluetoothHeadset && !string.IsNullOrWhiteSpace(settings.BluetoothHeadsetDeviceId))
                {
                    BluetoothAudioRouter.Instance.Start(
                        settings.BluetoothHeadsetDeviceId,
                        settings.BluetoothHeadsetDeviceName ?? "",
                        settings.BluetoothMuteMicSidetone
                    );
                }
            }
            catch { }
        };

        Closed += async (_, _) =>
        {
            BluetoothAudioRouter.Instance.Dispose();
            _trayService?.Dispose();
            ViewModel.Dispose();
            await _transport.DisposeAsync();
        };
        _transport.Start();

        // Started from "Run on startup": begin minimised.
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            var settings = AppSettings.Load();
            if (settings.MinimizeToTray)
            {
                AppWindow.Hide();
            }
            else if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }
    }

    public void RestoreWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ShowWindow(Handle, 9); // SW_RESTORE
                AppWindow.Show();
                if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                    {
                        presenter.Restore();
                    }
                }
                AppWindow.MoveInZOrderAtTop();
                SetForegroundWindow(Handle);
                ViewModel.SetMetersActive(ContentFrame.CurrentSourcePageType == typeof(MixerPage));
            }
            catch { }
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    public void NavigateToPage(string tag)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Pages.TryGetValue(tag, out var page))
            {
                ContentFrame.Navigate(page, ViewModel);
                if (tag == "mixer") Nav.SelectedItem = MixerItem;
                else if (tag == "sampler") Nav.SelectedItem = SamplerItem;
                else if (tag == "settings") Nav.SelectedItem = Nav.SettingsItem;
            }
        });
    }

    private void App_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "In Sampler importieren";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;

            // Automatisch zu Sampler wechseln
            if (!ReferenceEquals(Nav.SelectedItem, SamplerItem))
            {
                NavigateToPage("sampler");
            }
        }
    }

    private async void App_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var audioPaths = items.Select(i => i.Path)
                .Where(p => Services.SamplerStore.Extensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (audioPaths.Count > 0)
            {
                NavigateToPage("sampler");
                await ViewModel.Sampler.ImportAsync(audioPaths);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

    private const uint MB_YESNOCANCEL = 0x00000003;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_TOPMOST = 0x00040000;
    private const uint MB_SETFOREGROUND = 0x00010000;

    private const int IDYES = 6;
    private const int IDNO = 7;
    private const int IDCANCEL = 2;

    public async Task<bool> ConfirmExitWithUnsavedChangesAsync()
    {
        if (!MainViewModel.HasPendingChanges)
        {
            return true;
        }

        try { AppWindow.Show(); } catch { }

        if (Content?.XamlRoot != null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "NEXT : XLR — Ungespeicherte Änderungen",
                PrimaryButtonText = "Speichern & Beenden",
                SecondaryButtonText = "Nicht speichern",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Primary
            };

            var stack = new StackPanel { Spacing = 12 };
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 8) };
            headerRow.Children.Add(new FontIcon { Glyph = "\uE7BA", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandBrush"], FontSize = 22 });
            headerRow.Children.Add(new TextBlock
            {
                Text = "Profil- & Mikrofoneinstellungen sichern",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(headerRow);

            stack.Children.Add(new TextBlock
            {
                Text = "Sie haben nicht gespeicherte Änderungen an Ihren Mixer-Profilen oder Mikrofoneinstellungen vorgenommen.\n\nMöchten Sie Ihre Änderungen vor dem Beenden von NEXT : XLR speichern?",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });

            dialog.Content = stack;

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.None)
            {
                // Abbrechen
                return false;
            }

            if (res == ContentDialogResult.Primary)
            {
                // Speichern
                try
                {
                    var prof = await ViewModel.GetProfileAsync();
                    var setupName = ProfileService.Setups.DefaultName ?? "Standard";
                    ProfileService.Setups.Save(setupName, prof);

                    var micName = ProfileService.Mic.DefaultName ?? "Standard";
                    var micNode = System.Text.Json.JsonSerializer.SerializeToNode(ViewModel.Mic.Build(), Protocol.Json.Options);
                    if (micNode != null)
                    {
                        ProfileService.Mic.Save(micName, micNode);
                    }

                    MainViewModel.HasPendingChanges = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to save changes on exit: {ex}");
                }
            }
            else if (res == ContentDialogResult.Secondary)
            {
                MainViewModel.HasPendingChanges = false;
            }

            return true;
        }

        int result = MessageBoxW(
            Handle,
            "Sie haben nicht gespeicherte Änderungen an Profil- oder Mikrofoneinstellungen vorgenommen.\n\nMöchten Sie Ihre Änderungen vor dem Beenden speichern?",
            "NEXT : XLR — Ungespeicherte Änderungen",
            MB_YESNOCANCEL | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND);

        if (result == IDCANCEL)
        {
            return false;
        }

        if (result == IDYES)
        {
            try
            {
                var prof = await ViewModel.GetProfileAsync();
                var setupName = ProfileService.Setups.DefaultName ?? "Standard";
                ProfileService.Setups.Save(setupName, prof);

                var micName = ProfileService.Mic.DefaultName ?? "Standard";
                var micNode = System.Text.Json.JsonSerializer.SerializeToNode(ViewModel.Mic.Build(), Protocol.Json.Options);
                if (micNode != null)
                {
                    ProfileService.Mic.Save(micName, micNode);
                }

                MainViewModel.HasPendingChanges = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save changes on exit: {ex}");
            }
        }
        else if (result == IDNO)
        {
            MainViewModel.HasPendingChanges = false;
        }

        return true;
    }

    public async void ExitApplication()
    {
        var canExit = await ConfirmExitWithUnsavedChangesAsync();
        if (!canExit) return;

        ExitApplicationDirect();
    }

    public void ExitApplicationDirect()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isExplicitExit = true;
            _trayService?.Dispose();
            Close();
        });
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type targetPage = typeof(MixerPage);
        if (args.IsSettingsSelected || (args.SelectedItem is NavigationViewItem { Tag: "settings" }))
        {
            targetPage = typeof(SettingsPage);
        }
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            targetPage = page;
        }

        if (ContentFrame.CurrentSourcePageType != targetPage)
        {
            // Silky smooth, modern Windows 11 Fluent Slide transition
            var transition = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromBottom };
            ContentFrame.Navigate(targetPage, ViewModel, transition);
        }

        // Performance: Only run MeterAnimator when MixerPage is on screen
        ViewModel.SetMetersActive(targetPage == typeof(MixerPage));
    }

    /// <summary>If the device changes (e.g. a Mini is plugged in) while a now-hidden tab is open, go to Mixer.</summary>
    private void OnHardwareChanged(object? sender, PropertyChangedEventArgs e)
    {
        var hw = ViewModel.Hardware;
        var current = ContentFrame.CurrentSourcePageType;
        if ((current == typeof(EffectsPage) && !hw.HasEffects) || (current == typeof(SamplerPage) && !hw.HasSampler))
            Nav.SelectedItem = MixerItem;
    }

    // x:Bind helper functions
    public SolidColorBrush StatusBrush(bool connected) => connected ? Online : Offline;
    public bool HasText(string? s) => !string.IsNullOrEmpty(s);
    public bool Not(bool b) => !b;
    public Visibility VisibleIfNot(bool b) => b ? Visibility.Collapsed : Visibility.Visible;

    // ---- Guided Tour ----
    private int _tourStep;

    public void StartTour()
    {
        _tourStep = 0;
        TourOverlay.Visibility = Visibility.Visible;
        UpdateTourStep();
    }

    private void SelectNav(NavigationViewItem item)
    {
        if (!ReferenceEquals(Nav.SelectedItem, item)) Nav.SelectedItem = item;
    }

    private void UpdateTourStep()
    {
        TourBackButton.Visibility = _tourStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        TourProgressBar.Value = _tourStep + 1;

        switch (_tourStep)
        {
            case 0:
                TourIcon.Glyph = "\uE735";
                TourTitle.Text = "Willkommen bei NextXLR!";
                TourStepLabel.Text = "Übersicht";
                TourDescription.Text = "Die moderne, blitzschnelle Open-Source Steuerzentrale für dein GoXLR und GoXLR Mini.\n\n• Latenzfreier Hardware-DSP und nahtloses Audio-Routing\n• Schneller Profil-Import aus der bisherigen GoXLR-App\n• Modernes Fluent UI mit voller Kontrolle";
                TourNextButton.Content = "Tour starten";
                SelectNav(MixerItem);
                break;

            case 1:
                TourIcon.Glyph = "\uE992";
                TourTitle.Text = "1. Mixer & Lautstärken";
                TourStepLabel.Text = "Schritt 2 von 7";
                TourDescription.Text = "• Direkteingabe: Klicke auf die Zahl unter jedem Fader, um die Lautstärke direkt als Zahl einzugeben.\n• Mic-Monitor: Auf 50% bis 100% optimiert – für kristallklares Kopfhörer-Monitoring völlig ohne Hall.\n• Bleep-Regler: Passe die Lautstärke des Zensur-Tons (!@#$?*) stufenlos zwischen 0% und 100% an.";
                TourNextButton.Content = "Weiter";
                SelectNav(MixerItem);
                break;

            case 2:
                TourIcon.Glyph = "\uE720";
                TourTitle.Text = "2. Mikrofon & Profil-Import";
                TourStepLabel.Text = "Schritt 3 von 7";
                TourDescription.Text = "• Studio-DSP: Vollwertiges Noise Gate, Equalizer, Kompressor und De-Esser.\n• Profil-Import: Du hast alte Profile aus der bisherigen GoXLR-App? Klicke einfach auf 'Importieren', um deine gewohnten Profile (.goxlr, .goxlrMicProfile, .xml) direkt zu laden!";
                TourNextButton.Content = "Weiter";
                SelectNav(MicItem);
                break;

            case 3:
                TourIcon.Glyph = "\uF168";
                TourTitle.Text = "3. Flexibles Audio-Routing";
                TourStepLabel.Text = "Schritt 4 von 7";
                TourDescription.Text = "• Matrix-Routing: Weise jeden Audio-Eingang frei den gewünschten Ausgängen zu.\n• Bleep-Routing: Der Zensur-Ton wird automatisch auf Chat Mic, Broadcast und Kopfhörer geroutet, damit er überall zuverlässig greift.";
                TourNextButton.Content = "Weiter";
                SelectNav(RoutingItem);
                break;

            case 4:
                TourIcon.Glyph = "\uE93C";
                TourTitle.Text = "4. Effekte & Sampler";
                TourStepLabel.Text = "Schritt 5 von 7";
                TourDescription.Text = "• Voice FX: Megaphone, Robot, Hardtune und Pitch-Shifter.\n• Sampler: 3 Sound-Bänke (A, B, C) mit je 4 Pads.\n• Neuer Pad-Editor: Hinterlegte Pads können jetzt jederzeit direkt mit dem 'Bearbeiten'-Button oder per Rechtsklick editiert, getrimmt oder ausgetauscht werden!";
                TourNextButton.Content = "Weiter";
                SelectNav(SamplerItem);
                break;

            case 5:
                TourIcon.Glyph = "\uE790";
                TourTitle.Text = "5. Beleuchtung & GTA VI Theme";
                TourStepLabel.Text = "Schritt 6 von 7";
                TourDescription.Text = "• Volle RGB-Kontrolle: Gestalte Fader und Tasten nach deinen Vorlieben.\n• GTA VI Theme: Hol dir das Vice-City-Feeling direkt auf dein GoXLR – mit stilvollem Neon-Pink, Miami-Blue und Sonnenuntergangs-Verläufen!";
                TourNextButton.Content = "Weiter";
                SelectNav(LightingItem);
                break;

            case 6:
                TourIcon.Glyph = "\uE73E";
                TourTitle.Text = "Alles bereit!";
                TourStepLabel.Text = "Schritt 7 von 7";
                TourDescription.Text = "Du bist startklar! NextXLR ist optimal für deinen Stream und deine tägliche Nutzung konfiguriert.\n\nDu kannst diese Tour oder den Profil-Import jederzeit über die Einstellungen erneut starten.\n\nViel Spaß mit NextXLR!";
                TourNextButton.Content = "Tour abschließen";
                SelectNav(MixerItem);
                break;
        }
    }

    private void TourNext_Click(object sender, RoutedEventArgs e)
    {
        if (_tourStep < 6)
        {
            _tourStep++;
            UpdateTourStep();
        }
        else
        {
            EndTour();
        }
    }

    private void TourBack_Click(object sender, RoutedEventArgs e)
    {
        if (_tourStep > 0)
        {
            _tourStep--;
            UpdateTourStep();
        }
    }

    private void TourSkip_Click(object sender, RoutedEventArgs e) => EndTour();

    private void EndTour()
    {
        TourOverlay.Visibility = Visibility.Collapsed;
        try
        {
            var s = AppSettings.Load();
            s.HasCompletedTour = true;
            s.Save();
        }
        catch { }
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        DriverMissingBar.Message = "Installiere Treiber... Bitte bestätigen Sie den Windows Administrator-Dialog.";
        var (success, msg) = await DriverInstaller.InstallDriverAsync();
        if (success)
        {
            DriverMissingBar.Severity = InfoBarSeverity.Success;
            DriverMissingBar.Title = "Treiber erfolgreich installiert!";
            DriverMissingBar.Message = "Die Windows-Audiokanäle (System, Chat, Musik, Game) stehen nun bereit.";
            DriverMissingBar.ActionButton = null;
        }
        else
        {
            DriverMissingBar.Severity = InfoBarSeverity.Error;
            DriverMissingBar.Title = "Installation fehlgeschlagen";
            DriverMissingBar.Message = msg;
        }
    }
}
