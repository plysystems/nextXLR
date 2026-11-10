// Mirrors crates/protocol/src/lib.rs (API v1). JSON: snake_case, "type"-tagged.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextXLR.Protocol;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // .NET 8 requires the "type" discriminator to be the first property; serde's
        // internally-tagged enums always write it first.
    };
}

public sealed record Channel(
    uint Id, string Name, string Kind, string? Process, double Volume, bool Muted,
    bool Metered = true,
    string? Icon = null,
    /// <summary>Smart mute: position the fader returns to on unmute (set while muted).</summary>
    double? RestoreVolume = null,
    /// <summary>Meter-only channel (Broadcast Stream Mix, Chat Mic): no level, mute or fader.</summary>
    bool Fixed = false,
    /// <summary>Dual-Mix submix volume for Headphones/Monitor (0.0..1.0).</summary>
    double SubmixVolume = 0.8,
    /// <summary>Dual-Mix submix mute for Headphones/Monitor.</summary>
    bool SubmixMuted = false);
public sealed record Bus(uint Id, string Name);
public sealed record Route(uint Channel, uint Bus, double Gain);

public sealed record VstPluginSlot(
    string Id, string Name, string Vendor, string Format, string Path,
    bool Enabled, bool Bypass, uint Channel);

/// <summary>"full" (USB PID 0x8FE0) or "mini" (0x8FE4) — detected by the daemon.</summary>
public sealed record Capabilities(
    bool Effects,
    bool Sampler,
    int Faders,
    IReadOnlyList<int> EqBands,
    bool MotorizedFaders,
    IReadOnlyList<string> LightingZones);

public sealed record HardwareInfo(string Model, string? Serial, string Kind, Capabilities Capabilities);
public sealed record ChannelMeter(uint Channel, double Peak);

// ---- daemon → app --------------------------------------------------------------------------

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(StateMessage), "state")]
[JsonDerivedType(typeof(MetersMessage), "meters")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(ProfileMessage), "profile")]
[JsonDerivedType(typeof(ButtonMessage), "button")]
public abstract record ServerMessage;

public sealed record HelloMessage(string Server, string Version, uint Api) : ServerMessage;

public sealed record StateMessage(
    IReadOnlyList<Channel> Channels,
    IReadOnlyList<Bus> Buses,
    IReadOnlyList<Route> Routes,
    IReadOnlyList<HardwareInfo> Hardware,
    IReadOnlyList<LightingZone>? Lighting = null,
    IReadOnlyList<FaderAssignment>? Faders = null,
    bool Bleep = false,
    bool MicMuted = false,
    EffectsState? Effects = null,
    MicSettings? Mic = null,
    IReadOnlyList<VstPluginSlot>? VstPlugins = null) : ServerMessage;

/// <summary>Knob positions as on the device: reverb/echo 0..24, pitch/gender -24..24.</summary>
public sealed record EffectPreset(int Reverb, int Echo, int Pitch, int Gender, bool Megaphone, bool Robot, bool Hardtune,
    EffectDetails? Details = null);

/// <summary>Megaphone / Robot / Hardtune detail settings (ranges as on the GoXLR).</summary>
public sealed record EffectDetails(
    int MegaphoneAmount = 50, int MegaphonePostGain = 0,
    int RobotWaveform = 0, int RobotPulseWidth = 50, int RobotThreshold = -24, int RobotDryMix = -36,
    int RobotLowGain = 0, int RobotMidGain = 0, int RobotHighGain = 0,
    int HardtuneAmount = 70, int HardtuneRate = 50, int HardtuneWindow = 20);

/// <summary>Mic processing on the GoXLR DSP. Attack/release/ratio are step indices.</summary>
public sealed record MicSettings(
    bool GateEnabled = true, int GateThreshold = -40, int GateAttenuation = 100, int GateAttack = 0, int GateRelease = 19,
    int CompThreshold = -20, int CompRatio = 6, int CompAttack = 6, int CompRelease = 8, int CompMakeup = 0,
    IReadOnlyList<int>? EqGains = null, int Deesser = 0,
    bool RnnoiseEnabled = false, int RnnoiseIntensity = 80, string RnnoiseModel = "DeepFilterNet3",
    bool VoiceMorphEnabled = false, string VoiceMorphPersona = "Radio",
    int VoiceMorphPitch = 0, int VoiceMorphFormant = 0, int VoiceMorphDrive = 0);
public sealed record EffectsState(bool Enabled, int Preset, IReadOnlyList<EffectPreset> Presets);

/// <summary>Which channel a physical fader ("A"–"D") controls.</summary>
public sealed record FaderAssignment(string Fader, uint Channel);

/// <summary>Colours as "#RRGGBB". Buttons: active = on, inactive = off. Faders: top / bottom.</summary>
/// <remarks>Faders: <c>Gradient</c> = bottom (inactive) → top (active); false = solid top colour.</remarks>
public sealed record LightingZone(string Zone, string Active, string Inactive, bool Gradient = false);

public sealed record MetersMessage(IReadOnlyList<ChannelMeter> Levels) : ServerMessage;

public sealed record ErrorMessage(string Message) : ServerMessage;

/// <summary>Hardware button the app handles (sampler section): press and release.</summary>
public sealed record ButtonMessage(string Id, bool Pressed) : ServerMessage;

