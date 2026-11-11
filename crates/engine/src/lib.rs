//! Mixforge audio engine.
//!
//! `Engine` is the control-plane handle: it owns the authoritative `MixerState` and mirrors
//! every change into lock-free `ParamBank` atomics read by the real-time callbacks.

mod audio;
#[cfg(windows)]
mod endpoint_meters;
pub mod params;

use std::sync::Arc;

use anyhow::{bail, Result};
use mixforge_protocol::{
    Bus, BusId, Channel, ChannelId, ChannelKind, ChannelMeter, EffectDetails, EffectPreset,
    EffectsState, FaderAssignment, LightingZone, MicSettings, MixerState, Profile, ProfileChannel,
    Route, PROFILE_FORMAT,
};

/// Changes to the effects; `None` fields stay as they are.
#[derive(Debug, Default, Clone, Copy)]
pub struct EffectsChange {
    pub enabled: Option<bool>,
    pub preset: Option<u8>,
    pub reverb: Option<i8>,
    pub echo: Option<i8>,
    pub pitch: Option<i8>,
    pub gender: Option<i8>,
    pub megaphone: Option<bool>,
    pub robot: Option<bool>,
    pub hardtune: Option<bool>,
}

pub const FADER_LETTERS: [&str; 4] = ["A", "B", "C", "D"];

/// Subtle default: a little room ambience and echo, no pitch/gender shift, character effects off.
pub const DEFAULT_PRESET: EffectPreset = EffectPreset {
    reverb: 6, echo: 3, pitch: 0, gender: 0, megaphone: false, robot: false, hardtune: false,
    details: EffectDetails {
        megaphone_amount: 50, megaphone_post_gain: 0,
        robot_waveform: 0, robot_pulse_width: 50, robot_threshold: -24, robot_dry_mix: -36,
        robot_low_gain: 0, robot_mid_gain: 0, robot_high_gain: 0,
        hardtune_amount: 70, hardtune_rate: 50, hardtune_window: 20,
    },
};

/// Input → output pairs the GoXLR doesn't allow (they would feed a signal back into itself).
pub fn is_blocked_route(channel: ChannelId, bus: BusId) -> bool {
    matches!((channel.0, bus.0), (5, 4) | (1, 3)) // Samples→Sampler, Chat→Chat Mic
}

fn is_hex_colour(s: &str) -> bool {
    s.len() == 7 && s.starts_with('#') && s[1..].chars().all(|c| c.is_ascii_hexdigit())
}

/// Every lighting zone of the GoXLR family; the UI shows only those the attached device has.
const LIGHTING_ZONES: [&str; 20] = [
    "fader_a", "fader_b", "fader_c", "fader_d", "mute_a", "mute_b", "mute_c", "mute_d",
    "cough", "bleep", "global", "accent", "effect_presets", "effect_megaphone", "effect_robot",
    "effect_hardtune", "effect_fx", "encoders", "sampler_banks", "sampler_pads",
];

use params::{fader_to_gain, MeterBank, ParamBank, MAX_BUSES, MAX_CHANNELS};

pub struct Engine {
    state: MixerState,
    params: Arc<ParamBank>,
    meters: Arc<MeterBank>,
    _audio: Option<audio::AudioThread>,
    #[cfg(windows)]
    _endpoint_meters: Option<endpoint_meters::EndpointMeters>,
}

