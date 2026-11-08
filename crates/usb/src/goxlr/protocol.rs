//! Platform-independent GoXLR protocol: command ids, packet framing, button-state parsing.
//! Values come from goxlr-utility (MIT).

use crate::{Fader, HardwareEvent};

/// GoXLR internal channel ids (`goxlr_types::ChannelName` order).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum GoXlrChannel {
    Mic = 0,
    LineIn = 1,
    Console = 2,
    System = 3,
    Game = 4,
    Chat = 5,
    Sample = 6,
    Music = 7,
    Headphones = 8,
    MicMonitor = 9,
    LineOut = 10,
}

/// Mixforge's default channel ids → GoXLR channels (see `mixforge_engine::default_state`).
/// TODO: derive from the channel model (kind + hardware binding) once channels are user-defined.
pub fn goxlr_channel(id: u32) -> Option<GoXlrChannel> {
    Some(match id {
        0 => GoXlrChannel::Mic,
        1 => GoXlrChannel::Chat,
        2 => GoXlrChannel::Music,
        3 => GoXlrChannel::Game,
        4 => GoXlrChannel::System,
        5 => GoXlrChannel::Sample,
        6 => GoXlrChannel::LineIn,
        7 => GoXlrChannel::Console,
        8 => GoXlrChannel::Headphones,
        9 => GoXlrChannel::MicMonitor,
        10 => GoXlrChannel::LineOut,
        _ => return None,
    })
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Command {
    ResetCommandIndex,
    GetButtonStates,
    GetSerialNumber,
    SetFader(Fader),
    SetChannelVolume(GoXlrChannel),
    SetChannelState(GoXlrChannel),
    SetButtonStates,
    GetMicrophoneLevel,
    GetFirmwareVersion,
    SetColourMap,
    /// Routing row for one stereo side of an input (id from `routing::input_ids`).
    SetRouting(u8),
    /// Fader LED mode: body [gradient, meter].
    SetFaderDisplayMode(Fader),
    SetEffectParameters,
    SetEncoderValue(u8),
    SetScribble(Fader),
}

/// `GetFirmwareVersion` response → (major, minor, patch). Packed as major<<12 | minor<<8 | patch.
pub fn parse_firmware(body: &[u8]) -> Option<(u32, u32, u32)> {
    let packed = u32::from_le_bytes(body.get(0..4)?.try_into().ok()?);
    Some((packed >> 12, (packed >> 8) & 0xF, packed & 0xFF))
}

/// Raw `GetMicrophoneLevel` value → linear peak. goxlr-utility's scale: dB = 20·log10(raw) − 72.2.
pub fn mic_level_to_linear(raw: u16) -> f32 {
    const FULL_SCALE: f32 = 4073.8; // 10^(72.2 / 20)
    (raw as f32 / FULL_SCALE).clamp(0.0, 1.0)
}

impl Command {
    pub fn id(self) -> u32 {
        match self {
            Command::ResetCommandIndex => 0,
            Command::GetButtonStates => 0x800 << 12,
            Command::GetSerialNumber => (0x80f << 12) | 1,
            Command::SetFader(f) => (0x805 << 12) | f.index() as u32,
            Command::SetChannelVolume(c) => (0x806 << 12) | c as u32,
            Command::SetChannelState(c) => (0x809 << 12) | c as u32,
            Command::SetButtonStates => 0x808 << 12,
            Command::GetMicrophoneLevel => 0x80c << 12,
            Command::GetFirmwareVersion => 0x80f << 12,
            Command::SetColourMap => 0x803 << 12,
            Command::SetRouting(input) => (0x804 << 12) | input as u32,
            Command::SetFaderDisplayMode(f) => (0x814 << 12) | f.index() as u32,
            Command::SetEffectParameters => 0x801 << 12,
            Command::SetEncoderValue(e) => (0x80a << 12) | e as u32,
            Command::SetScribble(f) => (0x802 << 12) | f.index() as u32,
        }
    }
}

/// 16-byte header: command id (u32 LE), body length (u16 LE), command index (u16 LE), 8 zero bytes.
pub fn encode_request(command: Command, index: u16, body: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(16 + body.len());
    out.extend_from_slice(&command.id().to_le_bytes());
    out.extend_from_slice(&(body.len() as u16).to_le_bytes());
    out.extend_from_slice(&index.to_le_bytes());
    out.extend_from_slice(&[0; 8]);
    out.extend_from_slice(body);
    out
}

/// Returns `(command index, body)` of a response.
pub fn decode_response(raw: &[u8]) -> Option<(u16, &[u8])> {
    if raw.len() < 16 {
        return None;
    }
    let len = u16::from_le_bytes([raw[4], raw[5]]) as usize;
    let index = u16::from_le_bytes([raw[6], raw[7]]);
    let body = &raw[16..];
    Some((index, &body[..len.min(body.len())]))
}

/// Button bit positions in the `GetButtonStates` bitmask.
pub const FADER_MUTE_BITS: [u8; 4] = [4, 9, 14, 19];
pub const BUTTON_COUNT: usize = 24;

pub fn button_name(bit: u8) -> &'static str {
    match bit {
        0 => "effect_select_1",
        5 => "effect_select_2",
        10 => "effect_select_3",
        15 => "effect_select_4",
        1 => "effect_select_5",
        6 => "effect_select_6",
        21 => "effect_fx",
        20 => "effect_megaphone",
        11 => "effect_robot",
        16 => "effect_hardtune",
        2 => "sampler_select_a",
        7 => "sampler_select_b",
        12 => "sampler_select_c",
        3 => "sampler_top_left",
        8 => "sampler_top_right",
        17 => "sampler_bottom_left",
        13 => "sampler_bottom_right",
        18 => "sampler_clear",
        22 => "bleep",
        23 => "cough",
        4 | 9 | 14 | 19 => "fader_mute",
        _ => "unknown",
    }
}

