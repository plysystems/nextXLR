using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace NextXLR.Views;

/// <summary>Settings: General (autostart, tray), Updates (versions, update check), Help.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _loading = true;

    public MainViewModel ViewModel { get; private set; } = null!;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        AutostartToggle.IsOn = Autostart.IsEnabled;
        TrayToggle.IsOn = _settings.MinimizeToTray;
        MuteOnStartupToggle.IsOn = _settings.MuteOnStartup;
        AutoUpdateToggle.IsOn = _settings.CheckForUpdatesOnStart;
        AppVersionText.Text = Loc.Format("SetAppVersion", AppSettings.AppVersion);
        EngineVersionText.Text = Loc.Format("SetEngineVersion", ViewModel.EngineVersion);
        UpdateDriverStatus();

        BluetoothToggle.IsOn = _settings.EnableBluetoothHeadset;
        BluetoothSidetoneCheck.IsChecked = _settings.BluetoothMuteMicSidetone;
        BluetoothAudioRouter.Instance.StatusChanged -= OnBluetoothRouterStatusChanged;
        BluetoothAudioRouter.Instance.StatusChanged += OnBluetoothRouterStatusChanged;
        _ = LoadAudioEndpointsAsync();

        _loading = false;
    }

    private void OnBluetoothRouterStatusChanged(bool running, string msg)
    {
        DispatcherQueue.TryEnqueue(UpdateBluetoothStatus);
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPanel is null) return;
        var tag = (SectionList.SelectedItem as ListViewItem)?.Tag as string ?? "general";
        GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        AudioPanel.Visibility = tag == "audio" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "audio") UpdateDriverStatus();
        ProfilesPanel.Visibility = tag == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "profiles") RefreshProfiles();
        UpdatesPanel.Visibility = tag == "updates" ? Visibility.Visible : Visibility.Collapsed;
        HelpPanel.Visibility = tag == "help" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { Autostart.Set(AutostartToggle.IsOn); }
        catch { AutostartToggle.IsOn = Autostart.IsEnabled; }
    }

    private void Tray_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.MinimizeToTray = TrayToggle.IsOn;
        _settings.Save();
    }

    private void MuteOnStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.MuteOnStartup = MuteOnStartupToggle.IsOn;
        _settings.Save();
    }

    private void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CheckForUpdatesOnStart = AutoUpdateToggle.IsOn;
        _settings.Save();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateResult.Severity = InfoBarSeverity.Informational;
        UpdateResult.Title = "Suche nach Updates...";
        UpdateResult.Message = "Verbinde mit GitHub (https://github.com/plysystems/nextXLR)...";
        UpdateResult.ActionButton = null;
        UpdateResult.IsOpen = true;

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NextXLR-App/" + AppSettings.AppVersion);

            var url = "https://api.github.com/repos/plysystems/nextXLR/releases/latest";
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                var releaseUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "https://github.com/plysystems/nextXLR/releases" : "https://github.com/plysystems/nextXLR/releases";
                var releaseNotes = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";

                var cleanTag = tagName.TrimStart('v', 'V');
                var currentVer = AppSettings.AppVersion.TrimStart('v', 'V');

                if (Version.TryParse(cleanTag, out var remoteVer) && Version.TryParse(currentVer, out var localVer) && remoteVer > localVer)
                {
                    string? exeDownloadUrl = null;
                    if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElem.EnumerateArray())
                        {
                            var name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                exeDownloadUrl = asset.TryGetProperty("browser_download_url", out var bu) ? bu.GetString() : null;
                                break;
                            }
                        }
                    }

                    UpdateResult.Severity = InfoBarSeverity.Success;
                    UpdateResult.Title = $"Neues Update verfügbar: {tagName}";
                    UpdateResult.Message = $"{releaseNotes}\nInstalliert: v{AppSettings.AppVersion}. Klicke unten, um das Update herunterzuladen und direkt über die bestehende Version zu installieren.";

                    var btnAuto = new Button
                    {
                        Content = "Update automatisch installieren",
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                    };
                    btnAuto.Click += async (_, _) =>
                    {
                        if (!string.IsNullOrEmpty(exeDownloadUrl))
                        {
                            await DownloadAndInstallUpdateAsync(exeDownloadUrl, releaseUrl);
                        }
                        else
                        {
                            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(releaseUrl));
                        }
                    };
                    UpdateResult.ActionButton = btnAuto;
                }
                else
                {
                    UpdateResult.Severity = InfoBarSeverity.Success;
                    UpdateResult.Title = "NextXLR ist auf dem neuesten Stand!";
                    UpdateResult.Message = $"Version v{AppSettings.AppVersion} ist aktuell. Es liegen keine neueren Releases auf GitHub vor.";
                    UpdateResult.ActionButton = null;
                }
            }
            else
            {
                UpdateResult.Severity = InfoBarSeverity.Informational;
                UpdateResult.Title = "GitHub Repository geprüft";
                UpdateResult.Message = $"Aktuelle Version: v{AppSettings.AppVersion}. Besuche GitHub für die neuesten Releases, Commits und Downloads.";
                var btn = new Button { Content = "Releases auf GitHub öffnen" };
                btn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/releases"));
                UpdateResult.ActionButton = btn;
            }
        }
        catch (Exception ex)
        {
            UpdateResult.Severity = InfoBarSeverity.Informational;
            UpdateResult.Title = "GitHub Releases prüfen";
            UpdateResult.Message = $"Installierte Version: v{AppSettings.AppVersion}. Du kannst Releases direkt auf GitHub aufrufen ({ex.Message}).";
            var btn = new Button { Content = "GitHub öffnen" };
            btn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR"));
            UpdateResult.ActionButton = btn;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(string downloadUrl, string fallbackUrl)
    {
        UpdateResult.Severity = InfoBarSeverity.Informational;
        UpdateResult.Title = "Update wird heruntergeladen...";
        UpdateResult.Message = "Verbindung wird hergestellt...";
        UpdateResult.ActionButton = null;

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NextXLR-App/" + AppSettings.AppVersion);

            using var response = await client.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(fallbackUrl));
                return;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var tempExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NextXLR-Setup-Update.exe");

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new System.IO.FileStream(tempExe, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                var lastReport = DateTime.UtcNow;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 300)
                    {
                        lastReport = DateTime.UtcNow;
                        var mbRead = totalRead / (1024.0 * 1024.0);
                        if (totalBytes > 0)
                        {
                            var totalMb = totalBytes / (1024.0 * 1024.0);
                            var percent = (int)(totalRead * 100 / totalBytes);
                            UpdateResult.Message = $"Herunterladen: {percent}% ({mbRead:F1} MB von {totalMb:F1} MB)...";
                        }
                        else
                        {
                            UpdateResult.Message = $"Herunterladen: {mbRead:F1} MB...";
                        }
                    }
                }
            }

            UpdateResult.Severity = InfoBarSeverity.Success;
            UpdateResult.Title = "Download abgeschlossen!";
            UpdateResult.Message = "Starte Installationsprogramm und schließe NEXT : XLR...";

            await Task.Delay(800);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempExe,
                UseShellExecute = true,
            });

            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            UpdateResult.Severity = InfoBarSeverity.Error;
            UpdateResult.Title = "Fehler beim Herunterladen des Updates";
            UpdateResult.Message = $"{ex.Message}. Du kannst das Update alternativ manuell von GitHub herunterladen.";
            var btn = new Button { Content = "GitHub Release öffnen" };
            btn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(fallbackUrl));
            UpdateResult.ActionButton = btn;
        }
    }

    private void OpenGitHubRepo_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR"));
    }

    private void OpenGitHubReleases_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/releases"));
    }

    // ---------------- Profiles ----------------

    private void RefreshProfiles()
    {
        var items = ProfileService.Setups.List();
        ProfileList.ItemsSource = items;
        ProfileEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        ProfileResult.Severity = severity;
        ProfileResult.Message = message;
        ProfileResult.IsOpen = true;
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(ProfileNameBox.Text)
            ? Loc.Format("ProfileDefaultName", DateTime.Now.ToString("g"))
            : ProfileNameBox.Text.Trim();
        try
        {
            ProfileService.Setups.Save(name, await ViewModel.GetProfileAsync());
            ProfileNameBox.Text = "";
            RefreshProfiles();
            Show(InfoBarSeverity.Success, Loc.Format("ProfileSaved", name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OverwriteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileEntry entry) return;
        try
        {
            ProfileService.Setups.Save(entry.Name, await ViewModel.GetProfileAsync());
            RefreshProfiles();
            Show(InfoBarSeverity.Success, Loc.Format("ProfileOverwritten", entry.Name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileEntry entry) return;
        try
        {
            var (name, profile) = ProfileService.Setups.Read(entry.Path);
            await Apply(name, profile);
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async Task Apply(string name, System.Text.Json.Nodes.JsonNode profile)
    {
        var error = await ViewModel.LoadProfileAsync(profile);
        if (error is null) Show(InfoBarSeverity.Success, Loc.Format("ProfileLoaded", name));
        else Show(InfoBarSeverity.Error, Loc.Format("ProfileLoadFailed", error));
    }

    /// <summary>Star: this profile is applied to the GoXLR at every start (click again to unset).</summary>
    private void DefaultProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileEntry entry) return;
        try
        {
            if (entry.IsDefault)
            {
                ProfileService.Setups.ClearDefault();
                Show(InfoBarSeverity.Informational, Loc.Get("ProfileDefaultCleared"));
            }
            else
            {
                ProfileService.Setups.SetDefault(entry);
                Show(InfoBarSeverity.Success, Loc.Format("ProfileDefaultSet", entry.Name));
            }
            RefreshProfiles();
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileEntry entry) return;
        ProfileService.Setups.Delete(entry);
        RefreshProfiles();
        Show(InfoBarSeverity.Informational, Loc.Format("ProfileDeleted", entry.Name));
    }

    /// <summary>Import: read a .json, .goxlr, or .xml profile file, apply it, and keep a copy in the library.</summary>
    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".goxlr");
        picker.FileTypeFilter.Add(".xml");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var (name, profile) = ProfileService.Setups.Read(file.Path);
            var error = await ViewModel.LoadProfileAsync(profile);
            if (error is not null) { Show(InfoBarSeverity.Error, Loc.Format("ProfileLoadFailed", error)); return; }
            ProfileService.Setups.Save(name, profile);
            RefreshProfiles();
            Show(InfoBarSeverity.Success, Loc.Format("ProfileImported", name));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, Loc.Format("ProfileLoadFailed", ex.Message)); }
    }

    private async void ExportCurrent_Click(object sender, RoutedEventArgs e)
    {
        try { await Export(Loc.Format("ProfileDefaultName", DateTime.Now.ToString("yyyy-MM-dd")), await ViewModel.GetProfileAsync()); }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileEntry entry) return;
        try
        {
            var (name, profile) = ProfileService.Setups.Read(entry.Path);
            await Export(name, profile);
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, ex.Message); }
    }

    private async Task Export(string name, System.Text.Json.Nodes.JsonNode profile)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = name };
        picker.FileTypeChoices.Add("NEXT : XLR profile", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Handle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        ProfileService.Setups.Write(file.Path, name, profile);
        Show(InfoBarSeverity.Success, Loc.Format("ProfileExported", file.Name));
    }

    private void StartTour_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.StartTour();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void UpdateDriverStatus()
    {
        var installed = DriverInstaller.IsDriverInstalled();
        var (avail, total, details) = DriverInstaller.GetEndpointStatus();

        if (installed && avail >= 6)
        {
            DriverStatusText.Text = "TC-Helicon GoXLR USB-Audiotreiber ist eingerichtet.";
            DriverBadgeText.Text = "Installiert & Aktiv";
            DriverStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1B, 0x47, 0x36));
            DriverBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xB8));
        }
        else if (installed)
        {
            DriverStatusText.Text = "Treiber installiert, aber nicht alle Audiokanäle aktiv.";
            DriverBadgeText.Text = "Teilweise aktiv";
            DriverStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4D, 0x38, 0x14));
            DriverBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF5, 0xB9, 0x42));
        }
        else
        {
            DriverStatusText.Text = "Treiber nicht installiert. Audiokanäle (System, Chat, Game, Musik) fehlen in Windows.";
            DriverBadgeText.Text = "Nicht installiert";
            DriverStatusBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4D, 0x1B, 0x24));
            DriverBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF0, 0x50, 0x6A));
        }

        DriverEndpointsText.Text = details;
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        InstallDriverButton.IsEnabled = false;
        DriverResultBar.Severity = InfoBarSeverity.Informational;
        DriverResultBar.Title = "Installiere GoXLR Audiotreiber...";
        DriverResultBar.Message = "Bitte bestätigen Sie den Windows Administrator-Dialog (UAC)...";
        DriverResultBar.IsOpen = true;

        var (success, msg) = await DriverInstaller.InstallDriverAsync();

        DriverResultBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        DriverResultBar.Title = success ? "Treiber erfolgreich installiert" : "Treiberinstallation fehlgeschlagen";
        DriverResultBar.Message = msg;
        DriverResultBar.IsOpen = true;

        UpdateDriverStatus();
        InstallDriverButton.IsEnabled = true;
    }

    private void OpenSoundControl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "mmsys.cpl",
                UseShellExecute = true
            });
        }
        catch
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));
        }
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var licensePath = Path.Combine(baseDir, "LICENSE");
            if (!File.Exists(licensePath))
            {
                licensePath = Path.Combine(baseDir, "..", "..", "..", "..", "..", "LICENSE");
            }
            if (File.Exists(licensePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{Path.GetFullPath(licensePath)}\"",
                    UseShellExecute = true
                });
                return;
            }
        }
        catch
        {
        }
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/blob/main/LICENSE"));
    }

    #region Bluetooth & Wireless Headset Routing

    private async Task LoadAudioEndpointsAsync()
    {
        try
        {
            var items = await Task.Run(() =>
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);

                var list = new List<AudioEndpointItem>();
                foreach (var dev in devices)
                {
                    var name = dev.FriendlyName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var lowerName = name.ToLowerInvariant();
                    var lowerId = dev.ID.ToLowerInvariant();

                    // TC-Helicon GoXLR-Kanäle nicht als Ziel-Headsets auflisten
                    if (lowerName.Contains("tc-helicon") || (lowerName.Contains("goxlr") && !lowerName.Contains("bluetooth")))
                    {
                        continue;
                    }

                    var isBt = lowerId.Contains("bth") || lowerId.Contains("bluetooth")
                               || lowerName.Contains("bluetooth") || lowerName.Contains("wireless")
                               || lowerName.Contains("buds") || lowerName.Contains("airpods")
                               || lowerName.Contains("headset") || lowerName.Contains("hands-free")
                               || lowerName.Contains("wh-") || lowerName.Contains("wf-")
                               || lowerName.Contains("bose") || lowerName.Contains("jabra")
                               || lowerName.Contains("sennheiser") || lowerName.Contains("soundcore")
                               || lowerName.Contains("funk") || lowerName.Contains("kopfhörer");

                    list.Add(new AudioEndpointItem
                    {
                        Id = dev.ID,
                        Name = name,
                        IsBluetooth = isBt
                    });
                }
                return list;
            });

            // Bluetooth/Wireless Geräte nach oben sortieren
            var ordered = items.OrderByDescending(x => x.IsBluetooth).ThenBy(x => x.Name).ToList();
            BluetoothDeviceBox.ItemsSource = ordered;

            if (ordered.Count > 0)
            {
                var match = ordered.FirstOrDefault(x =>
                    (!string.IsNullOrEmpty(_settings.BluetoothHeadsetDeviceId) && x.Id.Equals(_settings.BluetoothHeadsetDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(_settings.BluetoothHeadsetDeviceName) && x.Name.Equals(_settings.BluetoothHeadsetDeviceName, StringComparison.OrdinalIgnoreCase)));

                if (match != null)
                {
                    BluetoothDeviceBox.SelectedItem = match;
                }
                else if (_settings.EnableBluetoothHeadset)
                {
                    // Fallback zum ersten erkannten Bluetooth-Gerät oder ersten Gerät
                    var firstBt = ordered.FirstOrDefault(x => x.IsBluetooth) ?? ordered.First();
                    BluetoothDeviceBox.SelectedItem = firstBt;
                }
            }

            UpdateBluetoothStatus();
        }
        catch (Exception ex)
        {
            BluetoothStatusInfo.Severity = InfoBarSeverity.Warning;
            BluetoothStatusInfo.Message = $"Audiogeräte konnten nicht vollständig enumeriert werden: {ex.Message}";
        }
    }

    private void BluetoothToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.EnableBluetoothHeadset = BluetoothToggle.IsOn;
        _settings.Save();

        if (BluetoothToggle.IsOn && BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone);
        }
        else
        {
            BluetoothAudioRouter.Instance.Stop();
        }

        UpdateBluetoothStatus();
    }

    private void BluetoothDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            _settings.BluetoothHeadsetDeviceId = selected.Id;
            _settings.BluetoothHeadsetDeviceName = selected.Name;
            _settings.Save();

            if (BluetoothToggle.IsOn)
            {
                BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone);
            }
        }
        UpdateBluetoothStatus();
    }

    private void BluetoothSidetone_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.BluetoothMuteMicSidetone = BluetoothSidetoneCheck.IsChecked == true;
        _settings.Save();

        if (BluetoothToggle.IsOn && BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone);
        }
    }

    private async void RefreshAudioDevices_Click(object sender, RoutedEventArgs e)
    {
        await LoadAudioEndpointsAsync();
    }

    private void UpdateBluetoothStatus()
    {
        if (!BluetoothToggle.IsOn)
        {
            BluetoothStatusInfo.Severity = InfoBarSeverity.Informational;
            BluetoothStatusInfo.Message = "Bluetooth-Routing inaktiv. Kopfhörer-Signal wird direkt über die GoXLR 3,5 mm Klinkenbuchse ausgegeben.";
            return;
        }

        if (BluetoothAudioRouter.Instance.IsRunning)
        {
            BluetoothStatusInfo.Severity = InfoBarSeverity.Success;
            BluetoothStatusInfo.Message = BluetoothAudioRouter.Instance.StatusMessage;
        }
        else if (BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            BluetoothStatusInfo.Severity = InfoBarSeverity.Warning;
            BluetoothStatusInfo.Message = string.IsNullOrWhiteSpace(BluetoothAudioRouter.Instance.StatusMessage)
                ? $"Verbinde mit '{selected.Name}'..."
                : BluetoothAudioRouter.Instance.StatusMessage;
        }
        else
        {
            BluetoothStatusInfo.Severity = InfoBarSeverity.Warning;
            BluetoothStatusInfo.Message = "Bitte wähle oben dein Bluetooth- oder Wireless-Headset aus der Liste.";
        }
    }

    #endregion
}

public sealed class AudioEndpointItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsBluetooth { get; set; }
    public string Glyph => IsBluetooth ? "\uE702" : "\uE7F6";
    public string DisplayName => IsBluetooth ? $"[Bluetooth / Funk] {Name}" : Name;
}