impl Engine {
    /// Starts the engine. If no audio device can be opened it keeps running headless,
    /// so the API and UI still work.
    pub fn start() -> Self {
        let mut engine = Self::headless();

        // AudioThread is permanently disabled: GoXLR hardware DSP handles all routing
        // and mixing directly. An obsolete software cpal loopback between default input and
        // default output caused microphone audio to loop directly into wireless headsets (e.g. AirPods)
        // and echo during screen sharing. Endpoint metering is handled below via EndpointMeters.

        #[cfg(windows)]
        {
            use endpoint_meters::{EndpointBinding, EndpointMeters};
            // PC-side channels ← GoXLR playback endpoints (see `default_state` ids).
            let bindings = vec![
                EndpointBinding { slot: 0, prefix: "Chat Mic", capture: true },
                EndpointBinding { slot: 1, prefix: "Chat", capture: false },
                EndpointBinding { slot: 2, prefix: "Music", capture: false },
                EndpointBinding { slot: 3, prefix: "Game", capture: false },
                EndpointBinding { slot: 4, prefix: "System", capture: false },
                EndpointBinding { slot: 5, prefix: "Sample", capture: false },
                // The GoXLR's mixes, as Windows recording devices.
                EndpointBinding { slot: 11, prefix: "Broadcast Stream Mix", capture: true },
                EndpointBinding { slot: 12, prefix: "Chat Mic", capture: true },
            ];
            match EndpointMeters::spawn(engine.meters.clone(), bindings) {
                Ok(m) => engine._endpoint_meters = Some(m),
                Err(e) => tracing::warn!("endpoint meters disabled: {e}"),
            }
        }
        engine
    }

    /// Engine without audio I/O, used by tests and by `MIXFORGE_NO_AUDIO=1`.
    pub fn headless() -> Self {
        let engine = Self {
            state: default_state(),
            params: Arc::new(ParamBank::new()),
            meters: Arc::new(MeterBank::new()),
            _audio: None,
            #[cfg(windows)]
            _endpoint_meters: None,
        };
        engine.sync_all_params();
        engine
    }

    /// Level from an external source (e.g. the GoXLR's own mic level) for a channel.
    pub fn push_meter(&self, id: ChannelId, peak: f32) {
        self.meters.push_external_peak(id.0 as usize, peak);
    }

    /// Disables any background software audio loopback thread (e.g. when hardware GoXLR is attached).
    pub fn disable_audio(&mut self) {
        if self._audio.is_some() {
            tracing::info!("disabling software audio loopback thread");
            self._audio = None;
        }
    }

    /// Effective gain the RT thread currently sees for a channel (test/diagnostic helper).
    pub fn effective_gain(&self, id: ChannelId) -> f32 {
        self.params.channel_gain(id.0 as usize)
    }

    pub fn state(&self) -> &MixerState {
        &self.state
    }

    pub fn state_mut_hardware(&mut self) -> &mut Vec<mixforge_protocol::HardwareInfo> {
        &mut self.state.hardware
    }

    /// Sets the fader position. Raising a muted channel's fader is an explicit "I want to hear
    /// this" — it unmutes and discards the remembered position.
    pub fn set_volume(&mut self, id: ChannelId, volume: f32) -> Result<()> {
        let ch = self.channel_mut(id)?;
        if ch.fixed {
            bail!("{} has no level control", ch.name);
        }
        ch.volume = volume.clamp(0.0, 1.0);
        if ch.muted && ch.volume > 0.0 {
            ch.muted = false;
            ch.restore_volume = None;
        }
        self.sync_channel(id);
        Ok(())
    }

    /// Sets the dual-mix submix volume for Headphones/Monitor bus.
    pub fn set_submix_volume(&mut self, id: ChannelId, volume: f32) -> Result<()> {
        let ch = self.channel_mut(id)?;
        if ch.fixed {
            bail!("{} has no level control", ch.name);
        }
        ch.submix_volume = volume.clamp(0.0, 1.0);
        if ch.submix_muted && ch.submix_volume > 0.0 {
            ch.submix_muted = false;
            ch.restore_submix_volume = None;
        }
        self.sync_channel(id);
        Ok(())
    }

    /// Dual-mix submix mute for Headphones/Monitor bus with smart mute.
    pub fn set_submix_mute(&mut self, id: ChannelId, muted: bool) -> Result<()> {
        let ch = self.channel_mut(id)?;
        if ch.fixed {
            bail!("{} can't be muted here", ch.name);
        }
        if muted && !ch.submix_muted {
            ch.restore_submix_volume = Some(ch.submix_volume);
            ch.submix_volume = 0.0;
        } else if !muted && ch.submix_muted {
            if let Some(v) = ch.restore_submix_volume.take() {
                ch.submix_volume = v;
            } else {
                ch.submix_volume = 0.8;
            }
        }
        ch.submix_muted = muted;
        self.sync_channel(id);
        Ok(())
    }

