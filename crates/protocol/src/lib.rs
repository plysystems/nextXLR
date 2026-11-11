//! Mixforge WebSocket API v1 — the single wire contract between the daemon and every client
//! (desktop UI, web panel, Stream Deck plugin, scripts).
//!
//! Keep `apps/ui/src/api/types.ts` in sync until TS generation via `ts-rs` lands (Phase 1).

use serde::{Deserialize, Serialize};

pub const API_VERSION: u32 = 1;
pub const DEFAULT_PORT: u16 = 48500;

#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
#[serde(transparent)]
pub struct ChannelId(pub u32);

#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
#[serde(transparent)]
pub struct BusId(pub u32);

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum ChannelKind {
    /// Physical input (GoXLR mic, other interface input).
    Microphone,
    /// Everything not captured by an app rule.
    System,
    /// Analog hardware input (GoXLR Line In / Console).
    LineIn,
    /// Output level (Headphones, Line Out, Mic Monitor): can sit on a fader, never routed.
    Output,
    /// Audio captured from one application (process loopback), e.g. `Discord.exe`.
    Application { process: String },
    /// Sampler / soundboard output.
    Sampler,
    /// Freely user-defined channel.
    Virtual,
}

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct Channel {
    pub id: ChannelId,
    pub name: String,
    #[serde(flatten)]
    pub kind: ChannelKind,
    /// Fader position 0.0..=1.0 (not linear gain — see engine taper).
    pub volume: f32,
    pub muted: bool,
    /// Dual-Mix Submix volume (Headphones / Monitor bus), 0.0..=1.0.
    #[serde(default = "default_submix_volume")]
    pub submix_volume: f32,
    /// Dual-Mix Submix mute for Headphones.
    #[serde(default)]
    pub submix_muted: bool,
    /// Meter-only channel without a level control (Broadcast Stream Mix, Chat Mic: mixes the
    /// GoXLR doesn't expose a volume for).
    #[serde(default)]
    pub fixed: bool,
    /// Whether a level meter exists for this channel. Analog GoXLR inputs (Line In, Console)
    /// have no PC-side endpoint and the device reports no level for them.
    #[serde(default = "default_true")]
    pub metered: bool,
    /// User-chosen icon id (e.g. "headset"); `None` = the default for the channel kind.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub icon: Option<String>,
    /// Smart mute: the fader position to return to on unmute. Set while muted.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub restore_volume: Option<f32>,
}

fn default_true() -> bool {
    true
}

fn default_submix_volume() -> f32 {
    0.8
}

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct Bus {
    pub id: BusId,
    pub name: String,
}

/// One cell of the routing matrix. Absent route == not routed.
#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq)]
pub struct Route {
    pub channel: ChannelId,
    pub bus: BusId,
    pub gain: f32,
}

/// Detected from the USB product id (0x8FE0 = full GoXLR, 0x8FE4 = GoXLR Mini).
#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum DeviceKind {
    Full,
    Mini,
}

/// What the attached device can do. The UI adapts to this; it never hard-codes per model.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct Capabilities {
    pub effects: bool,
    pub sampler: bool,
    pub faders: u8,
    pub eq_bands: Vec<u32>,
    pub motorized_faders: bool,
    /// Lighting zones present on this hardware, in display order.
    pub lighting_zones: Vec<String>,
}

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct HardwareInfo {
    pub model: String,
    pub serial: Option<String>,
    pub kind: DeviceKind,
    pub capabilities: Capabilities,
}

/// Colours of one lighting zone, as `#RRGGBB`. Buttons: active = on, inactive = off.
/// Faders: active = top of the meter gradient, inactive = bottom.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct LightingZone {
    pub zone: String,
    pub active: String,
    pub inactive: String,
    /// Faders only: gradient from bottom (inactive) to top (active); false = solid top colour.
    #[serde(default)]
    pub gradient: bool,
}

/// A saved mixer setup: labels, icons, volumes, mutes, routing and lighting.
/// Exported/imported as JSON; loading one updates every client and the hardware at once.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct Profile {
    /// Format version for forward compatibility.
    pub format: u32,
    pub channels: Vec<ProfileChannel>,
    pub routes: Vec<Route>,
    pub lighting: Vec<LightingZone>,
    #[serde(default)]
    pub faders: Vec<FaderAssignment>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub effects: Option<EffectsState>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mic: Option<MicSettings>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub vst_plugins: Option<Vec<VstPluginSlot>>,
}

