//! GoXLR hardware routing table (`SetRouting`, command 0x804 | input id).
//!
//! Every input is routed per stereo side: one request for the left input, one for the right.
//! The body is one byte per output slot (22 bytes, or 26 on firmware with "Mix 2": ≥ 1.5.0 Full /
//! ≥ 1.3.0 Mini); a routed output gets 0x20 at its left (or right) slot. Byte values and
//! positions are from goxlr-utility (MIT), `usb/src/routing.rs` + `apply_channel_routing`.
//!
//! Only the toggled input's row is rewritten, so the rest of the device routing is untouched.

const ROUTED: u8 = 0x20;

/// Mixforge channel id → (left, right) GoXLR input ids.
pub fn input_ids(channel: u32) -> Option<(u8, u8)> {
    Some(match channel {
        0 => (0x02, 0x03), // Mic
        1 => (0x0c, 0x0d), // Chat
        2 => (0x0e, 0x0f), // Music
        3 => (0x0a, 0x0b), // Game
        4 => (0x08, 0x09), // System
        5 => (0x10, 0x11), // Samples
        6 => (0x04, 0x05), // Line In
        7 => (0x06, 0x07), // Console
        _ => return None,
    })
}

/// Mixforge bus id → (left, right) slot in the routing row.
fn output_slots(bus: u32) -> Option<(usize, usize)> {
    Some(match bus {
        0 => (1, 3),   // Headphones
        1 => (5, 7),   // Broadcast Mix
        2 => (17, 19), // Line Out
        3 => (9, 11),  // Chat Mic
        4 => (13, 15), // Sampler
        _ => return None,
    })
}

/// Row length for this firmware.
pub fn row_len(mix2: bool) -> usize {
    if mix2 { 26 } else { 22 }
}

/// Firmware with the second stream mix uses the longer row.
pub fn supports_mix2(model: crate::Model, fw: (u32, u32, u32)) -> bool {
    let min = match model {
        crate::Model::Full => (1, 5, 0),
        crate::Model::Mini => (1, 3, 0),
    };
    fw >= min
}

/// (left row, right row) for an input routed to `buses`. Hardtune source bytes stay 0.
pub fn build_rows(buses: &[u32], mix2: bool) -> (Vec<u8>, Vec<u8>) {
    let mut left = vec![0u8; row_len(mix2)];
    let mut right = vec![0u8; row_len(mix2)];
    for &bus in buses {
        if let Some((l, r)) = output_slots(bus) {
            left[l] = ROUTED;
            right[r] = ROUTED;
        }
    }
    (left, right)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rows_mark_left_and_right_slots() {
        let (l, r) = build_rows(&[0, 3], false);
        assert_eq!(l.len(), 22);
        assert_eq!((l[1], l[9]), (0x20, 0x20)); // headphones L, chat mic L
        assert_eq!((r[3], r[11]), (0x20, 0x20)); // headphones R, chat mic R
        assert_eq!(l.iter().filter(|&&b| b != 0).count(), 2);
        assert_eq!(r.iter().filter(|&&b| b != 0).count(), 2);
    }

    #[test]
    fn unrouted_input_clears_row_and_mix2_length() {
        let (l, r) = build_rows(&[], true);
        assert_eq!((l.len(), r.len()), (26, 26));
        assert!(l.iter().chain(&r).all(|&b| b == 0));
    }

    #[test]
    fn ids_and_firmware() {
        assert_eq!(input_ids(0), Some((0x02, 0x03)));
        assert_eq!(input_ids(9), None);
        assert!(!supports_mix2(crate::Model::Full, (1, 4, 3)));
        assert!(supports_mix2(crate::Model::Full, (1, 5, 0)));
    }
}