    pub fn set_vst_plugin(&mut self, plugin: mixforge_protocol::VstPluginSlot) -> Result<()> {
        self.state.vst_plugins.retain(|p| p.id != plugin.id);
        self.state.vst_plugins.push(plugin);
        Ok(())
    }

    pub fn remove_vst_plugin(&mut self, id: &str) -> Result<()> {
        self.state.vst_plugins.retain(|p| p.id != id);
        Ok(())
    }

    /// Smart mute: muting drops the fader to 0 and remembers the position; unmuting returns to
    /// exactly that position. Repeated mute/unmute calls are idempotent.
    pub fn set_mute(&mut self, id: ChannelId, muted: bool) -> Result<()> {
        let ch = self.channel_mut(id)?;
        if ch.fixed {
            bail!("{} can't be muted here", ch.name);
        }
        if muted && !ch.muted {
            ch.restore_volume = Some(ch.volume);
            ch.volume = 0.0;
        } else if !muted && ch.muted {
            if let Some(v) = ch.restore_volume.take() {
                ch.volume = v;
            } else {
                ch.volume = 0.8;
            }
        }
        ch.muted = muted;
        self.sync_channel(id);
        Ok(())
    }

    /// Custom fader label.
    pub fn set_name(&mut self, id: ChannelId, name: &str) -> Result<()> {
        let name = name.trim();
        if name.is_empty() || name.chars().count() > 24 {
            bail!("channel name must be 1–24 characters");
        }
        self.channel_mut(id)?.name = name.to_string();
        Ok(())
    }

    /// Updates a lighting zone. Colours must be `#RRGGBB`.
    pub fn set_lighting(
        &mut self,
        zone: &str,
        active: Option<&str>,
        inactive: Option<&str>,
        gradient: Option<bool>,
    ) -> Result<()> {
        for c in active.iter().chain(inactive.iter()) {
            if !is_hex_colour(c) {
                bail!("invalid colour {c:?} (expected #RRGGBB)");
            }
        }
        let Some(z) = self.state.lighting.iter_mut().find(|z| z.zone == zone) else {
            bail!("unknown lighting zone {zone:?}");
        };
        if let Some(a) = active {
            z.active = a.to_ascii_uppercase();
        }
        if let Some(i) = inactive {
            z.inactive = i.to_ascii_uppercase();
        }
        if let Some(g) = gradient {
            z.gradient = g;
        }
        Ok(())
    }

    pub fn set_fader_target_submix(&mut self, submix: bool) {
        self.state.fader_target_submix = submix;
    }

    pub fn set_active_page(&mut self, page: u8) {
        self.state.active_page = page;
    }

    /// The current setup as a portable profile.
    pub fn profile(&self) -> Profile {
        Profile {
            format: PROFILE_FORMAT,
            channels: self
                .state
                .channels
                .iter()
                .map(|c| ProfileChannel {
                    id: c.id,
                    name: c.name.clone(),
                    icon: c.icon.clone(),
                    volume: c.volume,
                    muted: c.muted,
                    submix_volume: c.submix_volume,
                    submix_muted: c.submix_muted,
                    restore_volume: c.restore_volume,
                })
                .collect(),
            routes: self.state.routes.clone(),
            lighting: self.state.lighting.clone(),
            faders: self.state.faders.clone(),
            effects: Some(self.state.effects.clone()),
            mic: Some(self.state.mic.clone()),
            vst_plugins: Some(self.state.vst_plugins.clone()),
        }
    }

