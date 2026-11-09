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

public sealed class RouteRowViewModel
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
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private double _peakPercent;

    public string VolumeText => $"{(int)Math.Round(Volume)}%";

    public AppSessionItemViewModel(AppAudioSessionInfo info, List<AudioEndpointChoice> outChoices, List<AudioEndpointChoice> inChoices)
    {
        _info = info;
        _outputChoices = outChoices;
        _inputChoices = inChoices;

        _volume = Math.Round(info.Volume * 100);
        _isMuted = info.IsMuted;

        int outIdx = 0;
        for (int i = 0; i < outChoices.Count; i++)
        {
            if (!string.IsNullOrEmpty(info.CurrentOutputDeviceId) &&
                outChoices[i].Id.Equals(info.CurrentOutputDeviceId, System.StringComparison.OrdinalIgnoreCase))
            {
                outIdx = i;
                break;
            }
        }
        _selectedOutputIndex = outIdx;

        int inIdx = 0;
        for (int i = 0; i < inChoices.Count; i++)
        {
            if (!string.IsNullOrEmpty(info.CurrentInputDeviceId) &&
                inChoices[i].Id.Equals(info.CurrentInputDeviceId, System.StringComparison.OrdinalIgnoreCase))
            {
                inIdx = i;
                break;
            }
        }
        _selectedInputIndex = inIdx;
    }

    public void UpdatePeak()
    {
        if (_info.MeterInformation != null)
        {
            try
            {
                float peak = _info.MeterInformation.MasterPeakValue;
                PeakPercent = Math.Clamp(peak * 100.0, 0.0, 100.0);
            }
            catch
            {
                PeakPercent = 0.0;
            }
        }
        else
        {
            PeakPercent = 0.0;
        }
    }

    partial void OnSelectedOutputIndexChanged(int value)
    {
        if (value >= 0 && value < _outputChoices.Count)
        {
            var choice = _outputChoices[value];
            string? devId = string.IsNullOrEmpty(choice.Id) ? null : choice.Id;
            WindowsAppAudioRouter.SetAppEndpoint(ProcessId, ProcessName, false, devId);
            string? inDevId = SelectedInputIndex >= 0 && SelectedInputIndex < _inputChoices.Count ? (string.IsNullOrEmpty(_inputChoices[SelectedInputIndex].Id) ? null : _inputChoices[SelectedInputIndex].Id) : null;
            WindowsAppAudioRouter.SaveRule(ProcessName, devId, inDevId);
        }
    }

    partial void OnSelectedInputIndexChanged(int value)
    {
        if (value >= 0 && value < _inputChoices.Count)
        {
            var choice = _inputChoices[value];
            string? devId = string.IsNullOrEmpty(choice.Id) ? null : choice.Id;
            WindowsAppAudioRouter.SetAppEndpoint(ProcessId, ProcessName, true, devId);
            string? outDevId = SelectedOutputIndex >= 0 && SelectedOutputIndex < _outputChoices.Count ? (string.IsNullOrEmpty(_outputChoices[SelectedOutputIndex].Id) ? null : _outputChoices[SelectedOutputIndex].Id) : null;
            WindowsAppAudioRouter.SaveRule(ProcessName, outDevId, devId);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeText));
        if (_info.VolumeControl != null)
        {
            try { _info.VolumeControl.Volume = (float)System.Math.Clamp(value / 100.0, 0.0, 1.0); } catch { }
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_info.VolumeControl != null)
        {
            try { _info.VolumeControl.Mute = value; } catch { }
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
                _meterTimer.Interval = TimeSpan.FromMilliseconds(50);
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

            AppSessions.Clear();
            foreach (var s in sessions)
            {
                var vm = new AppSessionItemViewModel(s, OutputChoices, InputChoices);

                // Auto-apply saved rule if configured
                if (rules.TryGetValue(s.ProcessName, out var rule))
                {
                    if (!string.IsNullOrEmpty(rule.OutputEndpointId))
                    {
                        var outIdx = OutputChoices.FindIndex(c => c.Id.Equals(rule.OutputEndpointId, StringComparison.OrdinalIgnoreCase));
                        if (outIdx >= 0 && vm.SelectedOutputIndex != outIdx)
                        {
                            vm.SelectedOutputIndex = outIdx;
                        }
                    }
                    if (!string.IsNullOrEmpty(rule.InputEndpointId))
                    {
                        var inIdx = InputChoices.FindIndex(c => c.Id.Equals(rule.InputEndpointId, StringComparison.OrdinalIgnoreCase));
                        if (inIdx >= 0 && vm.SelectedInputIndex != inIdx)
                        {
                            vm.SelectedInputIndex = inIdx;
                        }
                    }
                }

                AppSessions.Add(vm);
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
