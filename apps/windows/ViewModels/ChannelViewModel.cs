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
    [ObservableProperty] private double? _restoreVolume;
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

    /// <summary>Monitor channel (Id 9) is 50-100% like in the original GoXLR software.</summary>
    public double MinVolumePercent => Id == 9 ? 50.0 : 0.0;
    public double MaxVolumePercent => 100.0;

    /// <summary>
    /// Only Microphone (0), Chat (1), Music (2), Game (3), and System (4) can be customized (name and icon).
    /// Console (7), Line In (6), Samples (5), Headphones (8), Broadcast Stream Mix (11), Line Out (10), Chat Mic (12), Monitor (9) are locked.
    /// </summary>
    public bool CanEdit => Id is 0 or 1 or 2 or 3 or 4;

    public double VolumePercent
    {
        get => Math.Round(Volume * 100, 1);
        set
        {
            var clamped = Math.Clamp(value, MinVolumePercent, MaxVolumePercent);
            Volume = Math.Clamp(clamped / 100.0, MinVolumePercent / 100.0, 1.0);
        }
    }

    public string VolumeLabel => $"{Math.Round(Volume * 100)}";

    /// <summary>Smart mute hint shown while muted: where the fader will return to.</summary>
    public string RestoreLabel => RestoreVolume is { } r ? Loc.Format("MixerRestoreTo", Math.Round(r * 100)) : "";

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            _isDragging = value;
            if (!value && _deferred is { } d)
            {
                _deferred = null;
                ApplyFromServer(d);
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
            RestoreVolume = c.RestoreVolume;
            Metered = c.Metered;
            IsFixed = c.Fixed;
        }
        finally { _applyingServer = false; }
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeLabel));
        if (value <= 0.001)
        {
            _targetDb = FloorDb;
            _displayDb = FloorDb;
            _holdDb = FloorDb;
            Meter = 0;
            PeakHold = 0;
            PeakLabel = "-∞";
        }
        if (!_applyingServer) _transport.SendCoalesced($"vol:{Id}", new SetVolume(Id, value));
    }

    /// <summary>
    /// Smart mute is decided by the daemon (single source of truth, so the hardware mute button
    /// behaves identically): mute drops the fader to 0 and remembers the position; unmute
    /// restores it exactly. The new volume/restore point arrive with the next state broadcast.
    /// </summary>
    partial void OnMutedChanged(bool value)
    {
        if (value)
        {
            _targetDb = FloorDb;
            _displayDb = FloorDb;
            _holdDb = FloorDb;
            Meter = 0;
            PeakHold = 0;
            PeakLabel = "-∞";
        }
        if (!_applyingServer) _transport.Send(new SetMute(Id, value));
    }

    partial void OnRestoreVolumeChanged(double? value) => OnPropertyChanged(nameof(RestoreLabel));

    /// <summary>Custom fader label (sent to the daemon, which validates and broadcasts it).</summary>
    public void Rename(string name)
    {
        if (!CanEdit) return;
        name = name.Trim();
        if (name.Length is > 0 and <= 24 && name != Name) _transport.Send(new SetChannelName(Id, name));
    }

    /// <summary>Assign an icon from <see cref="IconCatalog"/>; null resets to the default.</summary>
    public void SetIcon(string? iconId)
    {
        if (!CanEdit) return;
        if (iconId != IconId) _transport.Send(new SetChannelIcon(Id, iconId));
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
            _targetDb = FloorDb;
            return;
        }
        var db = peak > 0 ? 20 * Math.Log10(peak) : FloorDb;
        if (db > _targetDb) _targetDb = db;
    }

    /// <summary>Called every rendered frame: instant attack, linear-in-dB release, peak hold.</summary>
    public void Tick(double dt)
    {
        if (!Metered) return;
        var target = Math.Max(_targetDb, FloorDb);
        _targetDb = FloorDb;

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