    /// Loads a profile. Everything is validated first; on any error nothing changes.
    /// Unknown channels/zones are ignored (profiles may come from a newer version).
    pub fn load_profile(&mut self, p: &Profile) -> Result<()> {
        if p.format == 0 || p.format > PROFILE_FORMAT {
            bail!("unsupported profile format {}", p.format);
        }
        let mut next = self.state.clone();
        for pc in &p.channels {
            let Some(c) = next.channels.iter_mut().find(|c| c.id == pc.id) else { continue };
            let name = pc.name.trim();
            if name.is_empty() || name.chars().count() > 24 {
                bail!("profile: invalid name for channel {}", pc.id.0);
            }
            if !(0.0..=1.0).contains(&pc.volume) || pc.restore_volume.is_some_and(|v| !(0.0..=1.0).contains(&v)) {
                bail!("profile: volume out of range for channel {}", pc.id.0);
            }
            c.name = name.to_string();
            c.icon = pc.icon.clone().filter(|i| i.len() <= 32 && i.chars().all(|ch| ch.is_ascii_alphanumeric() || ch == '_' || ch == '-'));
            c.volume = pc.volume;
            c.muted = pc.muted;
            c.submix_volume = pc.submix_volume;
            c.submix_muted = pc.submix_muted;
            c.restore_volume = if pc.muted { pc.restore_volume.or(Some(pc.volume.max(0.0))) } else { None };
            if c.muted {
                c.volume = 0.0;
            }
        }
        let mut routes = Vec::new();
        for r in &p.routes {
            let known = next
                .channels
                .iter()
                .any(|c| c.id == r.channel && c.kind != ChannelKind::Output)
                && next.buses.iter().any(|b| b.id == r.bus);
            if known && r.gain > 0.0 && !is_blocked_route(r.channel, r.bus) {
                routes.push(Route { channel: r.channel, bus: r.bus, gain: r.gain.clamp(0.0, 2.0) });
            }
        }
        next.routes = routes;
        for z in &p.lighting {
            if !is_hex_colour(&z.active) || !is_hex_colour(&z.inactive) {
                bail!("profile: invalid colour in zone {}", z.zone);
            }
            if let Some(cur) = next.lighting.iter_mut().find(|c| c.zone == z.zone) {
                cur.active = z.active.to_ascii_uppercase();
                cur.inactive = z.inactive.to_ascii_uppercase();
                cur.gradient = z.gradient;
            }
        }
        if !p.faders.is_empty() {
            let mut seen = Vec::new();
            for f in &p.faders {
                if !FADER_LETTERS.contains(&f.fader.as_str())
                    || !next.channels.iter().any(|c| c.id == f.channel)
                    || seen.contains(&f.channel)
                {
                    bail!("profile: invalid fader assignment {}", f.fader);
                }
                seen.push(f.channel);
            }
            next.faders = p.faders.clone();
        }
        if let Some(fx) = &p.effects {
            let ok = fx.preset <= 5
                && fx.presets.len() == 6
                && fx.presets.iter().all(|q| {
                    (0..=24).contains(&q.reverb) && (0..=24).contains(&q.echo)
                        && (-24..=24).contains(&q.pitch) && (-24..=24).contains(&q.gender)
                        && q.details.is_valid()
                });
            if !ok {
                bail!("profile: invalid effects");
            }
            next.effects = fx.clone();
        }
        if let Some(m) = &p.mic {
            if !m.is_valid() {
                bail!("profile: invalid mic settings");
            }
            next.mic = m.clone();
        }
        if let Some(vst) = &p.vst_plugins {
            next.vst_plugins = vst.clone();
        }
        self.state = next;
        // Reset every route gain, then apply the profile's routes and channel gains.
        for c in 0..self.state.channels.len() {
            for b in 0..self.state.buses.len() {
                self.params.set_route_gain(c, b, 0.0);
            }
        }
        self.sync_all_params();
        Ok(())
    }

    /// Several zones at once (presets). Validated as a whole first.
    pub fn set_lighting_scheme(&mut self, zones: &[LightingZone]) -> Result<()> {
        for z in zones {
            if !is_hex_colour(&z.active) || !is_hex_colour(&z.inactive) {
                bail!("invalid colour in zone {}", z.zone);
            }
        }
        for z in zones {
            if let Some(cur) = self.state.lighting.iter_mut().find(|c| c.zone == z.zone) {
                cur.active = z.active.to_ascii_uppercase();
                cur.inactive = z.inactive.to_ascii_uppercase();
                cur.gradient = z.gradient;
            }
        }
        Ok(())
    }

