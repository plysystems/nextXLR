using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

public partial class RouteCellViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
    private readonly string? _customChannelName;
    private bool _applyingServer;

    public uint ChannelId { get; }
    public uint BusId { get; }
    public string Tooltip { get; }
    /// <summary>Not allowed on the GoXLR (would feed a signal back into itself).</summary>
    public bool IsBlocked { get; init; }
    /// <summary>Permanently routed (e.g. Bleep to Broadcast, Chat Mic &amp; Monitor).</summary>
    public bool IsLocked { get; init; }
    public bool IsEnabled => !IsBlocked;

    [ObservableProperty] private bool _isRouted;

    public RouteCellViewModel(uint channel, uint bus, string tooltip, PipeTransport transport, string? customChannelName = null)
    {
        ChannelId = channel;
        BusId = bus;
        Tooltip = tooltip;
        _transport = transport;
        _customChannelName = customChannelName;
    }

    public void ApplyFromServer(bool routed)
    {
        _applyingServer = true;
        try { IsRouted = routed; } finally { _applyingServer = false; }
    }

    partial void OnIsRoutedChanged(bool value)
    {
        if (IsLocked)
        {
            if (!value) IsRouted = true;
            return;
        }

        if (BusId == 888)
        {
            if (!_applyingServer)
            {
                var channelKey = _customChannelName ?? ChannelId.ToString();
                var settings = AppSettings.Load();
                if (settings.WirelessRouting == null)
                {
                    settings.WirelessRouting = new(StringComparer.OrdinalIgnoreCase);
                }
                settings.WirelessRouting[channelKey] = value;
                settings.Save();
                BluetoothAudioRouter.Instance.SetChannelRouted(channelKey, value);
            }
            return;
        }

        if (!_applyingServer) _transport.Send(new SetRoute(ChannelId, BusId, value ? 1.0 : 0.0));
    }
}

public sealed class RouteRowViewModel : ObservableObject
{
    public required uint ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public string Glyph { get; init; } = "\uE767";
    public Microsoft.UI.Xaml.Media.Brush? Accent { get; init; }
    /// <summary>Headphones, (Wireless Headphones), Broadcast Stream Mix, Line Out, Chat Mic.</summary>
    public ObservableCollection<RouteCellViewModel> Cells { get; } = new();
    public int ColumnCount => Cells.Count;
    /// <summary>The Sampler recording input, shown in its own highlighted column.</summary>
    public RouteCellViewModel? SamplerCell { get; set; }
}

public partial class AppSessionItemViewModel : ObservableObject
{
    private readonly AppAudioSessionInfo _info;
    private readonly List<AudioEndpointChoice> _outputChoices;
    private readonly List<AudioEndpointChoice> _inputChoices;

    public uint ProcessId => _info.ProcessId;
    public string ProcessName => _info.ProcessName;
    public string DisplayName => _info.DisplayName;
    public string Glyph => _info.Glyph;

    public List<AudioEndpointChoice> OutputChoices => _outputChoices;
    public List<AudioEndpointChoice> InputChoices => _inputChoices;

    [ObservableProperty] private int _selectedOutputIndex;
    [ObservableProperty] private int _selectedInputIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private double _peakPercent;

    public bool IsInitialized { get; private set; }
    public string VolumeText => $"{(int)Math.Round(Volume)}%";

