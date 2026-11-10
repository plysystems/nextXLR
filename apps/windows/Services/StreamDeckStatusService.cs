using System;
using System.Diagnostics;
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

    [ObservableProperty] private bool _isApiOnline;
    [ObservableProperty] private bool _isAppRunning;
    [ObservableProperty] private string _statusText = "Stream Deck: Prüfe...";
    [ObservableProperty] private string _statusDetail = "Prüfe Verbindung zu mixforged Port 48500...";
    [ObservableProperty] private Brush _statusBrush;

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
        _statusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 130, 145)); // Initial gray
    }

    public void StartMonitoring(DispatcherQueue uiQueue)
    {
        _uiQueue = uiQueue;
        if (_pollTimer == null)
        {
            _pollTimer = _uiQueue.CreateTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(2.5);
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
            }
            catch { }

            // 2. Check if NextXLR Engine API (Port 48500) is answering
            bool apiOnline = false;
            try
            {
                var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/health");
                apiOnline = resp.IsSuccessStatusCode;
            }
            catch { }

            if (_uiQueue != null)
            {
                _uiQueue.TryEnqueue(() =>
                {
                    IsApiOnline = apiOnline;
                    IsAppRunning = appRunning;

                    if (apiOnline && appRunning)
                    {
                        StatusText = "Stream Deck: Aktiv";
                        StatusDetail = "Elgato Stream Deck ist aktiv und mit der NextXLR Engine verbunden (Port 48500).";
                        StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)); // Emerald Green
                    }
                    else if (apiOnline)
                    {
                        StatusText = "Stream Deck: Bereit";
                        StatusDetail = "NextXLR REST & WebSocket API ist auf Port 48500 bereit (Stream Deck Software nicht gestartet).";
                        StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x4F, 0xE0, 0xB8)); // Brand Turquoise
                    }
                    else
                    {
                        StatusText = "Stream Deck: Offline";
                        StatusDetail = "Die NextXLR Engine API auf Port 48500 ist momentan nicht erreichbar.";
                        StatusBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 130, 145)); // Gray Muted
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

    // --- Interactive Stream Deck Test Actions ---

    public async Task<bool> SendTestFaderAsync(string channel, float volume)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/api/fader?channel={Uri.EscapeDataString(channel)}&volume={volume:F2}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SendTestBleepAsync(bool active)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/api/bleep?active={active.ToString().ToLowerInvariant()}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SendTestMuteToggleAsync(string channel)
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/api/mute?channel={Uri.EscapeDataString(channel)}&action=toggle");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> FetchSnapshotJsonAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"http://127.0.0.1:{DefaultPort}/api/status");
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

