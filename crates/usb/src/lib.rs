//! GoXLR hardware layer.
//!
//! Protocol reference: the MIT-licensed goxlr-utility project
//! (<https://github.com/GoXLR-on-Linux/goxlr-utility>). On Windows the device is **not** HID:
//! control goes through the TC-Helicon (Thesycon TUSBAUDIO) driver API,
//! `goxlr_audioapi_x64.dll` → `TUSBAUDIO_ClassVendorRequestOut/In`, and input changes are
//! signalled via `TUSBAUDIO_RegisterDeviceNotification` (a Win32 event fires, then the button /
//! fader state is read back). See docs/ARCHITECTURE.md §4.

pub mod goxlr;
mod sim;
mod thread;

pub use sim::{SimDevice, SimHandle};
pub use thread::{spawn_hardware_thread, HardwareThread};

use mixforge_protocol::{Capabilities, DeviceKind, HardwareInfo};

pub const VID_TC_HELICON: u16 = 0x1220;
pub const PID_GOXLR_FULL: u16 = 0x8fe0;
pub const PID_GOXLR_MINI: u16 = 0x8fe4;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Model {
    Full,
    Mini,
}

impl Model {
    pub fn from_pid(pid: u16) -> Option<Self> {
        match pid {
            PID_GOXLR_FULL => Some(Self::Full),
            PID_GOXLR_MINI => Some(Self::Mini),
            _ => None,
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Self::Full => "GoXLR",
            Self::Mini => "GoXLR Mini",
        }
    }

    pub fn kind(self) -> DeviceKind {
        match self {
            Self::Full => DeviceKind::Full,
            Self::Mini => DeviceKind::Mini,
        }
    }

    /// Hardware feature set. The UI is driven entirely by this — never by the model name.
    pub fn capabilities(self) -> Capabilities {
        let common = [
            "fader_a", "fader_b", "fader_c", "fader_d",
            "mute_a", "mute_b", "mute_c", "mute_d",
            "cough", "bleep", "global", "accent",
        ];
        match self {
            Self::Full => Capabilities {
                effects: true,
                sampler: true,
                faders: 4,
                eq_bands: vec![31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000],
                motorized_faders: true,
                lighting_zones: common
                    .iter()
                    .chain(&[
                        "effect_presets", "effect_megaphone", "effect_robot", "effect_hardtune",
                        "effect_fx", "encoders", "sampler_banks", "sampler_pads",
                    ])
                    .map(|s| s.to_string())
                    .collect(),
            },
            Self::Mini => Capabilities {
                effects: false,
                sampler: false,
                faders: 4,
                eq_bands: vec![90, 250, 500, 1000, 3000, 8000],
                motorized_faders: false,
                lighting_zones: common.iter().map(|s| s.to_string()).collect(),
            },
        }
    }

    pub fn info(self, serial: Option<String>) -> HardwareInfo {
        HardwareInfo {
            model: self.name().to_string(),
            serial,
            kind: self.kind(),
            capabilities: self.capabilities(),
        }
    }
}

/// Lists attached GoXLR devices by USB product id (0x8FE0 full / 0x8FE4 Mini).
/// Discovery only reads descriptors — it never claims the device.
pub fn discover() -> Vec<HardwareInfo> {
    let devices = match nusb::list_devices() {
        Ok(d) => d,
        Err(e) => {
            tracing::warn!("USB enumeration failed: {e}");
            return Vec::new();
        }
    };
    devices
        .filter(|d| d.vendor_id() == VID_TC_HELICON)
        .filter_map(|d| {
            let model = Model::from_pid(d.product_id())?;
            Some(model.info(d.serial_number().map(str::to_string)))
        })
        .collect()
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Fader {
    A,
    B,
    C,
    D,
}

impl Fader {
    pub const ALL: [Fader; 4] = [Fader::A, Fader::B, Fader::C, Fader::D];

    pub fn index(self) -> usize {
        self as usize
    }
}

/// Events coming *from* the hardware.
#[derive(Debug, Clone, PartialEq)]
pub enum HardwareEvent {
    /// Absolute physical position 0.0..=1.0.
    FaderMoved { fader: Fader, position: f32 },
    MuteButton { fader: Fader, pressed: bool },
    Button { id: String, pressed: bool },
    EncoderTurned { id: String, delta: i32 },
    /// Microphone input level, linear peak (0..=1). The GoXLR reports this on request only.
    MicLevel(f32),
    Disconnected,
}

/// Commands going *to* the hardware.
#[derive(Debug, Clone, PartialEq)]
pub enum HardwareCommand {
    /// Fader scribble strip / LED bar: show the channel's *software* value (faders aren't motorised).
    FaderDisplay { fader: Fader, position: f32, label: String },
    MuteLed { fader: Fader, on: bool },
    /// GoXLR hardware mode: set the device's internal channel volume (0..=1).
    ChannelVolume { channel: u32, volume: f32 },
    /// GoXLR hardware mode: mute/unmute a channel in the device's mixer. The device's mute
    /// buttons do NOT mute on their own — the software decides and sends this.
    ChannelMute { channel: u32, muted: bool },
    /// Complete lighting scheme: (zone id, active colour, inactive colour). The device has no
    /// partial update, so the full colour map is rebuilt and sent each time.
    ColourMap(Vec<(String, goxlr::lighting::Rgb, goxlr::lighting::Rgb)>),
    /// GoXLR hardware routing: the complete set of outputs one input goes to.
    Routing { channel: u32, buses: Vec<u32> },
    /// Fader LEDs: gradient (top → bottom colour) or solid top colour.
    FaderGradient { fader: Fader, gradient: bool },
    /// Make a physical fader control another channel (Mixforge channel id).
    AssignFader { fader: Fader, channel: u32 },
    /// Light a global button: "bleep" or "cough".
    ButtonLed { button: String, on: bool },
    /// Voice effects (full GoXLR): DSP parameters + effect/preset button LEDs. With
    /// `sync_encoders` the four knobs are moved to the given positions too (not needed when the
    /// change came from turning the knob itself).
    Effects { settings: goxlr::effects::EffectSettings, preset: u8, sync_encoders: bool },
    /// Microphone processing: gate, compressor, EQ (full GoXLR), de-esser.
    MicDsp(mixforge_protocol::MicSettings),
    /// Mute the mic at its input (also silences the Mic Monitor in the headphones).
    MicInputMute(bool),
    /// Scribble strip above a fader (full GoXLR): label + optional Segoe Fluent icon glyph.
    Scribble { fader: Fader, text: String, glyph: Option<char> },
    /// Standby (PC shutdown / suspend / app exit): all lighting, button LEDs and scribble
    /// strips go dark. State changes received while asleep are remembered, not shown.
    Sleep,
    /// Leave standby: restore the remembered colour map, button LEDs and scribble strips.
    Wake,
}

/// Anything that can act as a control surface: GoXLR, GoXLR Mini, MIDI controller, or a
/// simulated device. Owned by exactly one hardware thread, so it only needs `Send`.
pub trait HardwareDevice: Send {
    fn info(&self) -> HardwareInfo;
    /// Block for at most `timeout` waiting for input, then return what changed.
    /// The GoXLR driver signals a Win32 event on change, so this is not busy polling.
    fn wait_events(&mut self, timeout: std::time::Duration) -> anyhow::Result<Vec<HardwareEvent>>;
    fn apply(&mut self, cmd: &HardwareCommand) -> anyhow::Result<()>;
}
