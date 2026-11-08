using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NextXLR.Protocol;
using NextXLR.Services;

namespace NextXLR.ViewModels;

/// <summary>
/// Mic processing, synced with the GoXLR DSP: noise gate, compressor, 10-band EQ and de-esser go
/// to the daemon (<c>set_mic_settings</c>), which pushes them to the device and broadcasts them.
/// Values are in device units; attack/release/ratio are step indices shown with their real values.
/// Mic type and gain stay local for now (their USB encoding isn't verified yet).
/// </summary>
public partial class MicSettingsViewModel : ObservableObject
{
    private readonly PipeTransport _transport;
    private bool _applyingServer;
    private DateTime _lastEdit = DateTime.MinValue;

    // Mic type & gain (local)
    [ObservableProperty] private int _micType;            // 0 Dynamic, 1 Condenser (+48V), 2 Jack
    [ObservableProperty] private double _gain = 40;       // dB
    [ObservableProperty] private bool _noiseSuppression;  // RNNoise (Phase 2)

    // Noise gate
    [ObservableProperty] private bool _gateEnabled = true;
    [ObservableProperty] private double _gateThreshold = -40;   // -59..0 dB
    [ObservableProperty] private double _gateAttenuation = 100; // %
    [ObservableProperty] private double _gateAttack;            // step 0..45
    [ObservableProperty] private double _gateRelease = 19;      // step 0..45

    // Compressor
    [ObservableProperty] private double _compThreshold = -20;   // -40..0 dB
    [ObservableProperty] private int _compRatio = 6;            // step 0..14
    [ObservableProperty] private double _compAttack = 6;        // step 0..19
    [ObservableProperty] private double _compRelease = 8;       // step 0..19
    [ObservableProperty] private double _compMakeup;            // -6..24 dB

    [ObservableProperty] private double _deEsser;               // %

    // RNNoise / DeepFilterNet Neural Noise Reduction
    [ObservableProperty] private bool _rnnoiseEnabled;
    [ObservableProperty] private double _rnnoiseIntensity = 80; // 0..100%
    [ObservableProperty] private string _rnnoiseModel = "DeepFilterNet3";

    // Neural Voice Morphing & Personas
    [ObservableProperty] private bool _voiceMorphEnabled;
    [ObservableProperty] private string _voiceMorphPersona = "Radio";
    [ObservableProperty] private double _voiceMorphPitch; // -12..12 semitones
    [ObservableProperty] private double _voiceMorphFormant; // -12..12
    [ObservableProperty] private double _voiceMorphDrive; // 0..100%

    public ObservableCollection<EqBandViewModel> EqBands { get; }

    public string[] Ratios { get; } =
        ["1.0:1", "1.1:1", "1.2:1", "1.4:1", "1.6:1", "1.8:1", "2.0:1", "2.5:1", "3.2:1", "4.0:1", "5.6:1", "8:1", "16:1", "32:1", "64:1"];

    private static readonly int[] GateTimes =
        [.. Enumerable.Range(1, 20).Select(i => i * 10), .. Enumerable.Range(1, 16).Select(i => 200 + i * 50),
         .. Enumerable.Range(1, 10).Select(i => 1000 + i * 100)];
    private static readonly int[] CompAttackTimes = [0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 23, 26, 30, 35, 40];
    private static readonly int[] CompReleaseTimes = [0, 15, 25, 35, 45, 55, 65, 75, 85, 100, 115, 140, 170, 230, 340, 680, 1000, 1500, 2000, 3000];

    public string GateAttackLabel => $"{GateTimes[Step(GateAttack, 45)]} ms";
    public string GateReleaseLabel => $"{GateTimes[Step(GateRelease, 45)]} ms";
    public string CompAttackLabel => $"{CompAttackTimes[Step(CompAttack, 19)]} ms";
    public string CompReleaseLabel => $"{CompReleaseTimes[Step(CompRelease, 19)]} ms";

    private static int Step(double v, int max) => Math.Clamp((int)Math.Round(v), 0, max);

    private static readonly HashSet<string> DeviceProps =
    [
        nameof(GateEnabled), nameof(GateThreshold), nameof(GateAttenuation), nameof(GateAttack), nameof(GateRelease),
        nameof(CompThreshold), nameof(CompRatio), nameof(CompAttack), nameof(CompRelease), nameof(CompMakeup),
        nameof(DeEsser),
        nameof(RnnoiseEnabled), nameof(RnnoiseIntensity), nameof(RnnoiseModel),
        nameof(VoiceMorphEnabled), nameof(VoiceMorphPersona), nameof(VoiceMorphPitch), nameof(VoiceMorphFormant), nameof(VoiceMorphDrive),
    ];

