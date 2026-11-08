using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

public partial class RouteCellViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
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

    public RouteCellViewModel(uint channel, uint bus, string tooltip, PipeTransport transport)
    {
        ChannelId = channel;
        BusId = bus;
        Tooltip = tooltip;
        _transport = transport;
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
        if (!_applyingServer) _transport.Send(new SetRoute(ChannelId, BusId, value ? 1.0 : 0.0));
    }
}

public sealed class RouteRowViewModel
{
    public required uint ChannelId { get; init; }
    public required string ChannelName { get; init; }
    public string Glyph { get; init; } = "\uE767";
    public Microsoft.UI.Xaml.Media.Brush? Accent { get; init; }
    /// <summary>Headphones, Broadcast Stream Mix, Line Out, Chat Mic.</summary>
    public ObservableCollection<RouteCellViewModel> Cells { get; } = new();
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

    partial void OnSelectedOutputIndexChanged(int value)
    {
        if (value >= 0 && value < _outputChoices.Count)
        {
            var choice = _outputChoices[value];
            WindowsAppAudioRouter.SetAppEndpoint(ProcessId, ProcessName, false, string.IsNullOrEmpty(choice.Id) ? null : choice.Id);
        }
    }

    partial void OnSelectedInputIndexChanged(int value)
    {
        if (value >= 0 && value < _inputChoices.Count)
        {
            var choice = _inputChoices[value];
            WindowsAppAudioRouter.SetAppEndpoint(ProcessId, ProcessName, true, string.IsNullOrEmpty(choice.Id) ? null : choice.Id);
        }
    }

    partial void OnVolumeChanged(double value)
    {
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
    [ObservableProperty] private string _statusMessage = "";

    public async void Refresh()
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var outChoices = await Task.Run(WindowsAppAudioRouter.GetOutputChoices);
        var inChoices = await Task.Run(WindowsAppAudioRouter.GetInputChoices);
        var sessions = await Task.Run(WindowsAppAudioRouter.GetActiveAppSessions);

        OutputChoices = outChoices;
        InputChoices = inChoices;

        AppSessions.Clear();
        foreach (var s in sessions)
        {
            AppSessions.Add(new AppSessionItemViewModel(s, OutputChoices, InputChoices));
        }

        HasApps = AppSessions.Count > 0;
        StatusMessage = HasApps
            ? $"{AppSessions.Count} aktive Audio-Anwendung{(AppSessions.Count > 1 ? "en" : "")} erkannt"
            : "Keine aktiven Audio-Anwendungen gefunden. Spiele Ton in einer App ab, damit sie hier erscheint.";
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
