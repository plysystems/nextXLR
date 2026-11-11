using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NextXLR.Services;

public partial class StreamDeckStatusService : ObservableObject
{
    private static readonly Lazy<StreamDeckStatusService> _instance = new(() => new StreamDeckStatusService());
    public static StreamDeckStatusService Instance => _instance.Value;

    private readonly HttpClient _http;
    private DispatcherQueue? _uiQueue;
    private DispatcherQueueTimer? _pollTimer;
    private bool _isChecking;

    public const int DefaultPort = 48500;
    public const int LegacyPort = 14565;

    [ObservableProperty] private int _activePort = DefaultPort;
    [ObservableProperty] private bool _isApiOnline;
    [ObservableProperty] private bool _isAppRunning;
    [ObservableProperty] private bool _isPluginInstalled;
    [ObservableProperty] private string _pluginStatusText = "Plugin nicht installiert";
    [ObservableProperty] private string _appRunningText = "Software ist aktuell nicht gestartet";
    [ObservableProperty] private string _statusText = "Stream Deck: Prüfe...";
    [ObservableProperty] private string _statusDetail = "Prüfe Verbindung zu mixforged Port 48500...";

    public bool IsFullyActive => IsApiOnline && IsAppRunning;

    private StreamDeckStatusService()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(500)
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(1)
        };
    }

    public void StartMonitoring(DispatcherQueue uiQueue)
    {
        _uiQueue = uiQueue;
        if (_pollTimer == null)
        {
            _pollTimer = _uiQueue.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(4.0);
            _pollTimer.Tick += async (_, _) => await CheckStatusAsync();
        }
        _pollTimer.Start();
        _ = CheckStatusAsync();
    }

    public void StopMonitoring()
    {
        _pollTimer?.Stop();
    }

    public async Task CheckStatusAsync()
    {
        if (_isChecking) return;
        _isChecking = true;

        try
        {
            // 1. Check if StreamDeck software is running
            bool appRunning = false;
            try
            {
                var procs = Process.GetProcessesByName("StreamDeck");
                appRunning = procs.Length > 0;
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            catch { }

            // 2. Check if NextXLR Engine API is answering (Try 48500 first, then fallback to 14565)
            bool apiOnline = false;
            int detectedPort = DefaultPort;

            try
            {
                var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/health");
                if (resp.IsSuccessStatusCode)
                {
                    apiOnline = true;
                    detectedPort = DefaultPort;
                }
            }
            catch { }

            if (!apiOnline)
            {
                try
                {
                    var respLegacy = await _http.GetAsync($"http://127.0.0.1:{LegacyPort}/health");
                    if (respLegacy.IsSuccessStatusCode)
                    {
                        apiOnline = true;
                        detectedPort = LegacyPort;
                    }
                }
                catch { }
            }

            // 3. Check if Stream Deck Plugin is installed
            bool pluginInstalled = false;
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string pluginDir = Path.Combine(appData, @"Elgato\StreamDeck\Plugins\com.plysystems.nextxlr.sdPlugin");
                string manifestFile = Path.Combine(pluginDir, "manifest.json");
                pluginInstalled = File.Exists(manifestFile);
            }
            catch { }

            if (_uiQueue != null)
            {
                _uiQueue.TryEnqueue(() =>
                {
                    ActivePort = detectedPort;
                    IsApiOnline = apiOnline;
                    IsAppRunning = appRunning;
                    IsPluginInstalled = pluginInstalled;

                    PluginStatusText = pluginInstalled
                        ? "Plugin installiert & einsatzbereit"
                        : "Plugin noch nicht installiert";

                    AppRunningText = appRunning
                        ? "Software läuft im Hintergrund"
                        : "Software ist aktuell nicht gestartet";

                    if (apiOnline && appRunning)
                    {
                        StatusText = "Stream Deck: Aktiv";
                        StatusDetail = $"Elgato Stream Deck ist aktiv und mit der NextXLR Engine verbunden (Port {detectedPort}).";
                    }
                    else if (apiOnline)
                    {
                        StatusText = "Stream Deck: Bereit";
                        StatusDetail = $"NextXLR Engine API ist auf Port {detectedPort} bereit (Stream Deck Software nicht gestartet).";
                    }
                    else
                    {
                        StatusText = "Stream Deck: Offline";
                        StatusDetail = $"Die NextXLR Engine API auf Port {DefaultPort} ist momentan nicht erreichbar.";
                    }

                    OnPropertyChanged(nameof(IsFullyActive));
                });
            }
        }
        catch { }
        finally
        {
            _isChecking = false;
        }
    }

    public async Task<bool> EnsureDaemonRunningAsync()
    {
        App.EnsureDaemonRunning();
        await Task.Delay(600);
        await CheckStatusAsync();
        return IsApiOnline;
    }

    public async Task<bool> InstallPluginAsync()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string elgatoPluginsDir = Path.Combine(appData, @"Elgato\StreamDeck\Plugins");
            string targetPluginDir = Path.Combine(elgatoPluginsDir, "com.plysystems.nextxlr.sdPlugin");

            string baseDir = AppContext.BaseDirectory;

            // 1. First trigger official Elgato Stream Deck installation dialog if .streamDeckPlugin package exists
            string[] pkgCandidates =
            [
                Path.Combine(baseDir, @"Assets\StreamDeck\com.plysystems.nextxlr.streamDeckPlugin"),
                @"E:\Projects\plysys\Neuer Ordner\goxlr\dist\com.plysystems.nextxlr.streamDeckPlugin"
            ];

            bool launchedPkg = false;
            foreach (var pkg in pkgCandidates)
            {
                if (File.Exists(pkg))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(pkg) { UseShellExecute = true });
                        launchedPkg = true;
                    }
                    catch { }
                    break;
                }
            }

            // 2. Also ensure plugin files are safely copied to Plugins directory
            string[] sourceCandidateDirs =
            [
                Path.Combine(baseDir, @"Assets\StreamDeck\com.plysystems.nextxlr.sdPlugin"),
                @"E:\Projects\plysys\Neuer Ordner\goxlr\plugins\streamdeck\com.plysystems.nextxlr.sdPlugin",
                Path.Combine(baseDir, @"..\..\..\..\plugins\streamdeck\com.plysystems.nextxlr.sdPlugin")
            ];

            string? sourceDir = null;
            foreach (var cand in sourceCandidateDirs)
            {
                if (Directory.Exists(cand) && File.Exists(Path.Combine(cand, "manifest.json")))
                {
                    sourceDir = cand;
                    break;
                }
            }

            if (sourceDir != null)
            {
                try
                {
                    if (!Directory.Exists(elgatoPluginsDir))
                    {
                        Directory.CreateDirectory(elgatoPluginsDir);
                    }
                    CopyDirectory(sourceDir, targetPluginDir);
                }
                catch { }
            }

            await Task.Delay(500);
            await CheckStatusAsync();
            return IsPluginInstalled || launchedPkg;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            try
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            catch { }
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            // Avoid recursive nesting of the plugin directory
            if (dirName.Equals("com.plysystems.nextxlr.sdPlugin", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                string dest = Path.Combine(destDir, dirName);
                CopyDirectory(dir, dest);
            }
            catch { }
        }
    }

    // --- Interactive Stream Deck Test Actions ---

    public async Task<bool> SendTestFaderAsync(string channel, float volume)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{ActivePort}/api/fader?channel={Uri.EscapeDataString(channel)}&volume={volume:F2}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SendTestBleepAsync(bool active)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{ActivePort}/api/bleep?active={active.ToString().ToLowerInvariant()}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SendTestMuteToggleAsync(string channel)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{ActivePort}/api/mute?channel={Uri.EscapeDataString(channel)}&action=toggle");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> FetchSnapshotJsonAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{ActivePort}/api/status");
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsStringAsync();
            }
            return $"{{\"error\": \"HTTP {resp.StatusCode}\"}}";
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }
}