    public AppSessionItemViewModel(
        AppAudioSessionInfo info,
        List<AudioEndpointChoice> outChoices,
        List<AudioEndpointChoice> inChoices,
        WindowsAppAudioRouter.AppRoutingRule? rule = null)
    {
        _info = info;
        _outputChoices = outChoices;
        _inputChoices = inChoices;

        _volume = Math.Round(info.Volume * 100);
        _isMuted = info.IsMuted;

        // 1. Output choice priority: saved rule -> detected current endpoint -> default (0)
        int outIdx = 0;
        string? targetOutId = !string.IsNullOrEmpty(rule?.OutputEndpointId) ? rule.OutputEndpointId : info.CurrentOutputDeviceId;
        if (!string.IsNullOrEmpty(targetOutId))
        {
            int found = outChoices.FindIndex(c => c.Id.Equals(targetOutId, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) outIdx = found;
        }
        _selectedOutputIndex = outIdx;

        // 2. Input choice priority: saved rule -> detected current endpoint -> default (0)
        int inIdx = 0;
        string? targetInId = !string.IsNullOrEmpty(rule?.InputEndpointId) ? rule.InputEndpointId : info.CurrentInputDeviceId;
        if (!string.IsNullOrEmpty(targetInId))
        {
            int found = inChoices.FindIndex(c => c.Id.Equals(targetInId, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) inIdx = found;
        }
        _selectedInputIndex = inIdx;

        IsInitialized = true;
    }

    public void UpdatePeak()
    {
        if (_info.MeterInformation != null)
        {
            try
            {
                if (_info.MeterInformation.GetPeakValue(out float peak) == 0)
                {
                    var newPeak = Math.Clamp(peak * 100.0, 0.0, 100.0);
                    if (Math.Abs(PeakPercent - newPeak) > 0.5 || (newPeak == 0 && PeakPercent != 0))
                    {
                        PeakPercent = newPeak;
                    }
                }
                else if (PeakPercent != 0.0)
                {
                    PeakPercent = 0.0;
                }
            }
            catch
            {
                if (PeakPercent != 0.0) PeakPercent = 0.0;
            }
        }
        else if (PeakPercent != 0.0)
        {
            PeakPercent = 0.0;
        }
    }

    partial void OnSelectedOutputIndexChanged(int value)
    {
        if (!IsInitialized) return;
        ApplyOutputRouting(value);
    }

    partial void OnSelectedInputIndexChanged(int value)
    {
        if (!IsInitialized) return;
        ApplyInputRouting(value);
    }

    public void ApplyOutputRouting(int index)
    {
        if (index >= 0 && index < _outputChoices.Count)
        {
            if (SelectedOutputIndex != index)
            {
                SelectedOutputIndex = index;
            }
            var choice = _outputChoices[index];
            string? devId = string.IsNullOrEmpty(choice.Id) ? null : choice.Id;
            uint pid = ProcessId;
            string pName = ProcessName;
            string? inDevId = SelectedInputIndex >= 0 && SelectedInputIndex < _inputChoices.Count ? (string.IsNullOrEmpty(_inputChoices[SelectedInputIndex].Id) ? null : _inputChoices[SelectedInputIndex].Id) : null;
            Task.Run(() =>
            {
                WindowsAppAudioRouter.SetAppEndpoint(pid, pName, false, devId);
                WindowsAppAudioRouter.SaveRule(pName, devId, inDevId);
            });
        }
    }

    public void ApplyInputRouting(int index)
    {
        if (index >= 0 && index < _inputChoices.Count)
        {
            if (SelectedInputIndex != index)
            {
                SelectedInputIndex = index;
            }
            var choice = _inputChoices[index];
            string? devId = string.IsNullOrEmpty(choice.Id) ? null : choice.Id;
            uint pid = ProcessId;
            string pName = ProcessName;
            string? outDevId = SelectedOutputIndex >= 0 && SelectedOutputIndex < _outputChoices.Count ? (string.IsNullOrEmpty(_outputChoices[SelectedOutputIndex].Id) ? null : _outputChoices[SelectedOutputIndex].Id) : null;
            Task.Run(() =>
            {
                WindowsAppAudioRouter.SetAppEndpoint(pid, pName, true, devId);
                WindowsAppAudioRouter.SaveRule(pName, outDevId, devId);
            });
        }
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeText));
        if (_info.VolumeControl != null)
        {
            try { _info.VolumeControl.SetMasterVolume((float)Math.Clamp(value / 100.0, 0.0, 1.0), IntPtr.Zero); } catch { }
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_info.VolumeControl != null)
        {
            try { _info.VolumeControl.SetMute(value, IntPtr.Zero); } catch { }
        }
    }
}

public partial class AppRoutingManagerViewModel : ObservableObject
{
    public ObservableCollection<AppSessionItemViewModel> AppSessions { get; } = new();
    public List<AudioEndpointChoice> OutputChoices { get; private set; } = new();
    public List<AudioEndpointChoice> InputChoices { get; private set; } = new();

    [ObservableProperty] private bool _hasApps;
    [ObservableProperty] private bool _hasNoApps;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "";

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _meterTimer;

    public void StartMonitoring()
    {
        if (_meterTimer == null)
        {
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (queue != null)
            {
                _meterTimer = queue.CreateTimer();
                _meterTimer.Interval = TimeSpan.FromMilliseconds(75);
                _meterTimer.Tick += (_, _) =>
                {
                    foreach (var s in AppSessions)
                    {
                        s.UpdatePeak();
                    }
                };
            }
        }
        _meterTimer?.Start();
    }

    public void StopMonitoring()
    {
        _meterTimer?.Stop();
        foreach (var s in AppSessions)
        {
            s.PeakPercent = 0.0;
        }
    }