    /// Channel on physical fader `index` (0 = A).
    pub fn fader_channel(&self, index: usize) -> Option<ChannelId> {
        self.state.faders.iter().find(|f| f.fader == FADER_LETTERS[index]).map(|f| f.channel)
    }

    /// Puts `channel` on fader `index`. If that channel is on another fader, the two swap, so
    /// every fader always has a channel and no channel sits on two faders.
    /// Returns the faders whose channel changed.
    pub fn assign_fader(&mut self, index: usize, channel: ChannelId) -> Result<Vec<usize>> {
        if index >= 4 {
            bail!("unknown fader");
        }
        match self.state.channels.iter().find(|c| c.id == channel) {
            None => bail!("unknown channel {}", channel.0),
            Some(c) if c.fixed => bail!("{} can't be put on a fader", c.name),
            _ => {}
        }
        let previous = self.fader_channel(index);
        if previous == Some(channel) {
            return Ok(Vec::new());
        }
        let mut changed = vec![index];
        if let Some(other) = (0..4).find(|&i| i != index && self.fader_channel(i) == Some(channel)) {
            if let Some(prev) = previous {
                self.set_fader_raw(other, prev);
                changed.push(other);
            }
        }
        self.set_fader_raw(index, channel);
        Ok(changed)
    }

    fn set_fader_raw(&mut self, index: usize, channel: ChannelId) {
        let letter = FADER_LETTERS[index];
        match self.state.faders.iter_mut().find(|f| f.fader == letter) {
            Some(f) => f.channel = channel,
            None => self.state.faders.push(FaderAssignment { fader: letter.into(), channel }),
        }
    }

    pub fn set_bleep(&mut self, on: bool) {
        self.state.bleep = on;
    }

    pub fn set_mic_muted(&mut self, muted: bool) {
        self.state.mic_muted = muted;
    }

    /// Replaces the active preset's Megaphone / Robot / Hardtune details.
    pub fn set_effect_details(&mut self, d: EffectDetails) -> Result<()> {
        if !d.is_valid() {
            bail!("effect detail out of range");
        }
        let fx = &mut self.state.effects;
        let i = fx.preset as usize;
        if let Some(p) = fx.presets.get_mut(i) {
            p.details = d;
        }
        Ok(())
    }

    pub fn set_mic_settings(&mut self, m: MicSettings) -> Result<()> {
        if !m.is_valid() {
            bail!("mic setting out of range");
        }
        self.state.mic = m;
        Ok(())
    }

    pub fn active_preset(&self) -> EffectPreset {
        let fx = &self.state.effects;
        fx.presets.get(fx.preset as usize).copied().unwrap_or(DEFAULT_PRESET)
    }

    /// Applies an effects change (validated first; out-of-range knob values are rejected).
    pub fn set_effects(&mut self, c: EffectsChange) -> Result<()> {
        if c.preset.is_some_and(|p| p > 5) {
            bail!("preset must be 1–6");
        }
        let in_range = |v: Option<i8>, lo: i8, hi: i8| v.is_none_or(|v| (lo..=hi).contains(&v));
        if !in_range(c.reverb, 0, 24) || !in_range(c.echo, 0, 24) || !in_range(c.pitch, -24, 24) || !in_range(c.gender, -24, 24) {
            bail!("effect value out of range");
        }
        let fx = &mut self.state.effects;
        if let Some(e) = c.enabled {
            fx.enabled = e;
        }
        if let Some(p) = c.preset {
            fx.preset = p;
        }
        let i = fx.preset as usize;
        if let Some(p) = fx.presets.get_mut(i) {
            p.reverb = c.reverb.unwrap_or(p.reverb);
            p.echo = c.echo.unwrap_or(p.echo);
            p.pitch = c.pitch.unwrap_or(p.pitch);
            p.gender = c.gender.unwrap_or(p.gender);
            p.megaphone = c.megaphone.unwrap_or(p.megaphone);
            p.robot = c.robot.unwrap_or(p.robot);
            p.hardtune = c.hardtune.unwrap_or(p.hardtune);
        }
        Ok(())
    }

