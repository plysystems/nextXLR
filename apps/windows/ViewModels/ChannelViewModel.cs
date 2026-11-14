using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NextXLR.Protocol;
using NextXLR.Services;
using Windows.UI;

namespace NextXLR.ViewModels;

/// <summary>
/// One channel strip. The daemon owns the truth: incoming state is applied with
/// <see cref="ApplyFromServer"/>; user edits go out as intents. Two rules keep it in sync:
/// 1. values that came FROM the server are never echoed back (<c>_applyingServer</c>);
/// 2. while the user is dragging, server updates are deferred so they can't yank the thumb.
/// Meters are telemetry, not state: <see cref="PushPeak"/> records targets at the daemon's
/// ~30 Hz and <see cref="Tick"/> animates them at display rate with PPM-style ballistics.
/// </summary>
public partial class ChannelViewModel : ObservableObject
{
    private const double FloorDb = -60;
    private const double ReleaseDbPerSecond = 24;   // fall rate after a peak
    private const double HoldSeconds = 1.2;          // peak-hold marker

    private readonly PipeTransport _transport;
    private bool _applyingServer;
    private Channel? _deferred;
    private bool _isDragging;

    private double _targetDb = FloorDb;     // max peak since the last frame
    private double _displayDb = FloorDb;
    private double _holdDb = FloorDb;
    private double _holdAge;

    public uint Id { get; }
    /// <summary>The fader's lighting colour when on a hardware fader, otherwise neutral.</summary>
    [ObservableProperty] private SolidColorBrush _accent = new(Color.FromArgb(255, 0x5E, 0x66, 0x75));
    /// <summary>Hardware fader letter (A–D) this channel is assigned to, if any.</summary>
    [ObservableProperty] private string? _faderLetter;
    public bool HasFader => FaderLetter is not null;
    partial void OnFaderLetterChanged(string? value) => OnPropertyChanged(nameof(HasFader));

    /// <summary>Inputs are routable; outputs (Headphones, Line Out, Mic Monitor) only have a level.</summary>
    public bool IsOutput => Kind == "output";
    public string Kind { get; private set; } = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string? _iconId;
    [ObservableProperty] private string _glyph = "";
    [ObservableProperty] private double _volume;      // 0..1 fader position
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _submixVolume = 0.8; // Dual-mix submix fader
    [ObservableProperty] private bool _submixMuted;
    [ObservableProperty] private bool _isSubmixActive;
    [ObservableProperty] private bool _showDualMix = true;
    [ObservableProperty] private bool _dualFaderIsKopfhoerer = true;
    [ObservableProperty] private double _stripWidth = 110;

    partial void OnDualFaderIsKopfhoererChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFaderOnStream));
        OnPropertyChanged(nameof(ShowFaderOnPhones));
    }
    partial void OnShowDualMixChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFaderOnStream));
        OnPropertyChanged(nameof(ShowFaderOnPhones));
    }

    public bool ShowFaderOnStream => HasFader && (!ShowDualMix || !DualFaderIsKopfhoerer);
    public bool ShowFaderOnPhones => HasFader && ShowDualMix && DualFaderIsKopfhoerer;
    [ObservableProperty] private double? _restoreVolume;
    [ObservableProperty] private double? _restoreSubmixVolume;
    [ObservableProperty] private bool _metered = true;
    /// <summary>Meter-only (no level control on the GoXLR): Broadcast Stream Mix, Chat Mic.</summary>
    [ObservableProperty] private bool _isFixed;
    [ObservableProperty] private double _meter;       // 0..1 bar height
    [ObservableProperty] private double _peakHold;    // 0..1 hold marker
    [ObservableProperty] private string _peakLabel = "-∞";

    private string _kind = "";

    public ChannelViewModel(Channel c, PipeTransport transport)
    {
        _transport = transport;
        Id = c.Id;
        ApplyFromServer(c);
    }

    /// <summary>Faders can always drop to 0%.</summary>
    public double MinVolumePercent => 0.0;
    public double MaxVolumePercent => 100.0;

    /// <summary>
    /// Inputs can be customized (name and icon):
    /// Microphone (0), Chat (1), Music (2), Game (3), System (4), Samples (5), Line In (6), Console (7).
    /// Outputs (Headphones, Line Out, Mic Monitor, Broadcast Stream Mix, Chat Mic) remain locked.
    /// </summary>
    public bool CanEdit => Id is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7;

    partial void OnIsSubmixActiveChanged(bool value)
    {
        UpdateRouterLevels();
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeLabel));
        OnPropertyChanged(nameof(VolumeDbLabel));
        OnPropertyChanged(nameof(VolumePercentLabel));
        OnPropertyChanged(nameof(ActiveMuted));
        OnPropertyChanged(nameof(ActiveMuteTooltip));
    }

    public double VolumePercent
    {
        get => Math.Round((IsSubmixActive ? SubmixVolume : Volume) * 100, 1);
        set
        {
            var clamped = Math.Clamp(value, MinVolumePercent, MaxVolumePercent);
            var normalized = Math.Clamp(clamped / 100.0, 0.0, 1.0);
            if (IsSubmixActive)
                SubmixVolume = normalized;
            else
                Volume = normalized;
        }
    }

    public static string ToDbString(double volume)
    {
        if (volume >= 0.999) return "0.0 dB";
        if (volume <= 0.001) return "-∞ dB";
        // GoXLR cubic taper: gain = v^3, dB = 60 * log10(v)
        double db = Math.Round(60.0 * Math.Log10(volume), 1);
        if (db >= 0.0) return "0.0 dB";
        return $"{db:0.0} dB";
    }

    public string VolumeLabel => $"{Math.Round((IsSubmixActive ? SubmixVolume : Volume) * 100)}";
    public string VolumeDbLabel => ToDbString(IsSubmixActive ? SubmixVolume : Volume);
    public string VolumePercentLabel => $"{Math.Round((IsSubmixActive ? SubmixVolume : Volume) * 100)}%";

    public double StreamVolumePercent
    {
        get => Math.Round(Volume * 100, 1);
        set => Volume = Math.Clamp(value / 100.0, 0.0, 1.0);
    }
    public string StreamVolumeLabel => $"{Math.Round(Volume * 100)}";
    public string StreamVolumeDbLabel => ToDbString(Volume);
    public string StreamVolumePercentLabel => $"{Math.Round(Volume * 100)}%";

    public double SubmixVolumePercent
    {
        get => Math.Round(SubmixVolume * 100, 1);
        set => SubmixVolume = Math.Clamp(value / 100.0, 0.0, 1.0);
    }
    public string SubmixVolumeLabel => $"{Math.Round(SubmixVolume * 100)}";
    public string SubmixVolumeDbLabel => ToDbString(SubmixVolume);
    public string SubmixVolumePercentLabel => $"{Math.Round(SubmixVolume * 100)}%";

    public bool ActiveMuted
    {
        get => IsSubmixActive ? SubmixMuted : Muted;
        set
        {
            if (IsSubmixActive)
                SubmixMuted = value;
            else
                Muted = value;
        }
    }

    public string ActiveMuteTooltip => IsSubmixActive
        ? (SubmixMuted ? "Kopfhörer-Submix stummschalten (aufheben)" : "Kopfhörer-Submix stummschalten")
        : (Muted ? "Stummschaltung aufheben" : "Stummschalten");

    /// <summary>Smart mute hint shown while muted: where the fader will return to.</summary>
    public string RestoreLabel => RestoreVolume is { } r ? Loc.Format("MixerRestoreTo", Math.Round(r * 100)) : "";

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                if (!value)
                {
                    if (_deferred is { } d)
                    {
                        _deferred = null;
                        ApplyFromServer(d);
                    }
                    MainViewModel.Instance?.OnChannelDragEnded();
                }
            }
        }
    }

    public void ApplyFromServer(Channel c)
    {
        if (IsDragging) { _deferred = c; return; }
        _applyingServer = true;
        try
        {
            _kind = c.Kind;
            Kind = c.Kind;
            Name = c.Name;
            Subtitle = c.Kind switch
            {
                "application" when c.Process is { } p => p,
                "microphone" => "",   // the Mic strip shows just "Mic", no secondary text
                "line_in" => Loc.Get("KindAnalog"),
                "sampler" => Loc.Get("KindSampler"),
                "system" => Loc.Get("KindSystem"),
                _ => Loc.Get("KindVirtual"),
            };
            IconId = c.Icon;
            Glyph = IconCatalog.Glyph(c.Icon ?? IconCatalog.DefaultFor(c.Id, c.Kind));
            if (!CanEdit)
            {
                IconId = null;
                Glyph = IconCatalog.Glyph(IconCatalog.DefaultFor(c.Id, c.Kind));
            }
            Volume = c.Volume;
            Muted = c.Muted;
            SubmixVolume = c.SubmixVolume;
            SubmixMuted = c.SubmixMuted;
            RestoreVolume = c.RestoreVolume;
            RestoreSubmixVolume = c.RestoreSubmixVolume;
            Metered = c.Metered;
            IsFixed = c.Fixed;

            UpdateRouterLevels();
        }
        finally { _applyingServer = false; }
    }

    public void UpdateRouterLevels()
    {
        double v = IsSubmixActive ? SubmixVolume : Volume;
        bool m = IsSubmixActive ? SubmixMuted : Muted;

        if (AutoDuckingService.Instance.IsEnabled &&
            AutoDuckingService.Instance.CurrentAttenuationDb < -0.05 &&
            AutoDuckingService.Instance.TargetChannel.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            double factor = Math.Pow(10.0, AutoDuckingService.Instance.CurrentAttenuationDb / 20.0);
            v *= factor;
        }

        if (Id == 8)
        {
            BluetoothAudioRouter.Instance.Volume = (float)v;
            BluetoothAudioRouter.Instance.IsMuted = m;
        }
        else
        {
            BluetoothAudioRouter.Instance.SetChannelLevelById(Id, (float)v, m);
        }
    }

    partial void OnVolumeChanged(double value)
    {
        UpdateRouterLevels();
        OnPropertyChanged(nameof(StreamVolumePercent));
        OnPropertyChanged(nameof(StreamVolumeLabel));
        OnPropertyChanged(nameof(StreamVolumeDbLabel));
        OnPropertyChanged(nameof(StreamVolumePercentLabel));
        if (!IsSubmixActive)
        {
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(VolumeLabel));
            OnPropertyChanged(nameof(VolumeDbLabel));
            OnPropertyChanged(nameof(VolumePercentLabel));
        }
        if (value <= 0.001)
        {
            _targetDb = FloorDb;
            _displayDb = FloorDb;
            _holdDb = FloorDb;
            Meter = 0;
            PeakHold = 0;
            PeakLabel = "-∞";
        }
        if (value > 0.001 && Muted && !_applyingServer)
        {
            Muted = false;
        }
        if (!_applyingServer)
        {
            MainViewModel.HasPendingChanges = true;
            _transport.SendCoalesced($"vol:{Id}", new SetVolume(Id, value));
        }
    }

    partial void OnSubmixVolumeChanged(double value)
    {
        UpdateRouterLevels();
        OnPropertyChanged(nameof(SubmixVolumePercent));
        OnPropertyChanged(nameof(SubmixVolumeLabel));
        OnPropertyChanged(nameof(SubmixVolumeDbLabel));
        OnPropertyChanged(nameof(SubmixVolumePercentLabel));
        if (IsSubmixActive)
        {
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(VolumeLabel));
            OnPropertyChanged(nameof(VolumeDbLabel));
            OnPropertyChanged(nameof(VolumePercentLabel));
        }
        if (value > 0.001 && SubmixMuted && !_applyingServer)
        {
            SubmixMuted = false;
        }
        if (!_applyingServer)
        {
            MainViewModel.HasPendingChanges = true;
            _transport.SendCoalesced($"subvol:{Id}", new SetSubmixVolume(Id, value));
        }
    }

    partial void OnMutedChanged(bool value)
    {
        UpdateRouterLevels();
        if (!IsSubmixActive)
        {
            OnPropertyChanged(nameof(ActiveMuted));
            OnPropertyChanged(nameof(ActiveMuteTooltip));
        }
        if (value)
        {
            if (Volume > 0.001) RestoreVolume = Volume;
            Volume = 0.0;
            _targetDb = FloorDb;
            _displayDb = FloorDb;
            _holdDb = FloorDb;
            Meter = 0;
            PeakHold = 0;
            PeakLabel = "-∞";
        }
        else
        {
            Volume = RestoreVolume ?? 0.8;
            RestoreVolume = null;
        }
        if (!_applyingServer)
        {
            MainViewModel.HasPendingChanges = true;
            _transport.Send(new SetMute(Id, value));
        }
    }

    partial void OnSubmixMutedChanged(bool value)
    {
        UpdateRouterLevels();
        if (IsSubmixActive)
        {
            OnPropertyChanged(nameof(ActiveMuted));
            OnPropertyChanged(nameof(ActiveMuteTooltip));
        }
        if (value)
        {
            if (SubmixVolume > 0.001) RestoreSubmixVolume = SubmixVolume;
            SubmixVolume = 0.0;
        }
        else
        {
            SubmixVolume = RestoreSubmixVolume ?? 0.8;
            RestoreSubmixVolume = null;
        }
        if (!_applyingServer)
        {
            MainViewModel.HasPendingChanges = true;
            _transport.Send(new SetSubmixMute(Id, value));
        }
    }

    partial void OnRestoreVolumeChanged(double? value) => OnPropertyChanged(nameof(RestoreLabel));

    /// <summary>Custom fader label (sent to the daemon, which validates and broadcasts it).</summary>
    public void Rename(string name)
    {
        if (!CanEdit) return;
        name = name.Trim();
        if (name.Length is > 0 and <= 24 && name != Name)
        {
            Name = name;
            MainViewModel.HasPendingChanges = true;
            _transport.Send(new SetChannelName(Id, name));
        }
    }

    /// <summary>Assign an icon from <see cref="IconCatalog"/>; null resets to the default.</summary>
    public void SetIcon(string? iconId)
    {
        if (!CanEdit) return;
        if (iconId != IconId)
        {
            IconId = iconId;
            Glyph = IconCatalog.Glyph(iconId ?? DefaultIconId);
            MainViewModel.HasPendingChanges = true;
            _transport.Send(new SetChannelIcon(Id, iconId));
        }
    }

    public string DefaultIconId => IconCatalog.DefaultFor(Id, _kind);

    private static readonly string[] DbStringCache = InitDbCache();
    private static string[] InitDbCache()
    {
        var arr = new string[101]; // -90 to +10
        for (int i = 0; i <= 100; i++) arr[i] = (i - 90).ToString();
        return arr;
    }

    private static string FormatDb(double db)
    {
        if (db <= FloorDb + 0.5) return "-∞";
        int round = (int)Math.Round(db);
        int idx = round + 90;
        if (idx >= 0 && idx < DbStringCache.Length) return DbStringCache[idx];
        return round.ToString();
    }

    /// <summary>Daemon meter frame (linear peak). Keeps the max until the next display frame.</summary>
    public void PushPeak(double peak)
    {
        if (Muted || Volume <= 0.001)
        {
            Volatile.Write(ref _targetDb, FloorDb);
            return;
        }
        var db = peak > 0 ? 20 * Math.Log10(peak) : FloorDb;
        if (db > Volatile.Read(ref _targetDb))
        {
            Volatile.Write(ref _targetDb, db);
        }
    }

    /// <summary>Called every rendered frame: instant attack, linear-in-dB release, peak hold.</summary>
    public void Tick(double dt)
    {
        if (!Metered) return;
        var target = Math.Max(Volatile.Read(ref _targetDb), FloorDb);
        Volatile.Write(ref _targetDb, FloorDb);

        // Fast path: idle/silent channel avoids continuous floating point work & allocations
        if (target <= FloorDb && _displayDb <= FloorDb && _holdDb <= FloorDb)
        {
            if (Meter != 0 || PeakHold != 0)
            {
                Meter = 0;
                PeakHold = 0;
                PeakLabel = "-∞";
            }
            return;
        }

        _displayDb = target >= _displayDb ? target : Math.Max(target, _displayDb - ReleaseDbPerSecond * dt);

        if (_displayDb >= _holdDb) { _holdDb = _displayDb; _holdAge = 0; }
        else if ((_holdAge += dt) > HoldSeconds) _holdDb = Math.Max(_displayDb, _holdDb - ReleaseDbPerSecond * 2 * dt);

        var m = ToUnit(_displayDb);
        var h = ToUnit(_holdDb);
        if (Math.Abs(m - Meter) > 0.002) Meter = m;
        if (Math.Abs(h - PeakHold) > 0.002)
        {
            PeakHold = h;
            PeakLabel = FormatDb(_holdDb);
        }
    }

    private static double ToUnit(double db) => Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
}
