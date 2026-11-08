//! Microphone processing on the GoXLR DSP: noise gate, compressor, 10-band EQ, de-esser, and
//! the input mute. All are `SetEffectParameters` (0x801) keys; ranges and encodings from
//! goxlr-utility (MIT): `types::EffectKey`, `profile/microphone/*`, `mic_profile.rs`.

use mixforge_protocol::MicSettings;

pub const GATE_THRESHOLD: u32 = 0x0011;
pub const GATE_ENABLED: u32 = 0x0014;
pub const GATE_ATTENUATION: u32 = 0x0015;
pub const GATE_ATTACK: u32 = 0x0016;
pub const GATE_RELEASE: u32 = 0x0017;
pub const COMP_RATIO: u32 = 0x013c;
pub const COMP_THRESHOLD: u32 = 0x013d;
pub const COMP_ATTACK: u32 = 0x013e;
pub const COMP_RELEASE: u32 = 0x013f;
pub const COMP_MAKEUP: u32 = 0x0140;
pub const DEESSER: u32 = 0x000b;
/// Mutes the mic at the input — before the mixes *and* the Mic Monitor path to the headphones.
pub const MIC_INPUT_MUTE: u32 = 0x0158;
/// Gain keys for 31 Hz … 16 kHz (full GoXLR EQ).
pub const EQ_GAIN: [u32; 10] = [0x0127, 0x00f9, 0x0114, 0x012a, 0x0117, 0x011e, 0x012d, 0x0121, 0x010a, 0x0130];

/// Attenuation % → dB, the GoXLR table.
const GATE_ATTENUATION_TABLE: [i8; 26] = [
    -6, -7, -8, -9, -10, -11, -12, -13, -14, -15, -16, -17, -18, -19, -20, -21, -22, -23, -24, -25,
    -26, -27, -28, -30, -32, -61,
];

pub fn gate_attenuation_db(percent: u8) -> i32 {
    if percent > 99 {
        return GATE_ATTENUATION_TABLE[25] as i32;
    }
    GATE_ATTENUATION_TABLE[(percent as f32 * 0.24) as usize] as i32
}

/// Compressor ratio labels for the 15 ratio steps.
pub const COMP_RATIOS: [&str; 15] = [
    "1.0", "1.1", "1.2", "1.4", "1.6", "1.8", "2.0", "2.5", "3.2", "4.0", "5.6", "8.0", "16", "32", "64",
];

/// Parameters for `SetEffectParameters`. With the gate switched off, its threshold is opened
/// fully (-59 dB) so everything passes. `with_eq`: the Mini's EQ uses different keys, so it's
/// only sent to the full GoXLR.
pub fn mic_values(m: &MicSettings, with_eq: bool) -> Vec<(u32, i32)> {
    let mut v = vec![
        (GATE_ENABLED, 1),
        (GATE_THRESHOLD, if m.gate_enabled { m.gate_threshold as i32 } else { -59 }),
        (GATE_ATTENUATION, gate_attenuation_db(m.gate_attenuation)),
        (GATE_ATTACK, m.gate_attack as i32),
        (GATE_RELEASE, m.gate_release as i32),
        (COMP_THRESHOLD, m.comp_threshold as i32),
        (COMP_RATIO, m.comp_ratio as i32),
        (COMP_ATTACK, m.comp_attack as i32),
        (COMP_RELEASE, m.comp_release as i32),
        (COMP_MAKEUP, m.comp_makeup as i32),
        (DEESSER, m.deesser as i32),
    ];
    if with_eq {
        v.extend(EQ_GAIN.iter().zip(&m.eq_gains).map(|(&k, &g)| (k, g as i32)));
    }
    v
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn attenuation_table() {
        assert_eq!(gate_attenuation_db(0), -6);
        assert_eq!(gate_attenuation_db(50), -18);
        assert_eq!(gate_attenuation_db(100), -61);
    }

    #[test]
    fn gate_off_opens_threshold_and_eq_is_optional() {
        let mut m = MicSettings { gate_enabled: false, ..Default::default() };
        m.eq_gains[3] = 4;
        let v = mic_values(&m, true);
        assert_eq!(v.iter().find(|(k, _)| *k == GATE_THRESHOLD).unwrap().1, -59);
        assert_eq!(v.iter().find(|(k, _)| *k == EQ_GAIN[3]).unwrap().1, 4);
        assert!(!mic_values(&m, false).iter().any(|(k, _)| EQ_GAIN.contains(k)));
    }
}
