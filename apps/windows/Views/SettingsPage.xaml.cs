using NextXLR.Services;
using NextXLR.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace NextXLR.Views;

/// <summary>Settings: General (autostart, tray), Updates (versions, update check), Help.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _loading = true;

    public MainViewModel ViewModel { get; private set; } = null!;
    public StreamDeckStatusService StreamDeckService => StreamDeckStatusService.Instance;
    public string StreamDeckAppStatusText => StreamDeckService.IsAppRunning ? "Software läuft im Hintergrund" : "Software ist aktuell nicht gestartet";

    private static readonly SolidColorBrush StreamDeckActiveBrush = new(ColorHelper.FromArgb(255, 16, 185, 129));
    private static readonly SolidColorBrush StreamDeckReadyBrush = new(ColorHelper.FromArgb(255, 0x4F, 0xE0, 0xB8));
    private static readonly SolidColorBrush StreamDeckOfflineBrush = new(ColorHelper.FromArgb(255, 120, 130, 145));

    public SolidColorBrush StreamDeckStatusBrush(bool isApiOnline, bool isAppRunning)
    {
        if (isApiOnline && isAppRunning) return StreamDeckActiveBrush;
        if (isApiOnline) return StreamDeckReadyBrush;
        return StreamDeckOfflineBrush;
    }

    private bool _isLoadingEndpoints = false;

    public SettingsPage()
    {
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = (MainViewModel)e.Parameter;
        AutostartToggle.IsOn = Autostart.IsEnabled;
        TrayToggle.IsOn = _settings.MinimizeToTray;
        MuteOnStartupToggle.IsOn = _settings.MuteOnStartup;
        AutoUpdateToggle.IsOn = _settings.CheckForUpdatesOnStart;
        AppVersionText.Text = Loc.Format("SetAppVersion", AppSettings.VersionDisplay);
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
        StreamDeckPanel.Visibility = tag == "streamdeck" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "streamdeck") _ = StreamDeckService.CheckStatusAsync();
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

    private void ShowGeneralInfo(InfoBarSeverity severity, string message)
    {
        GeneralResult.Severity = severity;
        GeneralResult.Message = message;
        GeneralResult.IsOpen = true;
    }

    private void CleanupRemnants_Click(object sender, RoutedEventArgs e)
    {
        int cleanedRegistry = 0;
        int cleanedFiles = 0;
        int cleanedServices = 0;

        try
        {
            // 1. Registry autostart cleanup in HKCU Run key
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    foreach (var valName in key.GetValueNames())
                    {
                        if (valName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                            valName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                            valName.Equals("mixforge", StringComparison.OrdinalIgnoreCase))
                        {
                            try { key.DeleteValue(valName); cleanedRegistry++; } catch { }
                        }
                    }
                }
            }
            catch { }

            // Registry autostart cleanup in HKLM Run key (if accessible)
            try
            {
                using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (hklmKey != null)
                {
                    foreach (var valName in hklmKey.GetValueNames())
                    {
                        if (valName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                            valName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                            valName.Equals("mixforge", StringComparison.OrdinalIgnoreCase))
                        {
                            try { hklmKey.DeleteValue(valName); cleanedRegistry++; } catch { }
                        }
                    }
                }
            }
            catch { }

            // 2. Terminate old TC-Helicon background processes if running
            foreach (var procName in new[] { "GoXLRApp", "GoXLRAudioCplApp" })
            {
                try
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }

            // 3. Remove orphaned temp and crash files
            try
            {
                var tempPath = System.IO.Path.GetTempPath();
                var patterns = new[] { "GoXLR*", "TC-Helicon*", "mixforge*.log", "NextXLR*.log" };
                foreach (var pattern in patterns)
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(tempPath, pattern))
                    {
                        try { System.IO.File.Delete(file); cleanedFiles++; } catch { }
                    }
                }
            }
            catch { }

            // 4. Remove orphaned legacy service if exists
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", "delete mixforge")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(1000);
                if (p?.ExitCode == 0) cleanedServices++;
            }
            catch { }

            ShowGeneralInfo(InfoBarSeverity.Success,
                $"Altlasten bereinigt: {cleanedRegistry} Autostart-Einträge und {cleanedFiles} temporäre Cache-Dateien{(cleanedServices > 0 ? $", {cleanedServices} Dienst" : "")} erfolgreich entfernt.");
        }
        catch (Exception ex)
        {
            ShowGeneralInfo(InfoBarSeverity.Error, $"Fehler beim Bereinigen der Altlasten: {ex.Message}");
        }
    }

    private async void QuitAppAndService_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is MainWindow mw)
        {
            var canExit = await mw.ConfirmExitWithUnsavedChangesAsync();
            if (!canExit) return;

            try { BluetoothAudioRouter.Instance.Stop(); } catch { }

            foreach (var procName in new[] { "mixforged", "mixforge" })
            {
                try
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }

            mw.ExitApplicationDirect();
        }
        else
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
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
        UpdateResult.Message = "Prüfe GitHub Releases auf neue Versionen...";
        UpdateResult.ActionButton = null;
        UpdateResult.IsOpen = true;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = true;

        try
        {
            var handler = new System.Net.Http.SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NextXLR-App/" + AppSettings.AppVersion);

            // 1. Fetch releases (including prereleases) from GitHub Releases API
            string? latestReleaseTag = null;
            string? latestReleaseName = null;
            string? latestReleaseBody = null;
            string? exeDownloadUrl = null;

            try
            {
                var releasesUrl = "https://api.github.com/repos/plysystems/nextXLR/releases";
                var releasesResp = await client.GetAsync(releasesUrl);
                if (releasesResp.IsSuccessStatusCode)
                {
                    var json = await releasesResp.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var newestRel = doc.RootElement[0];
                        latestReleaseTag = newestRel.TryGetProperty("tag_name", out var tElem) ? tElem.GetString() : null;
                        latestReleaseName = newestRel.TryGetProperty("name", out var nElem) ? nElem.GetString() : null;
                        latestReleaseBody = newestRel.TryGetProperty("body", out var bElem) ? bElem.GetString() : null;

                        if (newestRel.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var asset in assetsElem.EnumerateArray())
                            {
                                var aName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                                if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    exeDownloadUrl = asset.TryGetProperty("browser_download_url", out var bu) ? bu.GetString() : null;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            UpdateProgressBar.Visibility = Visibility.Collapsed;

            var currentVer = AppSettings.AppVersion.TrimStart('v', 'V');
            var localHash = AppSettings.BuildCommitHash;

            // Check official GitHub Releases
            if (!string.IsNullOrEmpty(latestReleaseTag))
            {
                if (IsNewerRelease(latestReleaseTag, currentVer))
                {
                    var targetTag = latestReleaseTag;
                    if (string.IsNullOrEmpty(exeDownloadUrl))
                    {
                        var cleanTarget = targetTag.TrimStart('v', 'V');
                        exeDownloadUrl = $"https://github.com/plysystems/nextXLR/releases/download/{targetTag}/NextXLR-Setup-{cleanTarget}.exe";
                    }

                    UpdateResult.Severity = InfoBarSeverity.Success;
                    UpdateResult.Title = $"Neues Update verfügbar: {latestReleaseTag}";
                    UpdateResult.Message = $"Version {latestReleaseTag} ({latestReleaseName ?? "Release"}) steht auf GitHub bereit.\n" +
                        $"• Installierte Version: v{AppSettings.AppVersion}\n\n" +
                        $"Klicke unten, um das Update im Hintergrund herunterzuladen und automatisch zu installieren.";

                    var btnAuto = new Button
                    {
                        Content = "Update jetzt installieren & neu starten",
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                    };
                    btnAuto.Click += async (_, _) =>
                    {
                        await DownloadAndInstallUpdateAsync(exeDownloadUrl, targetTag);
                    };
                    UpdateResult.ActionButton = btnAuto;
                    return;
                }
                else
                {
                    // Official release is up to date!
                    UpdateResult.Severity = InfoBarSeverity.Success;
                    UpdateResult.Title = "NextXLR ist auf dem neuesten Stand!";
                    UpdateResult.Message = $"Version v{AppSettings.AppVersion} (Build {localHash}) ist aktuell. Es liegen keine neueren Releases vor.";
                    UpdateResult.ActionButton = null;
                    return;
                }
            }
            else
            {
                UpdateResult.Severity = InfoBarSeverity.Success;
                UpdateResult.Title = "NextXLR ist auf dem neuesten Stand!";
                UpdateResult.Message = $"Version v{AppSettings.AppVersion} (Build {localHash}) ist aktuell. Es liegen keine neueren Releases vor.";
                UpdateResult.ActionButton = null;
                return;
            }
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateResult.Severity = InfoBarSeverity.Error;
            UpdateResult.Title = "Fehler beim Prüfen auf Updates";
            UpdateResult.Message = $"Die Update-Prüfung konnte nicht durchgeführt werden ({ex.Message}). Bitte überprüfe deine Internetverbindung.";
            UpdateResult.ActionButton = null;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(string downloadUrl, string targetTag)
    {
        UpdateResult.Severity = InfoBarSeverity.Informational;
        UpdateResult.Title = "Update wird heruntergeladen...";
        UpdateResult.Message = "Verbindung mit GitHub wird hergestellt...";
        UpdateResult.ActionButton = null;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = true;

        try
        {
            var handler = new System.Net.Http.SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NextXLR-App/" + AppSettings.AppVersion);

            // Attempt download with fallback tag URLs if needed
            var urlsToTry = new List<string> { downloadUrl };
            var cleanTag = targetTag.TrimStart('v', 'V');
            var directTagUrl = $"https://github.com/plysystems/nextXLR/releases/download/{targetTag}/NextXLR-Setup-{cleanTag}.exe";
            if (!urlsToTry.Contains(directTagUrl)) urlsToTry.Add(directTagUrl);

            System.Net.Http.HttpResponseMessage? response = null;

            foreach (var url in urlsToTry)
            {
                try
                {
                    var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    var resp = await client.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                    {
                        response = resp;
                        break;
                    }
                    else
                    {
                        resp.Dispose();
                    }
                }
                catch { }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                UpdateResult.Severity = InfoBarSeverity.Error;
                UpdateResult.Title = "Download fehlgeschlagen";
                UpdateResult.Message = "Das Installationsprogramm konnte nicht von GitHub geladen werden. Bitte überprüfe deine Internetverbindung und versuche es in wenigen Augenblicken erneut.";
                var retryBtn = new Button { Content = "Erneut versuchen" };
                retryBtn.Click += async (_, _) => await DownloadAndInstallUpdateAsync(downloadUrl, targetTag);
                UpdateResult.ActionButton = retryBtn;
                return;
            }

            using (response)
            {
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                if (totalBytes > 0)
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    UpdateProgressBar.Maximum = 100;
                    UpdateProgressBar.Value = 0;
                }

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

                        if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 200)
                        {
                            lastReport = DateTime.UtcNow;
                            var mbRead = totalRead / (1024.0 * 1024.0);
                            if (totalBytes > 0)
                            {
                                var totalMb = totalBytes / (1024.0 * 1024.0);
                                var percent = (int)(totalRead * 100 / totalBytes);
                                UpdateProgressBar.Value = percent;
                                UpdateResult.Message = $"Herunterladen: {percent}% ({mbRead:F1} MB von {totalMb:F1} MB)...";
                            }
                            else
                            {
                                UpdateResult.Message = $"Herunterladen: {mbRead:F1} MB...";
                            }
                        }
                    }
                }

                UpdateProgressBar.Visibility = Visibility.Collapsed;
                UpdateResult.Severity = InfoBarSeverity.Success;
                UpdateResult.Title = "Update wird installiert...";
                UpdateResult.Message = "NEXT : XLR wird beendet, das Update wird im Hintergrund angewendet und die App startet automatisch neu...";

                await Task.Delay(1000);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempExe,
                    Arguments = $"--silent --restart --wait-pid {System.Diagnostics.Process.GetCurrentProcess().Id}",
                    UseShellExecute = true,
                };
                System.Diagnostics.Process.Start(psi);

                MainWindow.CurrentInstance?.ExitApplicationDirect();
            }
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateResult.Severity = InfoBarSeverity.Error;
            UpdateResult.Title = "Fehler beim Herunterladen des Updates";
            UpdateResult.Message = $"{ex.Message}. Bitte überprüfe deine Internetverbindung.";
            var retryBtn = new Button { Content = "Erneut versuchen" };
            retryBtn.Click += async (_, _) => await DownloadAndInstallUpdateAsync(downloadUrl, targetTag);
            UpdateResult.ActionButton = retryBtn;
        }
    }

    private static bool IsNewerRelease(string remoteTag, string currentVer)
    {
        var r = remoteTag.TrimStart('v', 'V');
        var c = currentVer.TrimStart('v', 'V');
        if (r.Equals(c, StringComparison.OrdinalIgnoreCase)) return false;

        var rParts = r.Split(new[] { '-', '+' }, 2);
        var cParts = c.Split(new[] { '-', '+' }, 2);

        if (Version.TryParse(rParts[0], out var rVer) && Version.TryParse(cParts[0], out var cVer))
        {
            if (rVer > cVer) return true;
            if (rVer < cVer) return false;
            // Base versions are equal (e.g. 0.1.1 vs 0.1.1)
            if (rParts.Length == 1 && cParts.Length > 1) return true; // remote is final release, local is prerelease
            if (rParts.Length > 1 && cParts.Length == 1) return false;
            return !r.Equals(c, StringComparison.OrdinalIgnoreCase);
        }

        return !r.Equals(c, StringComparison.OrdinalIgnoreCase);
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

    private void OpenThirdPartyNotices_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var noticesPath = Path.Combine(baseDir, "THIRD_PARTY_NOTICES.md");
            if (!File.Exists(noticesPath))
            {
                noticesPath = Path.Combine(baseDir, "..", "..", "..", "..", "..", "THIRD_PARTY_NOTICES.md");
            }
            if (File.Exists(noticesPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{Path.GetFullPath(noticesPath)}\"",
                    UseShellExecute = true
                });
                return;
            }
        }
        catch
        {
        }
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/blob/main/THIRD_PARTY_NOTICES.md"));
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

            _isLoadingEndpoints = true;
            try
            {
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
            }
            finally
            {
                _isLoadingEndpoints = false;
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
        if (_loading || _isLoadingEndpoints) return;
        _settings.EnableBluetoothHeadset = BluetoothToggle.IsOn;
        _settings.Save();
        ViewModel?.RefreshRouting();

        if (BluetoothToggle.IsOn && BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            Task.Run(() => BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone));
        }
        else
        {
            Task.Run(() => BluetoothAudioRouter.Instance.Stop());
        }

        UpdateBluetoothStatus();
    }

    private void BluetoothDeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _isLoadingEndpoints) return;
        if (BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            bool isAlreadyActive = string.Equals(_settings.BluetoothHeadsetDeviceId, selected.Id, StringComparison.OrdinalIgnoreCase) &&
                                   BluetoothAudioRouter.Instance.IsRunning;

            _settings.BluetoothHeadsetDeviceId = selected.Id;
            _settings.BluetoothHeadsetDeviceName = selected.Name;
            _settings.Save();

            if (BluetoothToggle.IsOn && !isAlreadyActive)
            {
                Task.Run(() => BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone));
            }
        }
        UpdateBluetoothStatus();
    }

    private void BluetoothSidetone_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _isLoadingEndpoints) return;
        _settings.BluetoothMuteMicSidetone = BluetoothSidetoneCheck.IsChecked == true;
        _settings.Save();

        if (BluetoothToggle.IsOn && BluetoothDeviceBox.SelectedItem is AudioEndpointItem selected)
        {
            Task.Run(() => BluetoothAudioRouter.Instance.Start(selected.Id, selected.Name, _settings.BluetoothMuteMicSidetone));
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
            BluetoothStatusInfo.Message = "Externe Ausgabe inaktiv. Kopfhörer-Signal wird direkt über die GoXLR 3,5 mm Klinkenbuchse ausgegeben.";
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
            BluetoothStatusInfo.Message = "Bitte wähle oben deine externe Output-Quelle aus der Liste.";
        }
    }

    #endregion

    #region Stream Deck Actions

    private async void CheckStreamDeckStatus_Click(object sender, RoutedEventArgs e)
    {
        await StreamDeckService.CheckStatusAsync();
        StreamDeckTestResult.Severity = StreamDeckService.IsApiOnline ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StreamDeckTestResult.Title = StreamDeckService.StatusText;
        StreamDeckTestResult.Message = StreamDeckService.StatusDetail;
        StreamDeckTestResult.IsOpen = true;
    }

    private void OpenStreamDeckApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("streamdeck:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                var localApp = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe");
                if (System.IO.File.Exists(localApp))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(localApp) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }

    private async void TestBleep_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await StreamDeckService.SendTestBleepAsync(true);
        StreamDeckTestResult.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StreamDeckTestResult.Title = ok ? "Zensur-Bleep aktiviert" : "Fehler beim Senden";
        StreamDeckTestResult.Message = ok ? "Befehl /api/bleep?active=true erfolgreich ausgeführt. (Schalte nach 500ms ab)" : "Konnte /api/bleep nicht erreichen.";
        StreamDeckTestResult.IsOpen = true;

        if (ok)
        {
            await Task.Delay(500);
            await StreamDeckService.SendTestBleepAsync(false);
        }
    }

    private async void TestMicMute_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await StreamDeckService.SendTestMuteToggleAsync("Mic");
        StreamDeckTestResult.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StreamDeckTestResult.Title = ok ? "Mic Mute umgeschaltet" : "Fehler beim Senden";
        StreamDeckTestResult.Message = ok ? "Befehl /api/mute?channel=Mic&action=toggle erfolgreich ausgeführt." : "Konnte /api/mute nicht erreichen.";
        StreamDeckTestResult.IsOpen = true;
    }

    private async void TestFaderA_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await StreamDeckService.SendTestFaderAsync("A", 0.75f);
        StreamDeckTestResult.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        StreamDeckTestResult.Title = ok ? "Fader A auf 75% gesetzt" : "Fehler beim Senden";
        StreamDeckTestResult.Message = ok ? "Befehl /api/fader?channel=A&volume=0.75 erfolgreich an Engine gesendet." : "Konnte /api/fader nicht erreichen.";
        StreamDeckTestResult.IsOpen = true;
    }

    private void OpenStreamDeckDoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var docPath = System.IO.Path.Combine(baseDir, "docs", "STREAMDECK.md");
            if (!System.IO.File.Exists(docPath))
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/blob/main/docs/STREAMDECK.md"));
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(docPath) { UseShellExecute = true });
            }
        }
        catch
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/plysystems/nextXLR/blob/main/docs/STREAMDECK.md"));
        }
    }

    private void OpenApiStatusBrowser_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("http://127.0.0.1:48500/api/status"));
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