/// Reverse of [`button_name`] (button ids double as LED indices).
pub fn button_bit(name: &str) -> Option<u8> {
    (0..BUTTON_COUNT as u8).find(|&b| button_name(b) == name && name != "fader_mute" && name != "unknown")
}

/// LED / bit index of the effect buttons.
pub const FX_BIT: u8 = 21;
pub const MEGAPHONE_BIT: u8 = 20;
pub const ROBOT_BIT: u8 = 11;
pub const HARDTUNE_BIT: u8 = 16;
/// Effect preset buttons 1–6.
pub const PRESET_BITS: [u8; 6] = [0, 5, 10, 15, 1, 6];

/// Bit / LED index of the Bleep and Cough (mic mute) buttons.
pub const BLEEP_BIT: u8 = 22;
pub const COUGH_BIT: u8 = 23;

pub const ENCODER_NAMES: [&str; 4] = ["pitch", "gender", "reverb", "echo"];

/// LED state byte for `SetButtonStates` (one per button).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ButtonLed {
    Colour2 = 0x00,
    Colour1 = 0x01,
    DimmedColour1 = 0x02,
    Flashing = 0x03,
    DimmedColour2 = 0x04,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct InputState {
    pub pressed: u32,
    pub faders: [u8; 4],
    pub encoders: [i8; 4],
}

impl InputState {
    pub fn parse(body: &[u8]) -> Option<Self> {
        if body.len() < 12 {
            return None;
        }
        Some(Self {
            pressed: u32::from_le_bytes(body[0..4].try_into().ok()?),
            encoders: [body[4] as i8, body[5] as i8, body[6] as i8, body[7] as i8],
            faders: [body[8], body[9], body[10], body[11]],
        })
    }

