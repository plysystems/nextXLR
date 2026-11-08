//! Voice effects on the GoXLR DSP (full GoXLR only).
//!
//! `SetEffectParameters` (0x801) takes (key u32 LE, value i32 LE) pairs. The encoder knobs are
//! moved with `SetEncoderValue` (0x80a | encoder). Keys and the knob→value formulas are from
//! goxlr-utility (MIT): `types::EffectKey`, `profile/components/{reverb,echo,gender,pitch}.rs`
//! and `mic_profile::get_effect_value`.

pub const REVERB_AMOUNT: u32 = 0x0076;
pub const ECHO_AMOUNT: u32 = 0x0075;
pub const PITCH_AMOUNT: u32 = 0x005d;
pub const GENDER_AMOUNT: u32 = 0x0060;
pub const ROBOT_ENABLED: u32 = 0x014e;
pub const MEGAPHONE_ENABLED: u32 = 0x00d7;
pub const HARDTUNE_ENABLED: u32 = 0x00d8;
pub const ENCODER_ENABLED: [u32; 4] = [0x00d5, 0x00d6, 0x0150, 0x0151];
pub const MEGAPHONE_AMOUNT: u32 = 0x003c;
pub const MEGAPHONE_POST_GAIN: u32 = 0x0040;
pub const ROBOT_WAVEFORM: u32 = 0x0147;
pub const ROBOT_PULSE_WIDTH: u32 = 0x0146;
pub const ROBOT_THRESHOLD: u32 = 0x0157;
pub const ROBOT_DRY_MIX: u32 = 0x014d;
pub const ROBOT_LOW_GAIN: u32 = 0x0134;
pub const ROBOT_MID_GAIN: u32 = 0x013a;
pub const ROBOT_HI_GAIN: u32 = 0x0137;
pub const HARDTUNE_AMOUNT: u32 = 0x005a;
pub const HARDTUNE_RATE: u32 = 0x005c;
pub const HARDTUNE_WINDOW: u32 = 0x005b;

/// Encoder ids for `SetEncoderValue`.
pub const ENC_PITCH: u8 = 0;
pub const ENC_GENDER: u8 = 1;
pub const ENC_REVERB: u8 = 2;
pub const ENC_ECHO: u8 = 3;

/// Knob positions as the device reports them: reverb/echo 0..=24, pitch/gender -24..=24.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct EffectSettings {
    pub enabled: bool,
    pub reverb: i8,
    pub echo: i8,
    pub pitch: i8,
    pub gender: i8,
    pub megaphone: bool,
    pub robot: bool,
    pub hardtune: bool,
    pub details: mixforge_protocol::EffectDetails,
}

/// Reverb/echo amount: knob 0..=24 → -36..=0 (dB-like scale used by the DSP).
pub fn amount_from_knob(knob: i8) -> i32 {
    36 * knob.clamp(0, 24) as i32 / 24 - 36
}

/// Gender, "Medium" style: knob -24..=24 → -25..=25.
pub fn gender_from_knob(knob: i8) -> i32 {
    ((25.0 * knob.clamp(-24, 24) as f32) / 24.0).round() as i32
}

/// Every parameter this module controls, ready for `SetEffectParameters`. The voice effects and
/// all four encoders are only active while FX is on (as on the hardware).
pub fn effect_values(s: &EffectSettings) -> Vec<(u32, i32)> {
    let fx = s.enabled;
    let mut v = vec![
        (REVERB_AMOUNT, amount_from_knob(s.reverb)),
        (ECHO_AMOUNT, amount_from_knob(s.echo)),
        (PITCH_AMOUNT, s.pitch.clamp(-24, 24) as i32),
        (GENDER_AMOUNT, gender_from_knob(s.gender)),
        (MEGAPHONE_ENABLED, (fx && s.megaphone) as i32),
        (ROBOT_ENABLED, (fx && s.robot) as i32),
        (HARDTUNE_ENABLED, (fx && s.hardtune) as i32),
    ];
    v.extend(ENCODER_ENABLED.iter().map(|&k| (k, fx as i32)));
    let d = &s.details;
    v.extend([
        (MEGAPHONE_AMOUNT, d.megaphone_amount as i32),
        (MEGAPHONE_POST_GAIN, d.megaphone_post_gain as i32),
        (ROBOT_WAVEFORM, d.robot_waveform as i32),
        (ROBOT_PULSE_WIDTH, d.robot_pulse_width as i32),
        (ROBOT_THRESHOLD, d.robot_threshold as i32),
        (ROBOT_DRY_MIX, d.robot_dry_mix as i32),
        (ROBOT_LOW_GAIN, d.robot_low_gain as i32),
        (ROBOT_MID_GAIN, d.robot_mid_gain as i32),
        (ROBOT_HI_GAIN, d.robot_high_gain as i32),
        (HARDTUNE_AMOUNT, d.hardtune_amount as i32),
        (HARDTUNE_RATE, d.hardtune_rate as i32),
        (HARDTUNE_WINDOW, d.hardtune_window as i32),
    ]);
    v
}

pub fn encode_params(values: &[(u32, i32)]) -> Vec<u8> {
    let mut out = Vec::with_capacity(values.len() * 8);
    for (k, val) in values {
        out.extend_from_slice(&k.to_le_bytes());
        out.extend_from_slice(&val.to_le_bytes());
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn knob_formulas_match_reference() {
        assert_eq!(amount_from_knob(0), -36);
        assert_eq!(amount_from_knob(24), 0);
        assert_eq!(amount_from_knob(12), -18);
        assert_eq!(gender_from_knob(24), 25);
        assert_eq!(gender_from_knob(-12), -13); // round(-12.5) away from zero
    }

    #[test]
    fn fx_off_disables_voice_effects_and_encoders() {
        let s = EffectSettings { enabled: false, reverb: 10, echo: 5, pitch: 3, gender: -4, megaphone: true, robot: true, hardtune: true, details: Default::default() };
        let v = effect_values(&s);
        for key in [MEGAPHONE_ENABLED, ROBOT_ENABLED, HARDTUNE_ENABLED].iter().chain(&ENCODER_ENABLED) {
            assert_eq!(v.iter().find(|(k, _)| k == key).unwrap().1, 0);
        }
        let on = effect_values(&EffectSettings { enabled: true, ..s });
        assert_eq!(on.iter().find(|(k, _)| *k == ROBOT_ENABLED).unwrap().1, 1);
    }

    #[test]
    fn params_are_key_value_le_pairs() {
        let b = encode_params(&[(0x0076, -36)]);
        assert_eq!(b, [0x76, 0, 0, 0, 0xDC, 0xFF, 0xFF, 0xFF]);
    }
}
