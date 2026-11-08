using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// Effects, synced two-way with the GoXLR: every change goes to the daemon (which sets the DSP
/// parameters, moves the physical knobs and lights the buttons); hardware button presses and knob
/// turns come back through the state broadcast.
/// Knobs use the device's positions: reverb/echo 0..24 (shown as %), pitch/gender -24..24.
/// </summary>
public partial class EffectsViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
    private bool _applyingServer;
    private DateTime _lastKnobEdit = DateTime.MinValue;

    /// <summary>Knob % per device step (24 steps).</summary>
    public const double PercentStep = 100.0 / 24.0;

    [ObservableProperty] private bool _fxEnabled = true;
    [ObservableProperty] private bool _megaphone;
    [ObservableProperty] private bool _robot;
    [ObservableProperty] private bool _hardtune;
    [ObservableProperty] private double _reverb;   // %
    [ObservableProperty] private double _echo;     // %
    [ObservableProperty] private double _pitch;    // -24..24
    [ObservableProperty] private double _gender;   // -24..24

    // ---- Character effect details (active preset) ----
    [ObservableProperty] private double _megaphoneAmount = 50;   // distortion 0..100
    [ObservableProperty] private double _megaphonePostGain;      // -20..20 dB
    [ObservableProperty] private int _robotWaveform;             // 0..3
    [ObservableProperty] private double _robotPulseWidth = 50;   // 0..100
    [ObservableProperty] private double _robotThreshold = -24;   // -36..0 dB
    [ObservableProperty] private double _robotDryMix = -36;      // -36..0 dB
    [ObservableProperty] private double _robotLowGain;           // -12..12 dB
    [ObservableProperty] private double _robotMidGain;
    [ObservableProperty] private double _robotHighGain;
    [ObservableProperty] private double _hardtuneAmount = 70;    // 0..100
    [ObservableProperty] private double _hardtuneRate = 50;      // retune speed 0..100
    [ObservableProperty] private double _hardtuneWindow = 20;    // 0..600

    private static readonly HashSet<string> DetailProps =
    [
        nameof(MegaphoneAmount), nameof(MegaphonePostGain), nameof(RobotWaveform), nameof(RobotPulseWidth),
        nameof(RobotThreshold), nameof(RobotDryMix), nameof(RobotLowGain), nameof(RobotMidGain), nameof(RobotHighGain),
        nameof(HardtuneAmount), nameof(HardtuneRate), nameof(HardtuneWindow),
    ];
    private DateTime _lastDetailEdit = DateTime.MinValue;

    public string[] RobotWaveforms { get; } = [Loc.Get("FxWave0"), Loc.Get("FxWave1"), Loc.Get("FxWave2"), Loc.Get("FxWave3")];

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_applyingServer || e.PropertyName is null || !DetailProps.Contains(e.PropertyName)) return;
        _lastDetailEdit = DateTime.UtcNow;
        _transport.SendCoalesced("fx:details", new SetEffectDetails(BuildDetails()));
    }

    private EffectDetails BuildDetails()
    {
        static int R(double v) => (int)Math.Round(v);
        return new EffectDetails(
            R(MegaphoneAmount), R(MegaphonePostGain),
            RobotWaveform, R(RobotPulseWidth), R(RobotThreshold), R(RobotDryMix),
            R(RobotLowGain), R(RobotMidGain), R(RobotHighGain),
            R(HardtuneAmount), R(HardtuneRate), R(HardtuneWindow));
    }

    private void ApplyDetails(EffectDetails d)
    {
        MegaphoneAmount = d.MegaphoneAmount; MegaphonePostGain = d.MegaphonePostGain;
        RobotWaveform = d.RobotWaveform; RobotPulseWidth = d.RobotPulseWidth;
        RobotThreshold = d.RobotThreshold; RobotDryMix = d.RobotDryMix;
        RobotLowGain = d.RobotLowGain; RobotMidGain = d.RobotMidGain; RobotHighGain = d.RobotHighGain;
        HardtuneAmount = d.HardtuneAmount; HardtuneRate = d.HardtuneRate; HardtuneWindow = d.HardtuneWindow;
    }

    public ObservableCollection<PresetViewModel> Presets { get; }

    public EffectsViewModel(PipeTransport transport)
    {
        _transport = transport;
        Presets = new(Enumerable.Range(1, 6).Select(n => new PresetViewModel(n, this)));
        Presets[0].SetActiveSilently(true);
    }

    public void ApplyFromServer(EffectsState s)
    {
        // Don't fight a knob the user is turning right now (its echo lags behind the pointer).
        var knobsBusy = DateTime.UtcNow - _lastKnobEdit < TimeSpan.FromMilliseconds(400);
        _applyingServer = true;
        try
        {
            FxEnabled = s.Enabled;
            foreach (var p in Presets) p.SetActiveSilently(p.Number - 1 == s.Preset);
            if (s.Presets.ElementAtOrDefault(s.Preset) is { } p0)
            {
                Megaphone = p0.Megaphone;
                Robot = p0.Robot;
                Hardtune = p0.Hardtune;
                if (p0.Details is { } d && DateTime.UtcNow - _lastDetailEdit > TimeSpan.FromMilliseconds(500))
                    ApplyDetails(d);
                if (!knobsBusy)
                {
                    Reverb = p0.Reverb * PercentStep;
                    Echo = p0.Echo * PercentStep;
                    Pitch = p0.Pitch;
                    Gender = p0.Gender;
                }
            }
        }
        finally { _applyingServer = false; }
    }

    internal void Activate(PresetViewModel preset)
    {
        foreach (var p in Presets) p.SetActiveSilently(p == preset);
        if (!_applyingServer) _transport.Send(new SetEffects(Preset: preset.Number - 1));
    }

    private static int Steps(double percent) => (int)Math.Round(percent / PercentStep);

    partial void OnFxEnabledChanged(bool value) { if (!_applyingServer) _transport.Send(new SetEffects(Enabled: value)); }
    partial void OnMegaphoneChanged(bool value) { if (!_applyingServer) _transport.Send(new SetEffects(Megaphone: value)); }
    partial void OnRobotChanged(bool value) { if (!_applyingServer) _transport.Send(new SetEffects(Robot: value)); }
    partial void OnHardtuneChanged(bool value) { if (!_applyingServer) _transport.Send(new SetEffects(Hardtune: value)); }

    partial void OnReverbChanged(double value) => Knob("fx:reverb", new SetEffects(Reverb: Steps(value)));
    partial void OnEchoChanged(double value) => Knob("fx:echo", new SetEffects(Echo: Steps(value)));
    partial void OnPitchChanged(double value) => Knob("fx:pitch", new SetEffects(Pitch: (int)Math.Round(value)));
    partial void OnGenderChanged(double value) => Knob("fx:gender", new SetEffects(Gender: (int)Math.Round(value)));

    private void Knob(string key, SetEffects msg)
    {
        if (_applyingServer) return;
        _lastKnobEdit = DateTime.UtcNow;
        _transport.SendCoalesced(key, msg);   // ~60/s while turning
    }
}

public partial class PresetViewModel(int number, EffectsViewModel owner) : ObservableObject
{
    private bool _silent;
    public int Number { get; } = number;
    public string Label => Number.ToString();

    [ObservableProperty] private bool _isActive;

    partial void OnIsActiveChanged(bool value)
    {
        if (_silent) return;
        // Clicking the active preset keeps it active (a preset is always selected).
        if (!value) { SetActiveSilently(true); return; }
        owner.Activate(this);
    }

    internal void SetActiveSilently(bool active)
    {
        _silent = true;
        IsActive = active;
        _silent = false;
    }
}
