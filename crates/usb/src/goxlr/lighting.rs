//! GoXLR colour map: the payload of `SetColourMap` (command 0x803).
//!
//! The map is a flat array of 4-byte colour slots, each stored **BGRA** (blue, green, red,
//! alpha=0). Every hardware target owns 1–3 consecutive slots at a fixed start index. Firmware
//! ≥ 1.3.40 (GoXLR) / ≥ 1.1.8 (Mini) uses a 520-byte layout with extra fader-meter slots; older
//! firmware uses 328 bytes. Indices are from goxlr-utility (MIT), `usb/src/colouring.rs`.
//!
//! The device has no "read colours" command, so the map is always sent in full.

/// 24-bit colour.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Rgb(pub u8, pub u8, pub u8);

impl Rgb {
    /// Parses `#RRGGBB` or `RRGGBB`.
    pub fn parse_hex(s: &str) -> Option<Self> {
        let s = s.strip_prefix('#').unwrap_or(s);
        if s.len() != 6 || !s.is_ascii() {
            return None;
        }
        let v = u32::from_str_radix(s, 16).ok()?;
        Some(Rgb((v >> 16) as u8, (v >> 8) as u8, v as u8))
    }

    pub fn to_hex(self) -> String {
        format!("#{:02X}{:02X}{:02X}", self.0, self.1, self.2)
    }

    fn bgra(self) -> [u8; 4] {
        [self.2, self.1, self.0, 0]
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MapFormat {
    /// 82 slots (328 bytes): firmware before 1.3.40 (Full) / 1.1.8 (Mini).
    Legacy,
    /// 130 slots (520 bytes).
    V1_3_40,
}

impl MapFormat {
    pub fn byte_len(self) -> usize {
        match self {
            MapFormat::Legacy => 328,
            MapFormat::V1_3_40 => 520,
        }
    }

    pub fn for_firmware(model: crate::Model, (major, minor, patch): (u32, u32, u32)) -> Self {
        let min = match model {
            crate::Model::Full => (1, 3, 40),
            crate::Model::Mini => (1, 1, 8),
        };
        if (major, minor, patch) >= min { MapFormat::V1_3_40 } else { MapFormat::Legacy }
    }
}

/// A lit element on the device.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Target {
    FaderMute(u8), // 0..=3
    Bleep,
    Cough,
    EffectSelect(u8), // 0..=5
    EffectFx,
    Megaphone,
    Robot,
    Hardtune,
    SamplerBank(u8), // 0..=2
    SamplerPad(u8),  // 0=TL 1=TR 2=BR 3=BL
    SamplerClear,
    FadeMeter(u8), // 0..=3 (colour 0 = top, 1 = bottom of the gradient)
    Scribble(u8),  // 0..=3
    Encoder(u8),   // 0 pitch, 1 gender, 2 reverb, 3 echo (3 colours)
    Accent,        // "LogoX"
    Global,        // "InternalLight"
}

impl Target {
    fn start(self, f: MapFormat) -> usize {
        let new = f == MapFormat::V1_3_40;
        // Targets after the fader meters shift by +48 slots in the new layout.
        let shifted = |legacy: usize| if new { legacy + 48 } else { legacy };
        match self {
            Target::Scribble(i) => 2 * i as usize,
            Target::Global => 8,
            Target::Accent => 10,
            Target::FaderMute(i) => 12 + 2 * i as usize,
            Target::FadeMeter(i) => if new { 20 + 14 * i as usize } else { 20 + 2 * i as usize },
            Target::EffectSelect(i) => shifted(29 + 2 * i as usize),
            Target::Encoder(i) => shifted(41 + 3 * i as usize),
            Target::SamplerBank(i) => shifted(54 + 2 * i as usize),
            Target::SamplerClear => shifted(60),
            Target::SamplerPad(i) => shifted(62 + 2 * i as usize),
            Target::Megaphone => shifted(70),
            Target::Robot => shifted(72),
            Target::Hardtune => shifted(74),
            Target::EffectFx => shifted(76),
            Target::Bleep => shifted(78),
            Target::Cough => shifted(80),
        }
    }

    fn colour_count(self) -> usize {
        match self {
            Target::Scribble(_) => 1,
            Target::Encoder(_) => 3,
            _ => 2,
        }
    }