pub const PROFILE_FORMAT: u32 = 1;

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct ProfileChannel {
    pub id: ChannelId,
    pub name: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub icon: Option<String>,
    pub volume: f32,
    pub muted: bool,
    #[serde(default = "default_submix_volume")]
    pub submix_volume: f32,
    #[serde(default)]
    pub submix_muted: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub restore_volume: Option<f32>,
}

/// Which channel a physical fader (A–D) controls.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct FaderAssignment {
    /// "A", "B", "C" or "D".
    pub fader: String,
    pub channel: ChannelId,
}

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct VstPluginSlot {
    pub id: String,
    pub name: String,
    pub vendor: String,
    pub format: String,
    pub path: String,
    pub enabled: bool,
    pub bypass: bool,
    pub channel: ChannelId,
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct MixerState {
    pub channels: Vec<Channel>,
    pub buses: Vec<Bus>,
    pub routes: Vec<Route>,
    pub hardware: Vec<HardwareInfo>,
    #[serde(default)]
    pub lighting: Vec<LightingZone>,
    #[serde(default)]
    pub faders: Vec<FaderAssignment>,
    /// Bleep (censor) currently held, on the device or in a client.
    #[serde(default)]
    pub bleep: bool,
    /// Global mic mute (the GoXLR "cough" button), independent of the Mic fader's mute.
    #[serde(default)]
    pub mic_muted: bool,
    #[serde(default)]
    pub effects: EffectsState,
    #[serde(default)]
    pub mic: MicSettings,
    #[serde(default)]
    pub vst_plugins: Vec<VstPluginSlot>,
    #[serde(default)]
    pub fader_target_submix: bool,
}

/// One effect preset (GoXLR buttons 1–6). Knob positions as on the device:
/// reverb/echo 0..=24, pitch/gender -24..=24.
#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq)]
pub struct EffectPreset {
    pub reverb: i8,
    pub echo: i8,
    pub pitch: i8,
    pub gender: i8,
    pub megaphone: bool,
    pub robot: bool,
    pub hardtune: bool,
    #[serde(default)]
    pub details: EffectDetails,
}

/// Detailed settings of the character effects (ranges as on the GoXLR).
#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq)]
#[serde(default)]
pub struct EffectDetails {
    /// Distortion amount 0..=100.
    pub megaphone_amount: u8,
    /// Output gain -20..=20 dB.
    pub megaphone_post_gain: i8,
    /// Synth waveform 0..=3.
    pub robot_waveform: u8,
    /// Synth pulse width 0..=100.
    pub robot_pulse_width: u8,
    /// Vocoder gate threshold -36..=0 dB.
    pub robot_threshold: i8,
    /// Dry (unprocessed) voice -36..=0 dB.
    pub robot_dry_mix: i8,
    /// Vocoder band gains -12..=12 dB.
    pub robot_low_gain: i8,
    pub robot_mid_gain: i8,
    pub robot_high_gain: i8,
    /// Correction strength 0..=100.
    pub hardtune_amount: u8,
    /// Retune speed 0..=100.
    pub hardtune_rate: u8,
    /// Detection window 0..=600.
    pub hardtune_window: u16,
}

impl Default for EffectDetails {
    fn default() -> Self {
        Self {
            megaphone_amount: 50, megaphone_post_gain: 0,
            robot_waveform: 0, robot_pulse_width: 50, robot_threshold: -24, robot_dry_mix: -36,
            robot_low_gain: 0, robot_mid_gain: 0, robot_high_gain: 0,
            hardtune_amount: 70, hardtune_rate: 50, hardtune_window: 20,
        }
    }
}

impl EffectDetails {
    pub fn is_valid(&self) -> bool {
        self.megaphone_amount <= 100 && (-20..=20).contains(&self.megaphone_post_gain)
            && self.robot_waveform <= 3 && self.robot_pulse_width <= 100
            && (-36..=0).contains(&self.robot_threshold) && (-36..=0).contains(&self.robot_dry_mix)
            && [self.robot_low_gain, self.robot_mid_gain, self.robot_high_gain].iter().all(|g| (-12..=12).contains(g))
            && self.hardtune_amount <= 100 && self.hardtune_rate <= 100 && self.hardtune_window <= 600
    }
}

/// Microphone processing (GoXLR DSP). Ranges and step indices as on the device.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
#[serde(default)]
pub struct MicSettings {
    pub gate_enabled: bool,
    /// -59..=0 dB.
    pub gate_threshold: i8,
    /// 0..=100 %.
    pub gate_attenuation: u8,
    /// Step 0..=45.
    pub gate_attack: u8,
    /// Step 0..=45.
    pub gate_release: u8,
    /// -40..=0 dB.
    pub comp_threshold: i8,
    /// Ratio step 0..=14 (1.0 … 64:1).
    pub comp_ratio: u8,
    /// Step 0..=19.
    pub comp_attack: u8,
    /// Step 0..=19.
    pub comp_release: u8,
    /// -6..=24 dB.
    pub comp_makeup: i8,
    /// 10 bands, 31 Hz … 16 kHz, -9..=9 dB.
    pub eq_gains: Vec<i8>,
    /// 0..=100 %.
    pub deesser: u8,
    /// Neural noise reduction (RNNoise / DeepFilterNet) enabled.
    #[serde(default)]
    pub rnnoise_enabled: bool,
    /// Neural noise reduction intensity 0..=100 %.
    #[serde(default = "default_rnnoise_intensity")]
    pub rnnoise_intensity: u8,
    /// Neural noise model ("DeepFilterNet3", "RNNoise", "VoiceIsolation").
    #[serde(default = "default_rnnoise_model")]
    pub rnnoise_model: String,
    /// Neural voice morphing & persona filters enabled.
    #[serde(default)]
    pub voice_morph_enabled: bool,
    /// Voice persona ("Radio", "Helldivers", "Robot", "Alien", "Announcer", "Custom").
    #[serde(default = "default_persona")]
    pub voice_morph_persona: String,
    /// Pitch shift in semitones (-12..=12).
    #[serde(default)]
    pub voice_morph_pitch: i8,
    /// Formant shift (-12..=12).
    #[serde(default)]
    pub voice_morph_formant: i8,
    /// Drive / saturation 0..=100 %.
    #[serde(default)]
    pub voice_morph_drive: u8,
}

fn default_rnnoise_intensity() -> u8 {
    80
}

fn default_rnnoise_model() -> String {
    "DeepFilterNet3".into()
}

fn default_persona() -> String {
    "Radio".into()
}

impl Default for MicSettings {
    fn default() -> Self {
        Self {
            gate_enabled: true, gate_threshold: -40, gate_attenuation: 100, gate_attack: 0, gate_release: 19,
            comp_threshold: -20, comp_ratio: 6, comp_attack: 6, comp_release: 8, comp_makeup: 0,
            eq_gains: vec![0; 10], deesser: 0,
            rnnoise_enabled: false,
            rnnoise_intensity: 80,
            rnnoise_model: "DeepFilterNet3".into(),
            voice_morph_enabled: false,
            voice_morph_persona: "Radio".into(),
            voice_morph_pitch: 0,
            voice_morph_formant: 0,
            voice_morph_drive: 0,
        }
    }
}

impl MicSettings {
    pub fn is_valid(&self) -> bool {
        (-59..=0).contains(&self.gate_threshold) && self.gate_attenuation <= 100
            && self.gate_attack <= 45 && self.gate_release <= 45
            && (-40..=0).contains(&self.comp_threshold) && self.comp_ratio <= 14
            && self.comp_attack <= 19 && self.comp_release <= 19 && (-6..=24).contains(&self.comp_makeup)
            && self.eq_gains.len() == 10 && self.eq_gains.iter().all(|g| (-9..=9).contains(g))
            && self.deesser <= 100
            && self.rnnoise_intensity <= 100
            && (-12..=12).contains(&self.voice_morph_pitch)
            && (-12..=12).contains(&self.voice_morph_formant)
            && self.voice_morph_drive <= 100
    }
}

#[derive(Serialize, Deserialize, Debug, Clone, Default, PartialEq)]
pub struct EffectsState {
    /// The FX button: voice effects on/off.
    pub enabled: bool,
    /// Active preset, 0..=5.
    pub preset: u8,
    pub presets: Vec<EffectPreset>,
}

#[derive(Serialize, Deserialize, Debug, Clone, Copy, PartialEq)]
pub struct ChannelMeter {
    pub channel: ChannelId,
    /// Linear peak 0.0..=1.0+ since the last meter frame.
    pub peak: f32,
}

/// Client → daemon.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ClientMessage {
    GetState,
    SetVolume { channel: ChannelId, volume: f32 },
    SetMute { channel: ChannelId, muted: bool },
    SetSubmixVolume { channel: ChannelId, volume: f32 },
    SetSubmixMute { channel: ChannelId, muted: bool },
    /// Whether hardware faders control the submix (Headphones) or the main stream mix.
    SetFaderTargetSubmix { submix: bool },
    SetVstPlugin { plugin: VstPluginSlot },
    RemoveVstPlugin { id: String },
    ScanVstPlugins,
    /// `gain <= 0` removes the route.
    SetRoute { channel: ChannelId, bus: BusId, gain: f32 },
    /// Custom fader label (1–24 characters after trimming).
    SetChannelName { channel: ChannelId, name: String },
    /// Icon id from the app's icon set; `null` resets to the default.
    SetChannelIcon { channel: ChannelId, icon: Option<String> },
    /// Change one lighting zone; omitted fields stay as they are. Colours `#RRGGBB`.
    SetLighting {
        zone: String,
        active: Option<String>,
        inactive: Option<String>,
        #[serde(default)]
        gradient: Option<bool>,
    },
    /// Apply one colour pair to every zone (lighting themes).
    SetLightingAll { active: String, inactive: String },
    /// Several zones at once, one device update (multi-colour presets such as "GTA VI").
    SetLightingScheme { zones: Vec<LightingZone> },
    /// Put a channel on a physical fader ("A"–"D"). If it is already on another fader, the two swap.
    AssignFader { fader: String, channel: ChannelId },
    /// Bleep (censor) from a client: while pressed, the mic is muted on the device.
    Bleep { pressed: bool },
    /// Global mic mute (same as the GoXLR cough button).
    SetMicMute { muted: bool },
    /// Detailed Megaphone / Robot / Hardtune settings of the active preset (replaces them).
    SetEffectDetails { details: EffectDetails },
    /// LEDs of the sampler buttons (`sampler_select_a`…`sampler_clear`), set by the app's sampler.
    SetButtonLeds { leds: Vec<ButtonLedState> },
    /// Microphone processing (gate, compressor, EQ, de-esser) — replaces the current settings.
    SetMicSettings { settings: MicSettings },
    /// Effects: any subset; knob values and Megaphone/Robot/Hardtune apply to the active preset.
    SetEffects {
        #[serde(default)]
        enabled: Option<bool>,
        #[serde(default)]
        preset: Option<u8>,
        #[serde(default)]
        reverb: Option<i8>,
        #[serde(default)]
        echo: Option<i8>,
        #[serde(default)]
        pitch: Option<i8>,
        #[serde(default)]
        gender: Option<i8>,
        #[serde(default)]
        megaphone: Option<bool>,
        #[serde(default)]
        robot: Option<bool>,
        #[serde(default)]
        hardtune: Option<bool>,
    },
    /// Reply with the current setup as a `profile` message.
    GetProfile,
    /// Replace the current setup with a profile (validated as a whole before anything changes).
    LoadProfile { profile: Profile },
}