/// <summary>Reply to <c>get_profile</c>: the complete setup as JSON (kept raw for export).</summary>
public sealed record ProfileMessage(System.Text.Json.Nodes.JsonNode Profile) : ServerMessage;

// ---- app → daemon --------------------------------------------------------------------------

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GetState), "get_state")]
[JsonDerivedType(typeof(SetVolume), "set_volume")]
[JsonDerivedType(typeof(SetMute), "set_mute")]
[JsonDerivedType(typeof(SetRoute), "set_route")]
[JsonDerivedType(typeof(SetChannelName), "set_channel_name")]
[JsonDerivedType(typeof(SetChannelIcon), "set_channel_icon")]
[JsonDerivedType(typeof(SetLighting), "set_lighting")]
[JsonDerivedType(typeof(SetLightingAll), "set_lighting_all")]
[JsonDerivedType(typeof(GetProfile), "get_profile")]
[JsonDerivedType(typeof(SetLightingScheme), "set_lighting_scheme")]
[JsonDerivedType(typeof(AssignFader), "assign_fader")]
[JsonDerivedType(typeof(Bleep), "bleep")]
[JsonDerivedType(typeof(SetMicMute), "set_mic_mute")]
[JsonDerivedType(typeof(SetEffects), "set_effects")]
[JsonDerivedType(typeof(LoadProfile), "load_profile")]
[JsonDerivedType(typeof(SetEffectDetails), "set_effect_details")]
[JsonDerivedType(typeof(SetMicSettings), "set_mic_settings")]
[JsonDerivedType(typeof(SetButtonLeds), "set_button_leds")]
[JsonDerivedType(typeof(SetSubmixVolume), "set_submix_volume")]
[JsonDerivedType(typeof(SetSubmixMute), "set_submix_mute")]
[JsonDerivedType(typeof(SetFaderTargetSubmix), "set_fader_target_submix")]
[JsonDerivedType(typeof(SetVstPlugin), "set_vst_plugin")]
[JsonDerivedType(typeof(RemoveVstPlugin), "remove_vst_plugin")]
[JsonDerivedType(typeof(ScanVstPlugins), "scan_vst_plugins")]
public abstract record ClientMessage;

public sealed record GetState : ClientMessage;
public sealed record SetVolume(uint Channel, double Volume) : ClientMessage;
public sealed record SetMute(uint Channel, bool Muted) : ClientMessage;
public sealed record SetSubmixVolume(uint Channel, double Volume) : ClientMessage;
public sealed record SetSubmixMute(uint Channel, bool Muted) : ClientMessage;
public sealed record SetFaderTargetSubmix(bool Submix) : ClientMessage;
public sealed record SetVstPlugin(VstPluginSlot Plugin) : ClientMessage;
public sealed record RemoveVstPlugin(string Id) : ClientMessage;
public sealed record ScanVstPlugins : ClientMessage;
public sealed record SetRoute(uint Channel, uint Bus, double Gain) : ClientMessage;
public sealed record SetChannelName(uint Channel, string Name) : ClientMessage;
/// <summary>Icon id from <c>IconCatalog</c>; null resets to the channel default.</summary>
public sealed record SetChannelIcon(uint Channel, string? Icon) : ClientMessage;
/// <summary>Change one lighting zone; a null colour is left unchanged.</summary>
public sealed record SetLighting(string Zone, string? Active, string? Inactive, bool? Gradient = null) : ClientMessage;
public sealed record SetLightingAll(string Active, string Inactive) : ClientMessage;
public sealed record GetProfile : ClientMessage;
/// <summary>Several zones in one device update (multi-colour presets like "GTA VI").</summary>
public sealed record SetLightingScheme(IReadOnlyList<LightingZone> Zones) : ClientMessage;
/// <summary>Put a channel on a physical fader; if it's on another fader the two swap.</summary>
public sealed record AssignFader(string Fader, uint Channel) : ClientMessage;
/// <summary>Bleep (censor) held in the app: the mic is muted on the device while pressed.</summary>
public sealed record Bleep(bool Pressed) : ClientMessage;
/// <summary>Global mic mute (the GoXLR cough button).</summary>
public sealed record SetMicMute(bool Muted) : ClientMessage;
/// <summary>Effects: any subset; knobs and character effects apply to the active preset.</summary>
public sealed record SetEffects(
    bool? Enabled = null, int? Preset = null,
    int? Reverb = null, int? Echo = null, int? Pitch = null, int? Gender = null,
    bool? Megaphone = null, bool? Robot = null, bool? Hardtune = null) : ClientMessage;
/// <summary>The profile is passed through as raw JSON so newer fields survive a round trip.</summary>
public sealed record LoadProfile(System.Text.Json.Nodes.JsonNode Profile) : ClientMessage;
/// <summary>Replaces the active preset's Megaphone / Robot / Hardtune details.</summary>
public sealed record SetEffectDetails(EffectDetails Details) : ClientMessage;
/// <summary>Replaces the mic processing (gate, compressor, EQ, de-esser).</summary>
public sealed record SetMicSettings(MicSettings Settings) : ClientMessage;
public sealed record ButtonLedState(string Button, bool On);
/// <summary>Sampler button LEDs (pages, pads, clear) — owned by the app's sampler.</summary>
public sealed record SetButtonLeds(IReadOnlyList<ButtonLedState> Leds) : ClientMessage;