    /// Byte offset of colour `n`. Encoders store their first two colours swapped (1, 0, 2).
    fn offset(self, n: usize, f: MapFormat) -> usize {
        let slot = match (self, n) {
            (Target::Encoder(_), 0) => 1,
            (Target::Encoder(_), 1) => 0,
            _ => n,
        };
        (self.start(f) + slot) * 4
    }
}

/// UI lighting zone → device targets. Zone ids match `Model::capabilities().lighting_zones`.
pub fn zone_targets(zone: &str) -> Vec<Target> {
    let idx = |s: &str| s.as_bytes()[s.len() - 1] - b'a';
    match zone {
        "fader_a" | "fader_b" | "fader_c" | "fader_d" => vec![Target::FadeMeter(idx(zone)), Target::Scribble(idx(zone))],
        "mute_a" | "mute_b" | "mute_c" | "mute_d" => vec![Target::FaderMute(idx(zone))],
        "cough" => vec![Target::Cough],
        "bleep" => vec![Target::Bleep],
        "global" => vec![Target::Global],
        "accent" => vec![Target::Accent],
        "effect_presets" => (0..6).map(Target::EffectSelect).collect(),
        "effect_megaphone" => vec![Target::Megaphone],
        "effect_robot" => vec![Target::Robot],
        "effect_hardtune" => vec![Target::Hardtune],
        "effect_fx" => vec![Target::EffectFx],
        "encoders" => (0..4).map(Target::Encoder).collect(),
        "sampler_banks" => (0..3).map(Target::SamplerBank).collect(),
        "sampler_pads" => (0..4).map(Target::SamplerPad).chain([Target::SamplerClear]).collect(),
        _ => Vec::new(),
    }
}

/// Builds the complete colour map. `zones` = (zone id, active colour, inactive colour).
/// Buttons: active = colour 1 ("on"), inactive = colour 2. Fader meters: active = top,
/// inactive = bottom. Encoders: active = left side + knob, inactive = right side.
/// Scribble strips use the active colour.
pub fn build_colour_map(zones: &[(String, Rgb, Rgb)], format: MapFormat) -> Vec<u8> {
    let mut map = vec![0u8; format.byte_len()];
    for (zone, active, inactive) in zones {
        for target in zone_targets(zone) {
            for n in 0..target.colour_count() {
                let colour = match (target, n) {
                    (Target::Encoder(_), 1) => *inactive,
                    (Target::Encoder(_), _) | (_, 0) => *active,
                    _ => *inactive,
                };
                let at = target.offset(n, format);
                if at + 4 <= map.len() {
                    map[at..at + 4].copy_from_slice(&colour.bgra());
                }
            }
        }
    }
    map
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hex_round_trip() {
        assert_eq!(Rgb::parse_hex("#1fc79a"), Some(Rgb(0x1F, 0xC7, 0x9A)));
        assert_eq!(Rgb(0x1F, 0xC7, 0x9A).to_hex(), "#1FC79A");
        assert_eq!(Rgb::parse_hex("12345"), None);
        assert_eq!(Rgb::parse_hex("#GGGGGG"), None);
    }

    #[test]
    fn map_sizes() {
        assert_eq!(build_colour_map(&[], MapFormat::Legacy).len(), 328);
        assert_eq!(build_colour_map(&[], MapFormat::V1_3_40).len(), 520);
    }

    #[test]
    fn offsets_match_goxlr_utility() {
        // Spot checks against the reference tables (start index × 4).
        assert_eq!(Target::Bleep.offset(0, MapFormat::Legacy), 78 * 4);
        assert_eq!(Target::Bleep.offset(0, MapFormat::V1_3_40), 126 * 4);
        assert_eq!(Target::FadeMeter(3).offset(0, MapFormat::V1_3_40), 62 * 4);
        assert_eq!(Target::SamplerPad(3).offset(0, MapFormat::Legacy), 68 * 4); // bottom-left
        assert_eq!(Target::Encoder(1).offset(0, MapFormat::Legacy), 44 * 4 + 4); // swapped
        assert_eq!(Target::Encoder(1).offset(1, MapFormat::Legacy), 44 * 4);
        assert_eq!(Target::Cough.offset(1, MapFormat::V1_3_40), 129 * 4);
    }

    #[test]
    fn colours_are_written_bgra() {
        let map = build_colour_map(
            &[("mute_b".into(), Rgb(0x11, 0x22, 0x33), Rgb(0x44, 0x55, 0x66))],
            MapFormat::V1_3_40,
        );
        let at = 14 * 4; // Fader2Mute
        assert_eq!(&map[at..at + 8], &[0x33, 0x22, 0x11, 0, 0x66, 0x55, 0x44, 0]);
    }

    #[test]
    fn firmware_selects_format() {
        assert_eq!(MapFormat::for_firmware(crate::Model::Full, (1, 3, 40)), MapFormat::V1_3_40);
        assert_eq!(MapFormat::for_firmware(crate::Model::Full, (1, 3, 39)), MapFormat::Legacy);
        assert_eq!(MapFormat::for_firmware(crate::Model::Mini, (1, 1, 8)), MapFormat::V1_3_40);
    }
}
