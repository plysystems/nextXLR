//! Lock-free parameter and meter storage shared between the control plane and the RT callbacks.
//!
//! Every value is an `AtomicU32` holding `f32` bits, so audio callbacks can read/write without
//! locks or allocation. Slot index == `ChannelId.0` / `BusId.0`.

use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

pub const MAX_CHANNELS: usize = 64;
pub const MAX_BUSES: usize = 16;

#[derive(Default)]
struct AtomicF32(AtomicU32);

impl AtomicF32 {
    #[inline]
    fn load(&self) -> f32 {
        f32::from_bits(self.0.load(Ordering::Relaxed))
    }
    #[inline]
    fn store(&self, v: f32) {
        self.0.store(v.to_bits(), Ordering::Relaxed)
    }
    #[inline]
    fn fetch_max(&self, v: f32) {
        let mut current = self.0.load(Ordering::Relaxed);
        loop {
            let current_f32 = f32::from_bits(current);
            if current_f32 >= v {
                break;
            }
            match self.0.compare_exchange_weak(
                current,
                v.to_bits(),
                Ordering::Relaxed,
                Ordering::Relaxed,
            ) {
                Ok(_) => break,
                Err(actual) => current = actual,
            }
        }
    }
}

/// Effective channel gain (fader taper × mute) and the routing matrix.
pub struct ParamBank {
    channel_gain: [AtomicF32; MAX_CHANNELS],
    route_gain: [[AtomicF32; MAX_BUSES]; MAX_CHANNELS],
}

impl ParamBank {
    pub fn new() -> Self {
        Self {
            channel_gain: std::array::from_fn(|_| AtomicF32::default()),
            route_gain: std::array::from_fn(|_| std::array::from_fn(|_| AtomicF32::default())),
        }
    }

    #[inline]
    pub fn channel_gain(&self, ch: usize) -> f32 {
        self.channel_gain.get(ch).map_or(0.0, AtomicF32::load)
    }

    pub fn set_channel_gain(&self, ch: usize, gain: f32) {
        if let Some(slot) = self.channel_gain.get(ch) {
            slot.store(gain);
        }
    }

    #[inline]
    pub fn route_gain(&self, ch: usize, bus: usize) -> f32 {
        self.route_gain.get(ch).and_then(|r| r.get(bus)).map_or(0.0, AtomicF32::load)
    }

    pub fn set_route_gain(&self, ch: usize, bus: usize, gain: f32) {
        if let Some(slot) = self.route_gain.get(ch).and_then(|r| r.get(bus)) {
            slot.store(gain);
        }
    }
}

impl Default for ParamBank {
    fn default() -> Self {
        Self::new()
    }
}

impl Default for MeterBank {
    fn default() -> Self {
        Self::new()
    }
}

/// Peak meters, written by the RT thread, read + reset by the control plane.
pub struct MeterBank {
    peaks: [AtomicF32; MAX_CHANNELS],
    /// Slot is fed by an external source (device telemetry); ignore the engine's own estimate.
    external: [AtomicBool; MAX_CHANNELS],
}

impl MeterBank {
    pub fn new() -> Self {
        Self {
            peaks: std::array::from_fn(|_| AtomicF32::default()),
            external: std::array::from_fn(|_| AtomicBool::new(false)),
        }
    }

    /// Meter from an external source (GoXLR mic level, WASAPI endpoint meter). Marks the slot
    /// as externally fed so the engine's own estimate no longer overwrites it.
    pub fn push_external_peak(&self, ch: usize, peak: f32) {
        if let Some(flag) = self.external.get(ch) {
            flag.store(true, Ordering::Relaxed);
            self.push_peak(ch, peak);
        }
    }

    /// RT side: the engine's own estimate, used only while no external source exists.
    #[inline]
    pub fn push_internal_peak(&self, ch: usize, peak: f32) {
        if self.external.get(ch).is_some_and(|f| !f.load(Ordering::Relaxed)) {
            self.push_peak(ch, peak);
        }
    }

    /// RT side: keep the max since the last read.
    #[inline]
    pub fn push_peak(&self, ch: usize, peak: f32) {
        if let Some(slot) = self.peaks.get(ch) {
            slot.fetch_max(peak);
        }
    }

    /// Control side: read and reset. A race with `push_peak` can lose one block's peak,
    /// which is irrelevant for UI metering.
    pub fn take_peak(&self, ch: usize) -> f32 {
        self.peaks.get(ch).map_or(0.0, |slot| {
            let v = slot.load();
            slot.store(0.0);
            v
        })
    }
}

/// Fader position (0..1) → linear gain. Cubic taper ≈ -60 dB at 10%, unity at 100%.
pub fn fader_to_gain(position: f32) -> f32 {
    let p = position.clamp(0.0, 1.0);
    p * p * p
}