/// Daemon → client.
#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ServerMessage {
    Hello { server: String, version: String, api: u32 },
    State(MixerState),
    Meters { levels: Vec<ChannelMeter> },
    Profile { profile: Profile },
    Error { message: String },
    /// A hardware button the daemon doesn't handle itself (the sampler section), forwarded to
    /// clients: press and release, so "hold to play" works.
    Button { id: String, pressed: bool },
}

#[derive(Serialize, Deserialize, Debug, Clone, PartialEq)]
pub struct ButtonLedState {
    pub button: String,
    pub on: bool,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn client_message_wire_format() {
        let m: ClientMessage =
            serde_json::from_str(r#"{"type":"set_volume","channel":2,"volume":0.5}"#).unwrap();
        assert_eq!(m, ClientMessage::SetVolume { channel: ChannelId(2), volume: 0.5 });
    }

    #[test]
    fn channel_kind_is_flattened() {
        let c = Channel {
            id: ChannelId(1),
            name: "Chat".into(),
            kind: ChannelKind::Application { process: "Discord.exe".into() },
            volume: 1.0,
            muted: false,
            submix_volume: 0.8,
            submix_muted: false,
            metered: true,
            icon: None,
            restore_volume: None,
            fixed: false,
        };
        let v = serde_json::to_value(&c).unwrap();
        assert_eq!(v["kind"], "application");
        assert_eq!(v["process"], "Discord.exe");
    }
}