    /// A hardware encoder moved by `delta` steps. Returns false for unknown encoders.
    pub fn turn_encoder(&mut self, id: &str, delta: i32) -> bool {
        let fx = &mut self.state.effects;
        let i = fx.preset as usize;
        let Some(p) = fx.presets.get_mut(i) else { return false };
        let step = |v: i8, lo: i32, hi: i32| (v as i32 + delta).clamp(lo, hi) as i8;
        match id {
            "reverb" => p.reverb = step(p.reverb, 0, 24),
            "echo" => p.echo = step(p.echo, 0, 24),
            "pitch" => p.pitch = step(p.pitch, -24, 24),
            "gender" => p.gender = step(p.gender, -24, 24),
            _ => return false,
        }
        true
    }

    /// Applies one colour pair to every zone.
    pub fn set_lighting_all(&mut self, active: &str, inactive: &str) -> Result<()> {
        if !is_hex_colour(active) || !is_hex_colour(inactive) {
            bail!("invalid colour (expected #RRGGBB)");
        }
        for z in &mut self.state.lighting {
            z.active = active.to_ascii_uppercase();
            z.inactive = inactive.to_ascii_uppercase();
        }
        Ok(())
    }

    /// Icon id chosen in the UI; `None` resets to the default.
    pub fn set_icon(&mut self, id: ChannelId, icon: Option<&str>) -> Result<()> {
        let icon = icon.map(str::trim).filter(|s| !s.is_empty());
        if icon.is_some_and(|s| s.len() > 32 || !s.chars().all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-')) {
            bail!("invalid icon id");
        }
        self.channel_mut(id)?.icon = icon.map(str::to_string);
        Ok(())
    }

    pub fn set_route(&mut self, channel: ChannelId, bus: BusId, gain: f32) -> Result<()> {
        if self.channel_mut(channel)?.kind == ChannelKind::Output {
            bail!("outputs can't be routed");
        }
        if gain > 0.0 && is_blocked_route(channel, bus) {
            bail!("this route would create a feedback loop");
        }
        if !self.state.buses.iter().any(|b| b.id == bus) {
            bail!("unknown bus {}", bus.0);
        }
        self.state.routes.retain(|r| !(r.channel == channel && r.bus == bus));
        let gain = gain.clamp(0.0, 2.0);
        if gain > 0.0 {
            self.state.routes.push(Route { channel, bus, gain });
        }
        self.params.set_route_gain(channel.0 as usize, bus.0 as usize, gain);
        Ok(())
    }

    /// Peaks since the last call, one entry per channel, attenuated by channel volume and mute.
    pub fn take_meters(&self) -> Vec<ChannelMeter> {
        self.state
            .channels
            .iter()
            .map(|c| {
                let raw = self.meters.take_peak(c.id.0 as usize);
                let peak = if c.muted || c.volume <= 0.001 {
                    0.0
                } else {
                    raw * c.volume
                };
                ChannelMeter { channel: c.id, peak }
            })
            .collect()
    }

    fn channel_mut(&mut self, id: ChannelId) -> Result<&mut Channel> {
        match self.state.channels.iter_mut().find(|c| c.id == id) {
            Some(c) => Ok(c),
            None => bail!("unknown channel {}", id.0),
        }
    }

    fn sync_channel(&self, id: ChannelId) {
        if let Some(c) = self.state.channels.iter().find(|c| c.id == id) {
            let gain = if c.muted { 0.0 } else { fader_to_gain(c.volume) };
            let submix_gain = if c.submix_muted { 0.0 } else { fader_to_gain(c.submix_volume) };
            self.params.set_channel_gain(id.0 as usize, gain);
            for r in &self.state.routes {
                if r.channel == id {
                    let effective = if r.bus.0 == 0 {
                        // Headphones / Monitor bus scales by submix_volume
                        r.gain * submix_gain
                    } else {
                        // Broadcast Stream Mix and other buses scale by main fader volume
                        r.gain * gain
                    };
                    self.params.set_route_gain(id.0 as usize, r.bus.0 as usize, effective);
                }
            }
        }
    }

