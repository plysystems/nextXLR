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

    public static MainViewModel? Instance { get; private set; }
    public static bool HasPendingChanges { get; set; }
    public static bool IsCurrentMini { get; set; }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    /// <summary>Mixer "Inputs": channels on faders A–D first (in fader order), then the rest.</summary>
    public ObservableCollection<ChannelViewModel> InputStrips { get; } = new();
    /// <summary>Mixer "Outputs": Headphones, Broadcast Stream Mix, Line Out, Chat Mic, Mic Monitor.</summary>
    public ObservableCollection<ChannelViewModel> OutputStrips { get; } = new();
    public ObservableCollection<BusView> Buses { get; } = new();
    public ObservableCollection<RouteRowViewModel> RouteRows { get; } = new();
    public AppRoutingManagerViewModel AppRouting { get; } = new();
    public StreamDeckStatusService StreamDeck => StreamDeckStatusService.Instance;

    public void SetMetersActive(bool active) => _meterAnimator.IsActive = active;

    public void Dispose()
    {
        _meterAnimator.Dispose();
        StreamDeckStatusService.Instance.StopMonitoring();
    }
    [ObservableProperty] private string _samplerBusName = "";
    [ObservableProperty] private int _routingColumnCount = 4;

    private RouteCellViewModel Cell(Channel c, Bus b)
    {
        if (b.Id == 888)
        {
            var wirelessTip = $"{c.Name} → Externer Kopfhörer";
            var wirelessCell = new RouteCellViewModel(c.Id, 888, wirelessTip, _transport, c.Name);
            wirelessCell.ApplyFromServer(AppSettings.Load().IsWirelessChannelRouted(c.Name));
            return wirelessCell;
        }
        var blocked = IsBlocked(c.Id, b.Id);
        var tip = blocked ? Loc.Get("RoutingBlocked") : $"{c.Name} → {BusView.Localize(b.Id, b.Name)}";
        return new RouteCellViewModel(c.Id, b.Id, tip, _transport, c.Name) { IsBlocked = blocked };
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

    /// <summary>Mixer Mode: 0 = Kopfhörer-Mix (Default & Standard), 1 = Stream-Mix, 2 = Dual-Mix (Beide gleichzeitig).</summary>
    [ObservableProperty] private int _activeMixIndex = 0;
    /// <summary>In Dual view: which mix physical GoXLR hardware faders control (0 = Kopfhörer [Default], 1 = Stream).</summary>
    [ObservableProperty] private int _dualFaderTarget = 0; // 0 = Kopfhörer, 1 = Stream
    [ObservableProperty] private bool _isSubmixActive = true;

    public bool IsDualMode => ActiveMixIndex == 2;
    public bool IsDualKopfhoererTarget => ActiveMixIndex == 2 && DualFaderTarget == 0;
    public bool IsDualStreamTarget => ActiveMixIndex == 2 && DualFaderTarget == 1;

    public bool TargetSubmix => ActiveMixIndex switch
    {
        0 => true,                   // Kopfhörer [Default]: always Kopfhörer (Submix)
        1 => false,                  // Stream: always Stream
        2 => (DualFaderTarget == 0), // Dual: user chooses Kopfhörer or Stream
        _ => true                    // Default: Kopfhörer
    };

    partial void OnActiveMixIndexChanged(int value) => ApplyFaderTargetAndMixMode();
    partial void OnDualFaderTargetChanged(int value) => ApplyFaderTargetAndMixMode();

    public void SetDualFaderTargetKopfhoerer() => DualFaderTarget = 0;
    public void SetDualFaderTargetStream() => DualFaderTarget = 1;

    public void ApplyFaderTargetAndMixMode()
    {
        bool showDual = (ActiveMixIndex == 2);
        bool targetSubmix = TargetSubmix;
        IsSubmixActive = targetSubmix;
        foreach (var ch in Channels)
        {
            ch.ShowDualMix = showDual && !ch.IsOutput;
            ch.IsSubmixActive = targetSubmix;
            ch.DualFaderIsKopfhoerer = (DualFaderTarget == 0);
        }
        _transport.Send(new SetFaderTargetSubmix(targetSubmix));
        OnPropertyChanged(nameof(MixModeTitle));
        OnPropertyChanged(nameof(MixModeDescription));
        OnPropertyChanged(nameof(IsDualMode));
        OnPropertyChanged(nameof(IsDualKopfhoererTarget));
        OnPropertyChanged(nameof(IsDualStreamTarget));
    }

    public string MixModeTitle => ActiveMixIndex switch
    {
        0 => "Kopfhörer-Mix (Monitor)",
        1 => "Stream-Mix (Broadcast)",
        2 => "Dual-Mix Ansicht (Kopfhörer & Stream)",
        _ => "Kopfhörer-Mix (Monitor)"
    };

    public string MixModeDescription => ActiveMixIndex switch
    {
        0 => "Kopfhörer-Mix aktiv [Standard]: Physische GoXLR-Fader steuern das persönliche Monitoring.",
        1 => "Stream-Mix aktiv: Physische GoXLR-Fader steuern den Stream-Broadcast.",
        2 => DualFaderTarget == 0
            ? "Dual-Mix aktiv: Physische GoXLR-Fader steuern den Kopfhörer-Mix."
            : "Dual-Mix aktiv: Physische GoXLR-Fader steuern den Stream-Mix.",
        _ => "Kopfhörer-Mix aktiv: Physische GoXLR-Fader steuern das persönliche Monitoring."
    };

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
        var faderMap = faders.GroupBy(f => f.Channel).ToDictionary(g => g.Key, g => g.Last().Fader);
        var lightingMap = lighting.GroupBy(z => z.Zone).ToDictionary(g => g.Key, g => g.Last().Active);

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
        var zones = (s.Lighting ?? []).GroupBy(z => z.Zone).ToDictionary(
            g => g.Key,
            g => new NextXLR.Controls.ZoneColors(ColorHex.Parse(g.Last().Active), ColorHex.Parse(g.Last().Inactive), g.Last().Gradient));
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

    /// <summary>Global loading / busy state indicator.</summary>
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "Laden...";

    public void SetBusy(bool busy, string text = "Laden...")
    {
        _ui.TryEnqueue(() =>
        {
            IsBusy = busy;
            BusyText = text;
        });
    }

    public MainViewModel(DispatcherQueue ui, PipeTransport transport)
    {
        Instance = this;
        _ui = ui;
        _transport = transport;
        _meterAnimator = new MeterAnimator(Channels);
        Lighting = new LightingViewModel(transport);
        Effects = new EffectsViewModel(transport);
        Mic = new MicSettingsViewModel(transport);
        VstPluginService.ActiveMicViewModel = Mic;
        Sampler = new SamplerViewModel(transport, ui);
        _transport.MessageReceived += m => _ui.TryEnqueue(() => Handle(m));
        _transport.StateChanged += s => _ui.TryEnqueue(() => OnConnection(s));

        try
        {
            var settings = AppSettings.Load();
            _bleepVolume = settings.BleepVolume;
            BleepSound.Volume = Math.Clamp(_bleepVolume / 100.0, 0.0, 1.0);
            _activeMixerPage = settings.ActiveMixerPage;
        }
        catch { }

        StreamDeckStatusService.Instance.StartMonitoring(_ui);
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
        SetBusy(true, "Lade Profil...");
        try
        {
            _profileLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
            LastError = null;
            _transport.Send(new LoadProfile(profile));
            // Success = no error within a short window (the state broadcast arrives meanwhile).
            var done = await Task.WhenAny(_profileLoad.Task, Task.Delay(700));
            return done == _profileLoad.Task ? await _profileLoad.Task : null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnConnection(ConnectionState s)
    {
        IsConnected = s == ConnectionState.Connected;
        if (IsConnected)
        {
            _transport.Send(new SetFaderTargetSubmix(ActiveMixIndex == 0 || ActiveMixIndex == 2));
            Sampler.PushLeds();   // the device's sampler LEDs follow the app
            SetBusy(false);
        }
        else if (s == ConnectionState.Connecting)
        {
            SetBusy(true, "Verbinde mit GoXLR Audio-Engine...");
        }
        else
        {
            SetBusy(false);
        }

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
                OnPropertyChanged(nameof(CanUseMixerPages));
                OnPropertyChanged(nameof(MixerPagesVisibility));
                ApplyFaders(s.Faders ?? []);
                ArrangeStrips(s.Faders ?? []);
                ApplyAccents(s.Faders ?? [], s.Lighting ?? []);
                ReconcileRouting(s);   // after accents, so row colours match the strips
                bool prevBleep = BleepActive;
                BleepActive = s.Bleep;
                bool prevCough = MicMuted;
                _applyingMicMute = true;
                MicMuted = s.MicMuted;
                _applyingMicMute = false;

                // Hardware combination on GoXLR (Full): MUTE + !@#$?* (Bleep) toggles Mixer Page
                if (CanUseMixerPages && s.Bleep && s.MicMuted && (!prevBleep || !prevCough))
                {
                    ToggleMixerPage();
                }
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
                foreach (var l in m.Levels)
                {
                    var ch = Find(l.Channel);
                    ch?.PushPeak(l.Peak);
                    if (l.Channel == 11 || (ch != null && (ch.Name == "Stream" || ch.Name == "Broadcast")))
                    {
                        SafetyLimiterService.Instance.UpdateMeters(l.Peak);
                    }
                }
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

    /// <summary>Canonical studio console order: Mic (0), Chat (1), Music (2), System (4), Game (3), Console (7), Line In (6), Samples (5).</summary>
    private static readonly uint[] RoutingOrder = [0, 1, 2, 4, 3, 7, 6, 5];
    private const uint SamplerBus = 4;
    private static bool IsBlocked(uint ch, uint bus) => (ch, bus) is (5, 4) or (1, 3);

    /// <summary>Canonical outputs order: Headphones (8), Broadcast Stream Mix (11), Line Out (10), Chat Mic (12), Monitor (9).</summary>
    private static readonly uint[] OutputOrder = [8, 11, 10, 12, 9];

    private IReadOnlyList<FaderAssignment>? _pendingFaderArrangement;

    public void OnChannelDragEnded()
    {
        if (_pendingFaderArrangement != null && !Channels.Any(c => c.IsDragging))
        {
            var pending = _pendingFaderArrangement;
            _pendingFaderArrangement = null;
            ArrangeStrips(pending);
        }
    }

    private void ArrangeStrips(IReadOnlyList<FaderAssignment> faders)
    {
        if (Channels.Any(c => c.IsDragging))
        {
            _pendingFaderArrangement = faders;
            return;
        }
        static int Rank(uint[] order, uint id) { var i = Array.IndexOf(order, id); return i < 0 ? 100 + (int)id : i; }
        var inputs = Channels.Where(c => !c.IsOutput)
            .OrderBy(c => Rank(RoutingOrder, c.Id)).ToList();
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
            if (Find(c.Id) is { } vm)
            {
                vm.IsSubmixActive = IsSubmixActive;
                vm.ShowDualMix = (ActiveMixIndex == 2) && !vm.IsOutput;
                vm.DualFaderIsKopfhoerer = (DualFaderTarget == 0);
                vm.ApplyFromServer(c);
            }
            else
            {
                var newVm = new ChannelViewModel(c, _transport)
                {
                    IsSubmixActive = IsSubmixActive,
                    ShowDualMix = (ActiveMixIndex == 2) && (c.Kind != "output"),
                    DualFaderIsKopfhoerer = (DualFaderTarget == 0)
                };
                Channels.Add(newVm);
            }
        }
        MicChannel = Find(0);
        if (Find(8) is { } hpChannel)
        {
            BluetoothAudioRouter.Instance.Volume = (float)hpChannel.Volume;
            BluetoothAudioRouter.Instance.IsMuted = hpChannel.Muted;
        }
        if (!_muteOnStartupApplied)
        {
            _muteOnStartupApplied = true;
            try
            {
                if (AppSettings.Load().MuteOnStartup)
                {
                    MicMuted = true;
                }
            }
            catch { }
        }
    }

    private bool _muteOnStartupApplied;

    private StateMessage? _lastState;

    public void RefreshRouting()
    {
        if (_lastState != null)
        {
            ReconcileRouting(_lastState);
        }
    }

    private void ReconcileRouting(StateMessage s)
    {
        _lastState = s;
        var routed = s.Routes.Select(r => (r.Channel, r.Bus)).ToHashSet();
        var inputs = RoutingOrder.Select(id => s.Channels.FirstOrDefault(c => c.Id == id))
            .OfType<Channel>().ToList();
        var columns = s.Buses.Where(b => b.Id != SamplerBus).ToList();
        var sampler = s.Buses.FirstOrDefault(b => b.Id == SamplerBus);

        var settings = AppSettings.Load();
        if (settings.EnableBluetoothHeadset)
        {
            // Insert Externer Kopfhörer column right after standard Kopfhörer
            columns.Insert(1, new Bus(888, "Externer Kopfhörer"));
        }

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
            RoutingColumnCount = Buses.Count > 0 ? Buses.Count : 4;
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
                if (b.Id == 888)
                {
                    var wirelessTip = "!@#$?* → Externer Kopfhörer";
                    var wirelessCell = new RouteCellViewModel(999, 888, wirelessTip, _transport, "!@#$?*")
                    {
                        IsBlocked = false,
                        IsLocked = false,
                    };
                    wirelessCell.ApplyFromServer(settings.IsWirelessChannelRouted("!@#$?*"));
                    bleepRow.Cells.Add(wirelessCell);
                    continue;
                }
                bool isBleepDest = b.Id is 0 or 1 or 3;
                var tip = isBleepDest ? Loc.Format("RoutingBleepActive", BusView.Localize(b.Id, b.Name)) : Loc.Get("RoutingBlocked");
                var cell = new RouteCellViewModel(999, b.Id, tip, _transport, "!@#$?*")
                {
                    IsBlocked = !isBleepDest,
                    IsLocked = isBleepDest,
                };
                cell.ApplyFromServer(isBleepDest);
                bleepRow.Cells.Add(cell);
            }
            if (sampler is not null)
            {
                var samplerCell = new RouteCellViewModel(999, sampler.Id, Loc.Get("RoutingBlocked"), _transport, "!@#$?*")
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
            {
                if (cell.BusId == 888)
                {
                    cell.ApplyFromServer(settings.IsWirelessChannelRouted(row.ChannelName));
                }
                else
                {
                    cell.ApplyFromServer(routed.Contains((cell.ChannelId, cell.BusId)));
                }
            }
        }
    }

    // ---------------- Mixer Pages (GoXLR Full Model only) ----------------
    public bool CanUseMixerPages => !Hardware.IsMini;
    public Microsoft.UI.Xaml.Visibility MixerPagesVisibility => CanUseMixerPages ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty] private int _activeMixerPage = 1;
    public bool IsPage1Active => ActiveMixerPage == 1;
    public bool IsPage2Active => ActiveMixerPage == 2;
    public string MixerPageDescription => ActiveMixerPage == 1 ? "Seite 1" : "Seite 2";

    public void SwitchMixerPage(int page)
    {
        if (Hardware.IsMini) return;
        if (page < 1 || page > 2) return;

        ActiveMixerPage = page;
        var settings = AppSettings.Load();
        settings.ActiveMixerPage = page;
        settings.EnsureDistinctMixerPages();
        settings.Save();

        uint[] targetChannels = page == 1 ? settings.MixerPage1Channels : settings.MixerPage2Channels;
        string[] letters = ["A", "B", "C", "D"];

        for (int i = 0; i < 4 && i < targetChannels.Length; i++)
        {
            uint chId = targetChannels[i];
            _transport.Send(new AssignFader(letters[i], chId));
            if (i < FaderSlots.Count)
            {
                FaderSlots[i].ApplyFromServer(chId);
            }
        }

        OnPropertyChanged(nameof(ActiveMixerPage));
        OnPropertyChanged(nameof(IsPage1Active));
        OnPropertyChanged(nameof(IsPage2Active));
        OnPropertyChanged(nameof(MixerPageDescription));
    }

    public void ToggleMixerPage()
    {
        if (Hardware.IsMini) return;
        SwitchMixerPage(ActiveMixerPage == 1 ? 2 : 1);
    }

    public void OnFaderSlotAssigned(string letter, uint channelId)
    {
        if (Hardware.IsMini) return;
        int index = letter.ToUpperInvariant() switch
        {
            "A" => 0,
            "B" => 1,
            "C" => 2,
            _ => 3
        };

        var settings = AppSettings.Load();
        settings.EnsureDistinctMixerPages();

        uint[] currentChannels = ActiveMixerPage == 1 ? settings.MixerPage1Channels : settings.MixerPage2Channels;
        uint[] otherChannels = ActiveMixerPage == 1 ? settings.MixerPage2Channels : settings.MixerPage1Channels;

        if (currentChannels.Length <= index) return;

        uint oldChannel = currentChannels[index];
        if (oldChannel == channelId) return;

        // 1. Wenn der gewählte Regler auf der anderen Seite liegt, tausche die Position mit oldChannel
        int otherIndex = Array.IndexOf(otherChannels, channelId);
        if (otherIndex >= 0)
        {
            otherChannels[otherIndex] = oldChannel;
        }

        // 2. Wenn der Regler auf derselben Seite in einem anderen Slot lag, tausche dort mit oldChannel
        int samePageIndex = Array.IndexOf(currentChannels, channelId);
        if (samePageIndex >= 0 && samePageIndex != index)
        {
            currentChannels[samePageIndex] = oldChannel;
        }

        // 3. Neuen Regler in aktuellen Slot eintragen
        currentChannels[index] = channelId;

        if (ActiveMixerPage == 1)
        {
            settings.MixerPage1Channels = currentChannels;
            settings.MixerPage2Channels = otherChannels;
        }
        else
        {
            settings.MixerPage1Channels = otherChannels;
            settings.MixerPage2Channels = currentChannels;
        }

        settings.EnsureDistinctMixerPages();
        settings.Save();
        HasPendingChanges = true;
    }
}

/// <summary>Output (bus) with a localized display name and an icon.</summary>
public sealed class BusView(uint id, string fallbackName)
{
    public uint Id { get; } = id;
    public string Name { get; } = Localize(id, fallbackName);
    public string Glyph { get; } = id switch
    {
        0 => "\uE7F6",   // headphones
        888 => "\uE702", // wireless / bluetooth headphones
        1 => "\uE774",   // broadcast
        2 => "\uE7F5",   // line out (speakers)
        3 => "\uE8BD",   // chat mic
        4 => "\uE7C8",   // sampler
        _ => "\uE767",
    };

    public static string Localize(uint id, string fallback)
    {
        if (id == 888) return "Externer Kopfhörer";
        var s = NextXLR.Services.Loc.Get($"Bus{id}");
        return s == $"Bus{id}" ? fallback : s;
    }
}