    /// Turns a state change into events. With no previous state, the current fader positions
    /// are reported so the hub adopts them (hardware is authoritative at connect).
    pub fn diff(&self, prev: Option<&InputState>) -> Vec<HardwareEvent> {
        let mut out = Vec::new();
        for fader in Fader::ALL {
            let i = fader.index();
            if prev.is_none_or(|p| p.faders[i] != self.faders[i]) {
                out.push(HardwareEvent::FaderMoved { fader, position: self.faders[i] as f32 / 255.0 });
            }
        }
        let Some(prev) = prev else { return out };

        let changed = self.pressed ^ prev.pressed;
        for bit in 0..BUTTON_COUNT as u8 {
            if changed & (1 << bit) == 0 {
                continue;
            }
            let pressed = self.pressed & (1 << bit) != 0;
            match FADER_MUTE_BITS.iter().position(|&b| b == bit) {
                Some(f) => out.push(HardwareEvent::MuteButton { fader: Fader::ALL[f], pressed }),
                None => out.push(HardwareEvent::Button { id: button_name(bit).into(), pressed }),
            }
        }
        for (i, name) in ENCODER_NAMES.iter().enumerate() {
            let delta = self.encoders[i] as i32 - prev.encoders[i] as i32;
            if delta != 0 {
                out.push(HardwareEvent::EncoderTurned { id: (*name).into(), delta });
            }
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn request_framing() {
        let r = encode_request(Command::SetChannelVolume(GoXlrChannel::Chat), 7, &[200]);
        assert_eq!(&r[0..4], &((0x806u32 << 12) | 5).to_le_bytes());
        assert_eq!(&r[4..6], &1u16.to_le_bytes());
        assert_eq!(&r[6..8], &7u16.to_le_bytes());
        assert_eq!(r.len(), 17);
        assert_eq!(r[16], 200);
    }

    #[test]
    fn response_decoding() {
        let mut raw = vec![0u8; 16];
        raw[4] = 2;
        raw[6] = 9;
        raw.extend_from_slice(&[0xAA, 0xBB, 0xCC]);
        assert_eq!(decode_response(&raw), Some((9, &[0xAA, 0xBB][..])));
        assert_eq!(decode_response(&[0; 4]), None);
    }

    fn state(pressed: u32, faders: [u8; 4]) -> InputState {
        InputState { pressed, faders, encoders: [0; 4] }
    }

    #[test]
    fn first_read_reports_all_faders() {
        let ev = state(0, [255, 0, 128, 64]).diff(None);
        assert_eq!(ev.len(), 4);
        assert_eq!(ev[0], HardwareEvent::FaderMoved { fader: Fader::A, position: 1.0 });
    }

    #[test]
    fn diff_detects_fader_and_mute_edges() {
        let a = state(0, [10, 10, 10, 10]);
        let b = state(1 << 9, [10, 99, 10, 10]);
        let ev = b.diff(Some(&a));
        assert!(ev.contains(&HardwareEvent::FaderMoved { fader: Fader::B, position: 99.0 / 255.0 }));
        assert!(ev.contains(&HardwareEvent::MuteButton { fader: Fader::B, pressed: true }));
        let ev = a.diff(Some(&b));
        assert!(ev.contains(&HardwareEvent::MuteButton { fader: Fader::B, pressed: false }));
    }

    #[test]
    fn firmware_parse() {
        let packed: u32 = (1 << 12) | (3 << 8) | 40;
        assert_eq!(parse_firmware(&packed.to_le_bytes()), Some((1, 3, 40)));
        assert_eq!(parse_firmware(&[1, 2]), None);
    }

    #[test]
    fn mic_level_scale() {
        assert_eq!(mic_level_to_linear(0), 0.0);
        assert!((mic_level_to_linear(4074) - 1.0).abs() < 0.001);
        // -20 dBFS ≈ 0.1 linear
        assert!((mic_level_to_linear(407) - 0.1).abs() < 0.001);
    }

    #[test]
    fn parse_button_state_body() {
        let body = [0x10, 0, 0, 0, 1, 0, 0, 0xFF, 1, 2, 3, 4];
        let s = InputState::parse(&body).unwrap();
        assert_eq!(s.pressed, 1 << 4);
        assert_eq!(s.encoders[3], -1);
        assert_eq!(s.faders, [1, 2, 3, 4]);
    }
}
