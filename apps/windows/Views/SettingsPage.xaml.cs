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
        AutoUpdateToggle.IsOn = _settings.CheckForUpdatesOnStart;
        AppVersionText.Text = Loc.Format("SetAppVersion", AppSettings.AppVersion);
        EngineVersionText.Text = Loc.Format("SetEngineVersion", ViewModel.EngineVersion);
        UpdateDriverStatus();
        _loading = false;
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPanel is null) return;
        var tag = (SectionList.SelectedItem as ListViewItem)?.Tag as string ?? "general";
        GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
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
                    UpdateResult.Severity = InfoBarSeverity.Success;
                    UpdateResult.Title = $"Neues Update verfügbar: {tagName}";
                    UpdateResult.Message = $"{releaseNotes}\nInstalliert: v{AppSettings.AppVersion}. Klicke unten, um das Update von GitHub herunterzuladen.";
                    var btn = new Button { Content = "Update herunterladen (GitHub)", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                    btn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(releaseUrl));
                    UpdateResult.ActionButton = btn;
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
}