    fn sync_all_params(&self) {
        for c in &self.state.channels {
            self.sync_channel(c.id);
        }
        for r in &self.state.routes {
            self.params.set_route_gain(r.channel.0 as usize, r.bus.0 as usize, r.gain);
        }
    }
}

/// GoXLR layout: 8 inputs × 5 outputs, matching the device's routing table.
/// Ids 0..=3 are bound to hardware faders A..=D. Mic → Headphones is *off* to avoid feedback.
fn default_state() -> MixerState {
    let ch = |id, name: &str, kind, metered| Channel {
        id: ChannelId(id),
        name: name.into(),
        kind,
        volume: 0.8,
        muted: false,
        submix_volume: 0.8,
        submix_muted: false,
        metered,
        icon: None,
        restore_volume: None,
        restore_submix_volume: None,
        fixed: false,
    };
    let channels = vec![
        ch(0, "Mic", ChannelKind::Microphone, true),
        ch(1, "Chat", ChannelKind::Application { process: "Discord.exe".into() }, true),
        ch(2, "Music", ChannelKind::Application { process: "Spotify.exe".into() }, true),
        ch(3, "Game", ChannelKind::Virtual, true),
        ch(4, "System", ChannelKind::System, true),
        ch(5, "Samples", ChannelKind::Sampler, true),
        // Analog inputs: no PC endpoint, and the GoXLR reports no level for them.
        ch(6, "Line In", ChannelKind::LineIn, false),
        ch(7, "Console", ChannelKind::LineIn, false),
        // Output levels: assignable to faders, never routed, no meter.
        ch(8, "Headphones", ChannelKind::Output, false),
        ch(9, "Monitor", ChannelKind::Output, false),
        ch(10, "Line Out", ChannelKind::Output, false),
        // Broadcast Stream Mix and Chat Mic: Windows capture endpoints with full volume control and mute
        Channel { fixed: false, ..ch(11, "Broadcast Stream Mix", ChannelKind::Output, true) },
        Channel { fixed: false, ..ch(12, "Chat Mic", ChannelKind::Output, true) },
    ];
    let buses: Vec<Bus> = ["Headphones", "Broadcast Stream Mix", "Line Out", "Chat Mic", "Sampler"]
        .iter()
        .enumerate()
        .map(|(i, n)| Bus { id: BusId(i as u32), name: (*n).into() })
        .collect();

    // GoXLR factory defaults: everything to Headphones/Broadcast/Line Out.
    // Mic → Headphones is routed via the hardware Monitor (dry sidetone, no reverb).
    let mut routes = Vec::new();
    let mut route = |c: u32, b: u32| routes.push(Route { channel: ChannelId(c), bus: BusId(b), gain: 1.0 });
    for c in 0..8u32 {
        route(c, 0);
        route(c, 1);
        if c != 0 {
            route(c, 2);
        }
    }
    route(0, 3);
    route(5, 3);
    route(0, 4);
    debug_assert!(channels.len() <= MAX_CHANNELS && buses.len() <= MAX_BUSES);
    // Default scheme: brand teal on, dimmed teal off; faders fade teal into deep green.
    let lighting = LIGHTING_ZONES
        .iter()
        .map(|z| LightingZone {
            zone: (*z).into(),
            active: "#1FC79A".into(),
            inactive: if z.starts_with("fader") { "#0B3D30".into() } else { "#103A30".into() },
            gradient: z.starts_with("fader"),
        })
        .collect();
    // Fader page: Mic, Chat, Music, Game on A–D.
    let faders = (0..4)
        .map(|i| FaderAssignment { fader: FADER_LETTERS[i].into(), channel: ChannelId(i as u32) })
        .collect();
    let effects = EffectsState { enabled: true, preset: 0, presets: vec![DEFAULT_PRESET; 6] };
    MixerState {
        channels, buses, routes, hardware: Vec::new(), lighting, faders,
        bleep: false, mic_muted: false, effects, mic: MicSettings::default(),
        vst_plugins: Vec::new(),
        fader_target_submix: true,
        active_page: 1,
    }
}