    public MicSettingsViewModel(PipeTransport transport)
    {
        _transport = transport;
        EqBands = new(new[] { 31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }
            .Select(f => new EqBandViewModel(f, Changed)));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(GateAttack): OnPropertyChanged(nameof(GateAttackLabel)); break;
            case nameof(GateRelease): OnPropertyChanged(nameof(GateReleaseLabel)); break;
            case nameof(CompAttack): OnPropertyChanged(nameof(CompAttackLabel)); break;
            case nameof(CompRelease): OnPropertyChanged(nameof(CompReleaseLabel)); break;
        }
        if (e.PropertyName is { } n && DeviceProps.Contains(n)) Changed();
    }

    private void Changed()
    {
        if (_applyingServer) return;
        _lastEdit = DateTime.UtcNow;
        _transport.SendCoalesced("mic:dsp", new SetMicSettings(Build()));
    }

    /// <summary>Current settings in the wire format (also what a mic profile stores).</summary>
    public MicSettings Build()
    {
        static int R(double v) => (int)Math.Round(v);
        return new MicSettings(
            GateEnabled, R(GateThreshold), R(GateAttenuation), R(GateAttack), R(GateRelease),
            R(CompThreshold), CompRatio, R(CompAttack), R(CompRelease), R(CompMakeup),
            EqBands.Select(b => R(b.Gain)).ToList(), R(DeEsser),
            RnnoiseEnabled, R(RnnoiseIntensity), RnnoiseModel,
            VoiceMorphEnabled, VoiceMorphPersona, R(VoiceMorphPitch), R(VoiceMorphFormant), R(VoiceMorphDrive));
    }

    public void ApplyFromServer(MicSettings m)
    {
        // Don't fight a slider the user is dragging (its echo lags behind the pointer).
        if (DateTime.UtcNow - _lastEdit < TimeSpan.FromMilliseconds(500)) return;
        _applyingServer = true;
        try
        {
            GateEnabled = m.GateEnabled; GateThreshold = m.GateThreshold; GateAttenuation = m.GateAttenuation;
            GateAttack = m.GateAttack; GateRelease = m.GateRelease;
            CompThreshold = m.CompThreshold; CompRatio = m.CompRatio; CompAttack = m.CompAttack;
            CompRelease = m.CompRelease; CompMakeup = m.CompMakeup;
            DeEsser = m.Deesser;
            RnnoiseEnabled = m.RnnoiseEnabled;
            RnnoiseIntensity = m.RnnoiseIntensity;
            RnnoiseModel = m.RnnoiseModel ?? "DeepFilterNet3";
            VoiceMorphEnabled = m.VoiceMorphEnabled;
            VoiceMorphPersona = m.VoiceMorphPersona ?? "Radio";
            VoiceMorphPitch = m.VoiceMorphPitch;
            VoiceMorphFormant = m.VoiceMorphFormant;
            VoiceMorphDrive = m.VoiceMorphDrive;
            var gains = m.EqGains ?? [];
            for (int i = 0; i < EqBands.Count; i++) EqBands[i].SetSilently(gains.ElementAtOrDefault(i));
        }
        finally { _applyingServer = false; }
    }

    /// <summary>Applies a curated microphone preset for popular studio mics and voice styles.</summary>
    public void ApplyQuickPreset(string preset)
    {
        switch (preset.ToLowerInvariant())
        {
            case "sm7b":
                MicType = 0; Gain = 56;
                GateEnabled = true; GateThreshold = -42; GateAttenuation = 100;
                CompThreshold = -18; CompRatio = 6; CompMakeup = 4;
                DeEsser = 25;
                SetEq([ -2, -1, 0, 1, 0, 1, 3, 4, 3, 2 ]);
                break;
            case "podmic":
                MicType = 0; Gain = 48;
                GateEnabled = true; GateThreshold = -38; GateAttenuation = 100;
                CompThreshold = -16; CompRatio = 5; CompMakeup = 3;
                DeEsser = 30;
                SetEq([ -3, -1, 0, 0, 1, 2, 3, 3, 2, 1 ]);
                break;
            case "condenser":
                MicType = 1; Gain = 36;
                GateEnabled = true; GateThreshold = -45; GateAttenuation = 90;
                CompThreshold = -20; CompRatio = 5; CompMakeup = 2;
                DeEsser = 35;
                SetEq([ -2, -2, -1, 0, 0, 1, 2, 2, 2, 1 ]);
                break;
            case "broadcast":
                Gain = Math.Max(Gain, 44);
                GateEnabled = true; GateThreshold = -40; GateAttenuation = 100;
                CompThreshold = -15; CompRatio = 7; CompMakeup = 4;
                DeEsser = 28;
                SetEq([ 3, 2, 1, 0, -1, 1, 3, 4, 3, 1 ]);
                break;
            case "neutral":
                GateEnabled = true; GateThreshold = -45; GateAttenuation = 80;
                CompThreshold = -22; CompRatio = 4; CompMakeup = 0;
                DeEsser = 15;
                SetEq([ 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ]);
                break;
        }
        Changed();
    }

    private void SetEq(int[] gains)
    {
        for (int i = 0; i < EqBands.Count && i < gains.Length; i++)
        {
            EqBands[i].Gain = gains[i];
        }
    }

    /// <summary>Applies a neural voice morphing persona preset.</summary>
    public void ApplyPersonaPreset(string persona)
    {
        VoiceMorphPersona = persona;
        VoiceMorphEnabled = true;
        switch (persona.ToLowerInvariant())
        {
            case "radio":
                VoiceMorphPitch = 0;
                VoiceMorphFormant = -2;
                VoiceMorphDrive = 65;
                break;
            case "helldivers":
                VoiceMorphPitch = -2;
                VoiceMorphFormant = -4;
                VoiceMorphDrive = 85;
                break;
            case "robot":
                VoiceMorphPitch = -4;
                VoiceMorphFormant = 4;
                VoiceMorphDrive = 50;
                break;
            case "alien":
                VoiceMorphPitch = -8;
                VoiceMorphFormant = -6;
                VoiceMorphDrive = 40;
                break;
            case "announcer":
                VoiceMorphPitch = -3;
                VoiceMorphFormant = -2;
                VoiceMorphDrive = 15;
                break;
            case "custom":
                break;
        }
        Changed();
    }

    /// <summary>Applies a mic profile: sends it as one update (the broadcast refreshes the page).</summary>
    public void Load(MicSettings m)
    {
        _lastEdit = DateTime.MinValue;
        _transport.Send(new SetMicSettings(m with { EqGains = m.EqGains ?? Enumerable.Repeat(0, 10).ToList() }));
    }
}