    public async void Refresh()
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsScanning = true;
        HasNoApps = false;
        try
        {
            var outChoices = await Task.Run(WindowsAppAudioRouter.GetOutputChoices);
            var inChoices = await Task.Run(WindowsAppAudioRouter.GetInputChoices);
            var sessions = await Task.Run(WindowsAppAudioRouter.GetActiveAppSessions);
            var rules = await Task.Run(WindowsAppAudioRouter.LoadRules);

            OutputChoices = outChoices;
            InputChoices = inChoices;

            var existingByPid = AppSessions.ToDictionary(a => a.ProcessId);
            var currentPids = new HashSet<uint>(sessions.Select(s => s.ProcessId));

            // Remove apps that have closed
            for (int i = AppSessions.Count - 1; i >= 0; i--)
            {
                if (!currentPids.Contains(AppSessions[i].ProcessId))
                {
                    AppSessions.RemoveAt(i);
                }
            }

            // Update existing or add new apps
            foreach (var s in sessions)
            {
                rules.TryGetValue(s.ProcessName, out var rule);
                if (existingByPid.TryGetValue(s.ProcessId, out var existing))
                {
                    existing.Volume = Math.Round(s.Volume * 100);
                    existing.IsMuted = s.IsMuted;
                }
                else
                {
                    var vm = new AppSessionItemViewModel(s, OutputChoices, InputChoices, rule);
                    AppSessions.Add(vm);
                }
            }

            HasApps = AppSessions.Count > 0;
            HasNoApps = !HasApps;
            StatusMessage = HasApps
                ? $"{AppSessions.Count} aktive Audio-Anwendung{(AppSessions.Count > 1 ? "en" : "")} erkannt"
                : "Keine aktiven Audio-Anwendungen gefunden. Spiele Ton in einer App ab, damit sie hier erscheint.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppRoutingManager.RefreshAsync error: {ex}");
            HasNoApps = AppSessions.Count == 0;
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void AutoAssignAll()
    {
        int count = 0;
        foreach (var app in AppSessions)
        {
            var name = app.ProcessName.ToLowerInvariant();

            // 1. Music apps -> Music / Musik
            if (name.Contains("spotify") || name.Contains("music") || name.Contains("itunes") || name.Contains("vlc") || name.Contains("aimp"))
            {
                var musicIdx = app.OutputChoices.FindIndex(c => c.ShortName == "Music" || c.ShortName == "Musik");
                if (musicIdx >= 0) { app.SelectedOutputIndex = musicIdx; count++; }
            }
            // 2. Voice / Chat apps -> Chat (Output) & Chat Mic (Input)
            else if (name.Contains("discord") || name.Contains("teamspeak") || name.Contains("ts3") || name.Contains("skype") || name.Contains("zoom") || name.Contains("teams") || name.Contains("slack"))
            {
                var chatIdx = app.OutputChoices.FindIndex(c => c.ShortName == "Chat");
                if (chatIdx >= 0) { app.SelectedOutputIndex = chatIdx; count++; }

                var micIdx = app.InputChoices.FindIndex(c => c.ShortName == "Chat Mic" || c.ShortName.Contains("Chat"));
                if (micIdx >= 0) { app.SelectedInputIndex = micIdx; }
            }
            // 3. Game apps -> Game
            else if (name.Contains("steam") || name.Contains("cs2") || name.Contains("valorant") || name.Contains("apex") || name.Contains("game") || name.Contains("overwatch") || name.Contains("league"))
            {
                var gameIdx = app.OutputChoices.FindIndex(c => c.ShortName == "Game");
                if (gameIdx >= 0) { app.SelectedOutputIndex = gameIdx; count++; }
            }
            // 4. Browsers / System -> System
            else if (name.Contains("chrome") || name.Contains("edge") || name.Contains("firefox") || name.Contains("opera") || name.Contains("brave"))
            {
                var sysIdx = app.OutputChoices.FindIndex(c => c.ShortName == "System");
                if (sysIdx >= 0) { app.SelectedOutputIndex = sysIdx; count++; }
            }
            // 5. OBS / Stream -> Broadcast Mix (Input)
            else if (name.Contains("obs") || name.Contains("streamlabs"))
            {
                var bcastIdx = app.InputChoices.FindIndex(c => c.ShortName == "Broadcast Mix" || c.ShortName.Contains("Broadcast"));
                if (bcastIdx >= 0) { app.SelectedInputIndex = bcastIdx; count++; }
            }
        }

        StatusMessage = $"{count} Anwendungen wurden automatisch den optimalen GoXLR-Kanälen zugewiesen!";
    }

    public void OpenWindowsMixer()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound-devicesavedsettings") { UseShellExecute = true });
        }
        catch { }
    }
}
