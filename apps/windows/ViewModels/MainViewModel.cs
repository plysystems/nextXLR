using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// The store: the only place incoming daemon messages touch view models. Everything is
/// marshalled onto the UI thread and reconciled by id, so bindings, focus and an in-progress
/// drag survive every update.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _ui;
    private readonly PipeTransport _transport;
    private readonly MeterAnimator _meterAnimator;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    /// <summary>Mixer "Inputs": channels on faders A–D first (in fader order), then the rest.</summary>
    public ObservableCollection<ChannelViewModel> InputStrips { get; } = new();
    /// <summary>Mixer "Outputs": Headphones, Broadcast Stream Mix, Line Out, Chat Mic, Mic Monitor.</summary>
    public ObservableCollection<ChannelViewModel> OutputStrips { get; } = new();
    public ObservableCollection<BusView> Buses { get; } = new();
    public ObservableCollection<RouteRowViewModel> RouteRows { get; } = new();
    [ObservableProperty] private string _samplerBusName = "";

    private RouteCellViewModel Cell(Channel c, Bus b)
    {
        var blocked = IsBlocked(c.Id, b.Id);
        var tip = blocked ? Loc.Get("RoutingBlocked") : $"{c.Name} → {BusView.Localize(b.Id, b.Name)}";
        return new RouteCellViewModel(c.Id, b.Id, tip, _transport) { IsBlocked = blocked };
    }

    [ObservableProperty] private string _connectionText = Loc.Get("StatusConnecting");
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private ChannelViewModel? _micChannel;
    /// <summary>Bleep held on the device or in a client.</summary>
    [ObservableProperty] private bool _bleepActive;
    /// <summary>Bleep volume level (0-100%).</summary>
    [ObservableProperty] private double _bleepVolume = 100;
    public string BleepVolumeText => $"{Math.Round(BleepVolume)} %";

    partial void OnBleepVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(BleepVolumeText));
        BleepSound.Volume = Math.Clamp(value / 100.0, 0.0, 1.0);
        try
        {
            var settings = AppSettings.Load();
            settings.BleepVolume = value;
            settings.Save();
        }
        catch { }
    }

    /// <summary>Global mic mute; two-way with the GoXLR cough button.</summary>
    [ObservableProperty] private bool _micMuted;
    private bool _applyingMicMute;

    partial void OnMicMutedChanged(bool value)
    {
        if (!_applyingMicMute) _transport.Send(new SetMicMute(value));
    }

    private static readonly Windows.UI.Color Neutral = Windows.UI.Color.FromArgb(255, 0x5E, 0x66, 0x75);

    /// <summary>
    /// Strips (and routing rows) on a hardware fader take that fader's lighting colour (top of its
    /// gradient), so the app matches the device; all other channels use a neutral grey.
    /// </summary>
    private void ApplyAccents(IReadOnlyList<FaderAssignment> faders, IReadOnlyList<LightingZone> lighting)
    {
        var faderMap = faders.ToDictionary(f => f.Channel, f => f.Fader);
        var lightingMap = lighting.ToDictionary(z => z.Zone, z => z.Active);

        foreach (var ch in Channels)
        {
            Windows.UI.Color color = Neutral;
            if (faderMap.TryGetValue(ch.Id, out var letter) && !string.IsNullOrEmpty(letter))
            {
                var zoneKey = letter.Length == 1 ? (letter[0] switch
                {
                    'A' or 'a' => "fader_a",
                    'B' or 'b' => "fader_b",
                    'C' or 'c' => "fader_c",
                    'D' or 'd' => "fader_d",
                    _ => "fader_" + char.ToLowerInvariant(letter[0])
                }) : "fader_" + letter.ToLowerInvariant();

                if (lightingMap.TryGetValue(zoneKey, out var hex))
                {
                    color = ColorHex.Parse(hex);
                }
            }
            if (ch.Accent.Color != color) ch.Accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        }
    }

    /// <summary>Physical faders A–D and the channel each one controls.</summary>
    public ObservableCollection<FaderSlotViewModel> FaderSlots { get; } = new(
        new[] { "A", "B", "C", "D" }.Select(l => new FaderSlotViewModel(l)));

    public void SetBleep(bool pressed) => _transport.Send(new Bleep(pressed));

    /// <summary>Attached device + capabilities; drives tab visibility and device-specific views.</summary>
    public HardwareState Hardware { get; } = new();

    // Page state lives here (pages are recreated on navigation).
    public EffectsViewModel Effects { get; }
    public SamplerViewModel Sampler { get; }
    public LightingViewModel Lighting { get; }
    public MicSettingsViewModel Mic { get; }

    /// <summary>Live picture of the device (lighting, buttons, faders, encoders); null = no device.</summary>
    [ObservableProperty] private object? _device;

    private void UpdateDevice(StateMessage s)
    {
        var zones = (s.Lighting ?? []).ToDictionary(z => z.Zone,
            z => new NextXLR.Controls.ZoneColors(ColorHex.Parse(z.Active), ColorHex.Parse(z.Inactive), z.Gradient));
        var faders = (s.Faders ?? []);
        Channel? On(int i) => faders.FirstOrDefault(f => f.Fader == ((char)('A' + i)).ToString()) is { } f
            ? s.Channels.FirstOrDefault(c => c.Id == f.Channel) : null;
        var fx = s.Effects;
        var p = fx?.Presets.ElementAtOrDefault(fx.Preset);
        Device = new NextXLR.Controls.DeviceSnapshot(
            Hardware.IsMini, zones,
            [.. Enumerable.Range(0, 4).Select(i => On(i)?.Volume ?? 0.8)],
            [.. Enumerable.Range(0, 4).Select(i => On(i)?.Name ?? (i switch { 0 => "Mic", 1 => "Music", 2 => "Chat", _ => "System" }))],
            [.. Enumerable.Range(0, 4).Select(i => On(i)?.Muted ?? false)],
            s.MicMuted, s.Bleep, fx?.Enabled ?? false, fx?.Preset ?? 0,
            p?.Megaphone ?? false, p?.Robot ?? false, p?.Hardtune ?? false,
            p is null ? [0.5, 0.5, 0, 0] : [(p.Pitch + 24) / 48.0, (p.Gender + 24) / 48.0, p.Reverb / 24.0, p.Echo / 24.0]);
    }

    /// <summary>Engine version from the daemon's hello (shown in Settings → Updates).</summary>
    [ObservableProperty] private string _engineVersion = "–";

    public MainViewModel(DispatcherQueue ui, PipeTransport transport)
    {
        _ui = ui;
        _transport = transport;
        _meterAnimator = new MeterAnimator(Channels);
        Lighting = new LightingViewModel(transport);
        Effects = new EffectsViewModel(transport);
        Mic = new MicSettingsViewModel(transport);
        Sampler = new SamplerViewModel(transport, ui);
        _transport.MessageReceived += m => _ui.TryEnqueue(() => Handle(m));
        _transport.StateChanged += s => _ui.TryEnqueue(() => OnConnection(s));

        try
        {
            var settings = AppSettings.Load();
            _bleepVolume = settings.BleepVolume;
            BleepSound.Volume = Math.Clamp(_bleepVolume / 100.0, 0.0, 1.0);
        }
        catch { }
    }

    private TaskCompletionSource<System.Text.Json.Nodes.JsonNode>? _profileRequest;
    private TaskCompletionSource<string?>? _profileLoad;

    /// <summary>The current setup from the daemon (labels, icons, volumes, mutes, routing, lighting).</summary>
    public async Task<System.Text.Json.Nodes.JsonNode> GetProfileAsync()
    {
        if (!IsConnected) throw new InvalidOperationException(Loc.Get("StatusOffline"));
        _profileRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Send(new GetProfile());
        var done = await Task.WhenAny(_profileRequest.Task, Task.Delay(3000));
        if (done != _profileRequest.Task) throw new TimeoutException(Loc.Get("ProfileTimeout"));
        return await _profileRequest.Task;
    }

    /// <summary>
    /// Sends a profile to the daemon. It validates everything first, then updates its state,
    /// pushes volumes/mutes/routing/lighting to the GoXLR and broadcasts the new state — which
    /// refreshes every page here. Returns an error message, or null on success.
    /// </summary>
    public async Task<string?> LoadProfileAsync(System.Text.Json.Nodes.JsonNode profile)
    {
        if (!IsConnected) return Loc.Get("StatusOffline");
        _profileLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        LastError = null;
        _transport.Send(new LoadProfile(profile));
        // Success = no error within a short window (the state broadcast arrives meanwhile).
        var done = await Task.WhenAny(_profileLoad.Task, Task.Delay(700));
        return done == _profileLoad.Task ? await _profileLoad.Task : null;
    }

    private void OnConnection(ConnectionState s)
    {
        IsConnected = s == ConnectionState.Connected;
        if (IsConnected) Sampler.PushLeds();   // the device's sampler LEDs follow the app
        ConnectionText = s switch
        {
            ConnectionState.Connected => Loc.Get("StatusConnected"),
            ConnectionState.Connecting => Loc.Get("StatusConnecting"),
            _ => Loc.Get("StatusOffline"),
        };
        if (s == ConnectionState.Connected) LastError = null;
        else { Hardware.Apply(null); }
    }

    private void Handle(ServerMessage msg)
    {
        switch (msg)
        {
            case StateMessage s:
                ReconcileChannels(s.Channels);
                ReconcileRouting(s);
                Hardware.Apply(s.Hardware.FirstOrDefault());
                ApplyFaders(s.Faders ?? []);
                ArrangeStrips(s.Faders ?? []);
                ApplyAccents(s.Faders ?? [], s.Lighting ?? []);
                ReconcileRouting(s);   // after accents, so row colours match the strips
                BleepActive = s.Bleep;
                _applyingMicMute = true;
                MicMuted = s.MicMuted;
                _applyingMicMute = false;
                if (s.Effects is { } fx) Effects.ApplyFromServer(fx);
                if (s.Mic is { } mic) Mic.ApplyFromServer(mic);
                Lighting.Apply(Hardware.LightingZoneIds, s.Lighting);
                UpdateDevice(s);
                break;
            case HelloMessage h:
                EngineVersion = h.Version;
                break;
            case ProfileMessage p:
                _profileRequest?.TrySetResult(p.Profile);
                break;
            case MetersMessage m:
                foreach (var l in m.Levels) Find(l.Channel)?.PushPeak(l.Peak);
                break;
            case ButtonMessage b:
                Sampler.HandleButton(b.Id, b.Pressed);
                break;
            case ErrorMessage e:
                LastError = e.Message;
                _profileLoad?.TrySetResult(e.Message);
                break;
        }
    }

    private ChannelViewModel? Find(uint id) => Channels.FirstOrDefault(c => c.Id == id);

    private void ApplyFaders(IReadOnlyList<FaderAssignment> faders)
    {
        foreach (var ch in Channels)
            ch.FaderLetter = faders.FirstOrDefault(f => f.Channel == ch.Id)?.Fader;
        foreach (var slot in FaderSlots)
        {
            slot.SyncOptions(Channels.Where(c => !c.IsFixed), _transport);
            slot.ApplyFromServer(faders.FirstOrDefault(f => f.Fader == slot.Letter)?.Channel);
        }
    }

    /// <summary>GoXLR routing table order: Mic, Chat, Music, Game, Console, Line In, System, Samples.</summary>
    private static readonly uint[] RoutingOrder = [0, 1, 2, 3, 7, 6, 4, 5];
    private const uint SamplerBus = 4;
    private static bool IsBlocked(uint ch, uint bus) => (ch, bus) is (5, 4) or (1, 3);

    private static readonly uint[] OutputOrder = [8, 11, 10, 12, 9];

    private void ArrangeStrips(IReadOnlyList<FaderAssignment> faders)
    {
        static int Rank(uint[] order, uint id) { var i = Array.IndexOf(order, id); return i < 0 ? 100 + (int)id : i; }
        int FaderRank(uint id) => faders.FirstOrDefault(f => f.Channel == id)?.Fader is { Length: 1 } l ? l[0] - 'A' : 99;
        var inputs = Channels.Where(c => !c.IsOutput)
            .OrderBy(c => FaderRank(c.Id)).ThenBy(c => Rank(RoutingOrder, c.Id)).ToList();
        var outputs = Channels.Where(c => c.IsOutput).OrderBy(c => Rank(OutputOrder, c.Id)).ToList();
        SyncOrder(InputStrips, inputs);
        SyncOrder(OutputStrips, outputs);
    }

    /// <summary>Reorders in place (Move), so existing strips — and a drag in progress — survive.</summary>
    private static void SyncOrder(ObservableCollection<ChannelViewModel> target, List<ChannelViewModel> desired)
    {
        for (int i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            var at = target.IndexOf(desired[i]);
            if (at < 0) target.Insert(i, desired[i]);
            else if (at != i) target.Move(at, i);
        }
    }

    private void ReconcileChannels(IReadOnlyList<Channel> channels)
    {
        for (int i = Channels.Count - 1; i >= 0; i--)
            if (channels.All(c => c.Id != Channels[i].Id)) Channels.RemoveAt(i);
        foreach (var c in channels)
        {
            if (Find(c.Id) is { } vm) vm.ApplyFromServer(c);
            else Channels.Add(new ChannelViewModel(c, _transport));
        }
        MicChannel = Find(0);
    }

    private void ReconcileRouting(StateMessage s)
    {
        var routed = s.Routes.Select(r => (r.Channel, r.Bus)).ToHashSet();
        var inputs = RoutingOrder.Select(id => s.Channels.FirstOrDefault(c => c.Id == id))
            .OfType<Channel>().ToList();
        var columns = s.Buses.Where(b => b.Id != SamplerBus).ToList();
        var sampler = s.Buses.FirstOrDefault(b => b.Id == SamplerBus);
        bool shapeChanged =
            Buses.Count != columns.Count || !Buses.Select(b => b.Id).SequenceEqual(columns.Select(b => b.Id)) ||
            RouteRows.Count != (inputs.Count + 1) || !RouteRows.Take(inputs.Count).Select(r => r.ChannelId).SequenceEqual(inputs.Select(c => c.Id)) ||
            // custom labels/icons rename the routing rows too
            !RouteRows.Take(inputs.Count).Select(r => r.ChannelName).SequenceEqual(inputs.Select(c => c.Name)) ||
            !RouteRows.Take(inputs.Count).Select(r => r.Glyph).SequenceEqual(inputs.Select(c => Find(c.Id)?.Glyph ?? "")) ||
            // fader colours: rows follow the strip accent
            !RouteRows.Take(inputs.Count).Select(r => (r.Accent as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color)
                .SequenceEqual(inputs.Select(c => Find(c.Id)?.Accent.Color));

        if (shapeChanged)
        {
            Buses.Clear();
            foreach (var b in columns) Buses.Add(new BusView(b.Id, b.Name));
            SamplerBusName = sampler is null ? "" : BusView.Localize(sampler.Id, sampler.Name);
            RouteRows.Clear();
            foreach (var c in inputs)
            {
                var strip = Find(c.Id);
                var row = new RouteRowViewModel
                {
                    ChannelId = c.Id,
                    ChannelName = c.Name,
                    Glyph = strip?.Glyph ?? "\uE767",
                    Accent = strip?.Accent,
                };
                foreach (var b in columns)
                    row.Cells.Add(Cell(c, b));
                if (sampler is not null) row.SamplerCell = Cell(c, sampler);
                RouteRows.Add(row);
            }

            // Bleep / Censor row: "!@#$?*"
            // Always routed to Chat Mic (3), Broadcast (1) and Mic Monitor (Headphones 0)
            var bleepRow = new RouteRowViewModel
            {
                ChannelId = 999,
                ChannelName = "!@#$?*",
                Glyph = "\uE7BA",
                Accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE0, 0x56, 0x38)),
            };
            foreach (var b in columns)
            {
                bool isBleepDest = b.Id is 0 or 1 or 3;
                var tip = isBleepDest ? Loc.Format("RoutingBleepActive", BusView.Localize(b.Id, b.Name)) : Loc.Get("RoutingBlocked");
                var cell = new RouteCellViewModel(999, b.Id, tip, _transport)
                {
                    IsBlocked = !isBleepDest,
                    IsLocked = isBleepDest,
                };
                cell.ApplyFromServer(isBleepDest);
                bleepRow.Cells.Add(cell);
            }
            if (sampler is not null)
            {
                var samplerCell = new RouteCellViewModel(999, sampler.Id, Loc.Get("RoutingBlocked"), _transport)
                {
                    IsBlocked = true,
                };
                samplerCell.ApplyFromServer(false);
                bleepRow.SamplerCell = samplerCell;
            }
            RouteRows.Add(bleepRow);
        }
        foreach (var row in RouteRows)
        {
            if (row.ChannelId == 999) continue;
            foreach (var cell in row.Cells.Append(row.SamplerCell).OfType<RouteCellViewModel>())
                cell.ApplyFromServer(routed.Contains((cell.ChannelId, cell.BusId)));
        }
    }
}

/// <summary>Output (bus) with a localized display name and an icon.</summary>
public sealed class BusView(uint id, string fallbackName)
{
    public uint Id { get; } = id;
    public string Name { get; } = Localize(id, fallbackName);
    public string Glyph { get; } = id switch
    {
        0 => "",   // headphones
        1 => "",   // broadcast
        2 => "",   // line out (speakers)
        3 => "",   // chat mic
        4 => "",   // sampler
        _ => "",
    };

    public static string Localize(uint id, string fallback)
    {
        var s = NextXLR.Services.Loc.Get($"Bus{id}");
        return s == $"Bus{id}" ? fallback : s;
    }
}
