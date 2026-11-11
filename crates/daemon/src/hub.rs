//! The state hub: the ONE place where mixer state changes.
//!
//! Every input (UI client, hardware fader, button, later OBS/MIDI/OS rules) becomes an `Input`;
//! `Hub::handle` applies it to the authoritative state and returns the `Effect`s needed to bring
//! every *other* party back in sync:
//!
//! ```text
//!   UI client ─┐                         ┌─► Engine params (audio follows instantly)
//!   GoXLR  ────┼─► Input ─► Hub::apply ──┼─► HardwareCommand (fader LEDs, mute LEDs, device mixer)
//!   OBS/MIDI ──┘                         └─► Broadcast State (all UIs / panels / Stream Deck)
//! ```
//!
//! `handle` is synchronous and side-effect free apart from the engine, so the sync rules are
//! unit-testable without sockets, threads or USB.

use mixforge_engine::{EffectsChange, Engine, FADER_LETTERS};
use mixforge_usb::goxlr::effects::EffectSettings;

use crate::icons;
use mixforge_protocol::{BusId, ChannelId, ClientMessage, ServerMessage};
use mixforge_usb::goxlr::lighting::Rgb;
use mixforge_usb::{Fader, HardwareCommand, HardwareEvent};

/// Physical fader must come this close to the software value before it takes control again.
const PICKUP_TOLERANCE: f32 = 0.03;

#[derive(Debug, Clone, PartialEq)]
pub enum Input {
    Client(ClientMessage),
    Hardware(HardwareEvent),
}

#[derive(Debug, Clone, PartialEq)]
pub enum Effect {
    /// To every connected client.
    Broadcast(ServerMessage),
    /// Only to the client that sent the input.
    Reply(ServerMessage),
    /// To the control surface.
    Hardware(HardwareCommand),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Origin {
    Client,
    Hardware,
}

#[derive(Debug, Clone)]
enum Change {
    Volume(ChannelId, f32),
    Mute(ChannelId, bool),
    SubmixVolume(ChannelId, f32),
    SubmixMute(ChannelId, bool),
    Route(ChannelId, BusId, f32),
    Name(ChannelId, String),
    Icon(ChannelId, Option<String>),
}

impl Change {
    fn channel(&self) -> ChannelId {
        match self {
            Change::Volume(id, _) | Change::Mute(id, _) | Change::Route(id, _, _) => *id,
            Change::SubmixVolume(id, _) | Change::SubmixMute(id, _) => *id,
            Change::Name(id, _) | Change::Icon(id, _) => *id,
        }
    }
}

/// Fader → channel binding plus soft-takeover state. GoXLR faders are not motorised, so after
/// the software changes a fader-bound volume the physical knob is "stale" until the user moves
/// it back to (or across) the software value.
struct FaderSlot {
    picked_up: bool,
    last_physical: Option<f32>,
}

/// The Mic Monitor output (zero-latency mic → headphones).
const MIC_MONITOR: ChannelId = ChannelId(9);

pub struct Hub {
    engine: Engine,
    faders: [FaderSlot; 4],
    /// Full GoXLR: motors follow software changes, so soft takeover is unnecessary
    /// (the driver hides the motor travel). GoXLR Mini / MIDI without motors: false.
    motorized_faders: bool,
    /// The device's fader LED modes are only known after we've set them once.
    lighting_synced: bool,
    /// Bleep held from a client (mic muted on the device while true).
    client_bleep: bool,
    /// Mic DSP values set by the user or a profile. Until then the device keeps its own (we
    /// can't read them back), so profile syncs don't overwrite them with defaults.
    mic_dsp_known: bool,
    /// Whether hardware faders control the submix (Headphones) or the main stream mix.
    pub fader_target_submix: bool,
}

impl Hub {
    pub fn new(engine: Engine) -> Self {
        // Fader → channel bindings live in the engine state (assignable); this is takeover state.
        let faders = std::array::from_fn(|_| FaderSlot {
            // At connect the hardware is authoritative: first physical move applies directly.
            picked_up: true,
            last_physical: None,
        });
        let mut hub = Self {
            engine,
            faders,
            motorized_faders: false,
            lighting_synced: false,
            client_bleep: false,
            mic_dsp_known: false,
            fader_target_submix: false,
        };
        hub.engine.disable_audio();
        hub
    }

    pub fn set_motorized_faders(&mut self, motorized: bool) {
        self.motorized_faders = motorized;
    }

    #[cfg(test)]
    pub fn engine(&self) -> &Engine {
        &self.engine
    }

    pub fn state_message(&self) -> ServerMessage {
        ServerMessage::State(self.engine.state().clone())
    }

    pub fn meters_message(&self) -> ServerMessage {
        ServerMessage::Meters {
            levels: self.engine.take_meters(),
        }
    }

    pub fn set_hardware(&mut self, hw: Vec<mixforge_protocol::HardwareInfo>) {
        if !hw.is_empty() {
            self.engine.disable_audio();
        }
        *self.engine.state_mut_hardware() = hw;
    }

    /// Everything a freshly connected control surface needs to mirror the current state.
    pub fn full_hardware_sync(&self) -> Vec<Effect> {
        let mut out = Vec::new();
        for fader in Fader::ALL {
            if let Some(ch) = self.channel(fader) {
                let pos = if self.fader_target_submix { ch.submix_volume } else { ch.volume };
                let muted = if self.fader_target_submix { ch.submix_muted } else { ch.muted };
                out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                    fader,
                    position: pos,
                    label: ch.name.clone(),
                }));
                out.push(Effect::Hardware(HardwareCommand::MuteLed {
                    fader,
                    on: muted,
                }));
            }
            out.extend(self.scribble(fader).map(Effect::Hardware));
        }
        out.extend(self.output_sync());
        out
    }

    /// Outputs at connect / profile load. Earlier versions muted output channels with
    /// `SetChannelState`, which the device keeps until told otherwise (also across restarts): a
    /// stale mute there silenced the Mic Monitor for good. Outputs are now muted by level only,
    /// so their channel state is cleared here. Then the Mic Monitor level is pushed, so the
    /// device matches what the app shows. Headphones / Line Out levels are never pushed (a wrong
    /// value could be dangerously loud).
    fn output_sync(&self) -> Vec<Effect> {
        let mut out: Vec<Effect> = self
            .engine
            .state()
            .channels
            .iter()
            .filter(|c| c.kind == mixforge_protocol::ChannelKind::Output && !c.fixed)
            .map(|c| {
                Effect::Hardware(HardwareCommand::ChannelMute {
                    channel: c.id.0,
                    muted: false,
                })
            })
            .collect();
        out.extend(self.mic_monitor_cmds());
        out
    }

    /// Mic Monitor = the GoXLR's zero-latency mic → headphones path. Its level is the MicMonitor
    /// channel volume; "off" (muted) is level 0. The mic input mute (cough / mute / censor)
    /// silences it as well, independently.
    fn mic_monitor_cmds(&self) -> Vec<Effect> {
        self.engine
            .state()
            .channels
            .iter()
            .find(|c| c.id == MIC_MONITOR)
            .map(|c| {
                Effect::Hardware(HardwareCommand::ChannelVolume {
                    channel: MIC_MONITOR.0,
                    volume: if c.muted { 0.0 } else { c.volume },
                })
            })
            .into_iter()
            .collect()
    }

    pub fn handle(&mut self, input: Input) -> Vec<Effect> {
        match input {
            Input::Client(msg) => self.handle_client(msg),
            Input::Hardware(ev) => self.handle_hardware(ev),
        }
    }

    fn handle_client(&mut self, msg: ClientMessage) -> Vec<Effect> {
        let change = match msg {
            ClientMessage::GetState => return vec![Effect::Reply(self.state_message())],
            ClientMessage::SetLighting {
                zone,
                active,
                inactive,
                gradient,
            } => {
                let r = self.engine.set_lighting(
                    &zone,
                    active.as_deref(),
                    inactive.as_deref(),
                    gradient,
                );
                let mut fx = self.apply_lighting(r);
                // Gradient mode is a separate device command; only send it when it changes.
                if let (Some(g), Some(f)) = (gradient, fader_of_zone(&zone)) {
                    if !fx.iter().any(|e| matches!(e, Effect::Reply(_))) {
                        fx.insert(
                            1,
                            Effect::Hardware(HardwareCommand::FaderGradient {
                                fader: f,
                                gradient: g,
                            }),
                        );
                    }
                }
                return fx;
            }
            ClientMessage::GetProfile => {
                return vec![Effect::Reply(ServerMessage::Profile {
                    profile: self.engine.profile(),
                })];
            }
            ClientMessage::LoadProfile { profile } => {
                if let Err(e) = self.engine.load_profile(&profile) {
                    return vec![Effect::Reply(ServerMessage::Error {
                        message: e.to_string(),
                    })];
                }
                // Sync Monitor mute state with the Mic → Headphones route from the profile if routes were supplied
                let mic_routed_to_hp = self.engine.state().routes.iter().any(|r| r.channel == ChannelId(0) && r.bus == BusId(0) && r.gain > 0.0);
                if !profile.routes.is_empty() {
                    let _ = self.engine.set_mute(MIC_MONITOR, !mic_routed_to_hp);
                }
                self.mic_dsp_known |= profile.mic.is_some();
                return self.full_profile_sync();
            }
            ClientMessage::SetLightingAll { active, inactive } => {
                let r = self.engine.set_lighting_all(&active, &inactive);
                return self.apply_lighting(r);
            }
            ClientMessage::SetLightingScheme { zones } => {
                let r = self.engine.set_lighting_scheme(&zones);
                // The fader modes are sent below anyway; don't also send the first-time sync.
                let was_synced = std::mem::replace(&mut self.lighting_synced, true);
                let mut fx = self.apply_lighting(r);
                if fx.iter().any(|e| matches!(e, Effect::Reply(_))) {
                    self.lighting_synced = was_synced;
                }
                if !fx.iter().any(|e| matches!(e, Effect::Reply(_))) {
                    // Presets may switch gradients on/off: resend all four fader modes.
                    let modes = self.fader_modes();
                    fx.splice(1..1, modes);
                }
                return fx;
            }
            ClientMessage::AssignFader { fader, channel } => {
                let Some(index) = FADER_LETTERS
                    .iter()
                    .position(|l| l.eq_ignore_ascii_case(&fader))
                else {
                    return vec![Effect::Reply(ServerMessage::Error {
                        message: format!("unknown fader {fader:?}"),
                    })];
                };
                return match self.engine.assign_fader(index, channel) {
                    Ok(changed) => self.apply_fader_changes(&changed),
                    Err(e) => vec![Effect::Reply(ServerMessage::Error {
                        message: e.to_string(),
                    })],
                };
            }
            ClientMessage::SetMixerPage { page } => {
                self.engine.set_active_page(page);
                return vec![Effect::Broadcast(self.state_message())];
            }
            ClientMessage::Bleep { pressed } => {
                self.client_bleep = pressed;
                return self.bleep_effects(pressed, true);
            }
            ClientMessage::SetMicMute { muted } => return self.mic_mute_effects(muted),
            ClientMessage::SetButtonLeds { leds } => {
                // Only the sampler section is app-owned; other LEDs follow daemon state.
                return leds
                    .into_iter()
                    .filter(|l| l.button.starts_with("sampler_"))
                    .map(|l| Effect::Hardware(HardwareCommand::ButtonLed { button: l.button, on: l.on }))
                    .collect();
            }
            ClientMessage::SetEffectDetails { details } => {
                return match self.engine.set_effect_details(details) {
                    Ok(()) => vec![
                        Effect::Hardware(self.effects_cmd(false)),
                        Effect::Broadcast(self.state_message()),
                    ],
                    Err(e) => vec![Effect::Reply(ServerMessage::Error {
                        message: e.to_string(),
                    })],
                };
            }
            ClientMessage::SetMicSettings { settings } => {
                return match self.engine.set_mic_settings(settings) {
                    Ok(()) => {
                        self.mic_dsp_known = true;
                        vec![
                            Effect::Hardware(HardwareCommand::MicDsp(
                                self.engine.state().mic.clone(),
                            )),
                            Effect::Broadcast(self.state_message()),
                        ]
                    }
                    Err(e) => vec![Effect::Reply(ServerMessage::Error {
                        message: e.to_string(),
                    })],
                };
            }
            ClientMessage::SetEffects {
                enabled,
                preset,
                reverb,
                echo,
                pitch,
                gender,
                megaphone,
                robot,
                hardtune,
            } => {
                let change = EffectsChange {
                    enabled,
                    preset,
                    reverb,
                    echo,
                    pitch,
                    gender,
                    megaphone,
                    robot,
                    hardtune,
                };
                return match self.engine.set_effects(change) {
                    // Changed in the app: also move the physical knobs.
                    Ok(()) => vec![
                        Effect::Hardware(self.effects_cmd(true)),
                        Effect::Broadcast(self.state_message()),
                    ],
                    Err(e) => vec![Effect::Reply(ServerMessage::Error {
                        message: e.to_string(),
                    })],
                };
            }
            ClientMessage::SetVolume { channel, volume } => Change::Volume(channel, volume),
            ClientMessage::SetMute { channel, muted } => Change::Mute(channel, muted),
            ClientMessage::SetSubmixVolume { channel, volume } => Change::SubmixVolume(channel, volume),
            ClientMessage::SetSubmixMute { channel, muted } => Change::SubmixMute(channel, muted),
            ClientMessage::SetFaderTargetSubmix { submix } => {
                self.fader_target_submix = submix;
                self.engine.set_fader_target_submix(submix);
                for slot in &mut self.faders {
                    slot.picked_up = false;
                }
                let mut out = Vec::new();
                for fader in Fader::ALL {
                    if let Some(ch) = self.channel(fader) {
                        let pos = if self.fader_target_submix { ch.submix_volume } else { ch.volume };
                        let muted = if self.fader_target_submix { ch.submix_muted } else { ch.muted };
                        out.push(Effect::Hardware(HardwareCommand::ChannelVolume {
                            channel: ch.id.0,
                            volume: pos,
                        }));
                        out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                            fader,
                            position: pos,
                            label: ch.name.clone(),
                        }));
                        out.push(Effect::Hardware(HardwareCommand::MuteLed { fader, on: muted }));
                    }
                }
                return out;
            }
            ClientMessage::SetVstPlugin { plugin } => {
                return match self.engine.set_vst_plugin(plugin) {
                    Ok(()) => vec![Effect::Broadcast(self.state_message())],
                    Err(e) => vec![Effect::Reply(ServerMessage::Error { message: e.to_string() })],
                };
            }
            ClientMessage::RemoveVstPlugin { id } => {
                return match self.engine.remove_vst_plugin(&id) {
                    Ok(()) => vec![Effect::Broadcast(self.state_message())],
                    Err(e) => vec![Effect::Reply(ServerMessage::Error { message: e.to_string() })],
                };
            }
            ClientMessage::ScanVstPlugins => {
                return vec![Effect::Broadcast(self.state_message())];
            }
            ClientMessage::SetRoute { channel, bus, gain } => Change::Route(channel, bus, gain),
            ClientMessage::SetChannelName { channel, name } => Change::Name(channel, name),
            ClientMessage::SetChannelIcon { channel, icon } => Change::Icon(channel, icon),
        };
        match self.apply(change, Origin::Client) {
            Ok(effects) => effects,
            Err(e) => vec![Effect::Reply(ServerMessage::Error {
                message: e.to_string(),
            })],
        }
    }

    fn handle_hardware(&mut self, ev: HardwareEvent) -> Vec<Effect> {
        let result = match ev {
            HardwareEvent::FaderMoved { fader, position } => match self.takeover(fader, position) {
                Some(ch) => {
                    let change = if self.fader_target_submix {
                        Change::SubmixVolume(ch, position)
                    } else {
                        Change::Volume(ch, position)
                    };
                    self.apply(change, Origin::Hardware)
                }
                None => Ok(Vec::new()),
            },
            HardwareEvent::MuteButton {
                fader,
                pressed: true,
            } => match self.channel(fader) {
                Some(ch) => {
                    let (id, muted) = if self.fader_target_submix {
                        (ch.id, ch.submix_muted)
                    } else {
                        (ch.id, ch.muted)
                    };
                    let change = if self.fader_target_submix {
                        Change::SubmixMute(id, !muted)
                    } else {
                        Change::Mute(id, !muted)
                    };
                    self.apply(change, Origin::Hardware)
                }
                None => Ok(Vec::new()),
            },
            HardwareEvent::MuteButton { pressed: false, .. } => Ok(Vec::new()),
            // The device plays the bleep tone itself while its button is held; we mirror the
            // state (LED + clients). Mic muting is only needed for the client-side button.
            HardwareEvent::Button { id, pressed } if id == "bleep" => {
                Ok(self.bleep_effects(pressed, false))
            }
            // Global mic mute: each press toggles (two-way synced with the app's button).
            HardwareEvent::Button { id, pressed: true } if id == "cough" => {
                let muted = !self.engine.state().mic_muted;
                Ok(self.mic_mute_effects(muted))
            }
            HardwareEvent::Button { id, pressed: true } if id.starts_with("effect_") => {
                Ok(self.effect_button(&id))
            }
            // Sampler buttons: the app's sampler plays the pads (press and release).
            HardwareEvent::Button { id, pressed } if id.starts_with("sampler_") => {
                Ok(vec![Effect::Broadcast(ServerMessage::Button { id, pressed })])
            }
            HardwareEvent::Button { id, pressed } => {
                tracing::debug!(%id, pressed, "unmapped hardware button");
                Ok(Vec::new())
            }
            HardwareEvent::EncoderTurned { id, delta } => {
                if self.engine.turn_encoder(&id, delta) {
                    // The knob already shows the value: don't move it back.
                    Ok(vec![
                        Effect::Hardware(self.effects_cmd(false)),
                        Effect::Broadcast(self.state_message()),
                    ])
                } else {
                    Ok(Vec::new())
                }
            }
            // Telemetry, not state: goes straight to the meter bank, no broadcast.
            HardwareEvent::MicLevel(level) => {
                self.engine.push_meter(ChannelId(0), level);
                Ok(Vec::new())
            }
            HardwareEvent::Disconnected => {
                self.set_hardware(Vec::new());
                Ok(vec![Effect::Broadcast(self.state_message())])
            }
        };
        result.unwrap_or_else(|e| {
            tracing::warn!("hardware event rejected: {e:#}");
            Vec::new()
        })
    }

    /// Soft takeover. Returns the bound channel if this physical move should apply.
    fn takeover(&mut self, fader: Fader, position: f32) -> Option<ChannelId> {
        let (current, id, is_submix_muted, is_muted) = {
            let ch = self.channel(fader)?;
            let current = if self.fader_target_submix { ch.submix_volume } else { ch.volume };
            (current, ch.id, ch.submix_muted, ch.muted)
        };
        let slot = &mut self.faders[fader.index()];
        if !slot.picked_up {
            let near = (position - current).abs() <= PICKUP_TOLERANCE;
            let crossed = slot
                .last_physical
                .is_some_and(|last| (last - current).signum() != (position - current).signum());
            slot.last_physical = Some(position);
            if !(near || crossed) {
                return None;
            }
            slot.picked_up = true;
        }
        slot.last_physical = Some(position);
        if position > 0.02 {
            if self.fader_target_submix {
                if is_submix_muted {
                    let _ = self.engine.set_submix_mute(id, false);
                }
            } else if is_muted {
                let _ = self.engine.set_mute(id, false);
            }
        }
        Some(id)
    }

    /// The single mutation path. Updates the engine (→ audio), then emits what hardware and
    /// clients need, derived from what actually changed. Hardware is told about changes *it*
    /// didn't originate; it never gets its own fader movement echoed back.
    fn apply(&mut self, change: Change, origin: Origin) -> anyhow::Result<Vec<Effect>> {
        let mut out = Vec::new();
        let id = change.channel();
        let before = self.snapshot(id);
        match change {
            Change::Volume(id, v) => self.engine.set_volume(id, v)?,
            Change::Mute(id, muted) => self.engine.set_mute(id, muted)?,
            Change::SubmixVolume(id, v) => self.engine.set_submix_volume(id, v)?,
            Change::SubmixMute(id, muted) => self.engine.set_submix_mute(id, muted)?,
            Change::Name(id, ref name) => self.engine.set_name(id, name)?,
            Change::Icon(id, ref icon) => self.engine.set_icon(id, icon.as_deref())?,
            Change::Route(ch, bus, gain) => {
                self.engine.set_route(ch, bus, gain)?;
                // The device takes a whole row per input: send everything this input goes to.
                out.push(Effect::Hardware(self.routing_row(ch)));
                // Mic → Headphones route controls the hardware Mic Monitor ("Monitor", dry sidetone with no hall)
                if ch == ChannelId(0) && bus == BusId(0) {
                    if gain > 0.0 {
                        let _ = self.engine.set_mute(MIC_MONITOR, false);
                    } else {
                        let _ = self.engine.set_mute(MIC_MONITOR, true);
                    }
                    out.extend(self.mic_monitor_cmds());
                    if let Some(f) = self.fader_of(MIC_MONITOR) {
                        if let Some(c) = self.channel(f) {
                            out.push(Effect::Hardware(HardwareCommand::MuteLed { fader: f, on: c.muted }));
                            out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                                fader: f,
                                position: c.volume,
                                label: c.name.clone(),
                            }));
                        }
                    }
                }
            }
        }
        let after = self.snapshot(id);

        if let (Some((v0, m0, n0)), Some((v1, m1, n1))) = (before, after) {
            // When Monitor (channel 9) mute or volume changes, keep the Mic → Headphones route in sync
            if id == MIC_MONITOR && (v1 != v0 || m1 != m0) {
                let active = !m1 && v1 > 0.0;
                let _ = self.engine.set_route(ChannelId(0), BusId(0), if active { 1.0 } else { 0.0 });
            }
            let fader = self.fader_of(id);
            let device_moved_it = origin == Origin::Hardware
                && (matches!(change, Change::Volume(..)) || matches!(change, Change::SubmixVolume(..)));
            if v1 != v0 {
                // Smart mute also lands here: muting drops the volume to 0, unmuting restores it.
                if !device_moved_it {
                    out.push(Effect::Hardware(HardwareCommand::ChannelVolume {
                        channel: id.0,
                        volume: v1,
                    }));
                }
                if let Some(f) = fader {
                    if !device_moved_it && !self.motorized_faders {
                        self.faders[f.index()].picked_up = false;
                    }
                }
            }
            if let Some(f) = fader {
                if v1 != v0 || n1 != n0 {
                    out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                        fader: f,
                        position: v1,
                        label: n1.clone(),
                    }));
                }
                // New label or icon: redraw the scribble strip above this fader.
                if n1 != n0 || matches!(change, Change::Icon(..)) {
                    out.extend(self.scribble(f).map(Effect::Hardware));
                }
            }
            if m1 != m0 || matches!(change, Change::Mute(..)) {
                // Always sent on a mute action: the device's mute button doesn't mute by itself.
                // The mic stays muted on the device while the global mic mute or Bleep is active.
                if id.0 == 0 {
                    out.extend(self.mic_mute_cmds());
                } else if self.is_output(id) {
                    // Outputs are muted by level, never by channel state (see `output_sync`).
                    out.push(Effect::Hardware(HardwareCommand::ChannelVolume {
                        channel: id.0,
                        volume: if m1 { 0.0 } else { v1 },
                    }));
                } else {
                    out.push(Effect::Hardware(HardwareCommand::ChannelMute {
                        channel: id.0,
                        muted: m1,
                    }));
                }
                // The LED is software-owned even when the button was pressed on the device.
                if let Some(f) = fader {
                    out.push(Effect::Hardware(HardwareCommand::MuteLed {
                        fader: f,
                        on: m1,
                    }));
                    // When muted, drop physical fader display to 0; when unmuted, restore to volume position v1.
                    out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                        fader: f,
                        position: if m1 { 0.0 } else { v1 },
                        label: n1.clone(),
                    }));
                }
            }
        }
        out.push(Effect::Broadcast(self.state_message()));
        Ok(out)
    }

    /// Lighting changed: send the complete colour map (the device has no partial update and no
    /// read-back) and broadcast the new state. Nothing is pushed at startup, so the colours the
    /// device already shows stay until the user changes one here.
    fn apply_lighting(&mut self, result: anyhow::Result<()>) -> Vec<Effect> {
        if let Err(e) = result {
            return vec![Effect::Reply(ServerMessage::Error {
                message: e.to_string(),
            })];
        }
        let mut out = vec![Effect::Hardware(self.colour_map())];
        if !self.lighting_synced {
            // Until now the device's fader LED modes were unknown (whatever the last app set),
            // so the gradient shown here could differ from the device. Sync all four once.
            out.extend(self.fader_modes());
            self.lighting_synced = true;
        }
        out.push(Effect::Broadcast(self.state_message()));
        out
    }

    /// After fader assignments changed: rebind on the device and refresh each fader's display
    /// and mute LED for its new channel.
    fn apply_fader_changes(&mut self, changed: &[usize]) -> Vec<Effect> {
        let mut out = Vec::new();
        for &i in changed {
            let fader = Fader::ALL[i];
            let Some(ch) = self.channel(fader).cloned() else {
                continue;
            };
            // Only rebind. The device keeps each channel's own level; we must NOT push our stored
            // volume here: for outputs (Headphones, Line Out, Mic Monitor) it was never read from
            // the device, and forcing it could suddenly make headphones loud. Any resulting
            // motor/fader movement is reported back by the device and adopted as usual.
            out.push(Effect::Hardware(HardwareCommand::AssignFader {
                fader,
                channel: ch.id.0,
            }));
            let pos = if self.fader_target_submix { ch.submix_volume } else { ch.volume };
            let muted = if self.fader_target_submix { ch.submix_muted } else { ch.muted };
            out.push(Effect::Hardware(HardwareCommand::FaderDisplay {
                fader,
                position: pos,
                label: ch.name.clone(),
            }));
            out.push(Effect::Hardware(HardwareCommand::MuteLed {
                fader,
                on: muted,
            }));
            out.extend(self.scribble(fader).map(Effect::Hardware));
            // Without motors the physical fader no longer matches: require pickup.
            self.faders[i] = FaderSlot {
                picked_up: self.motorized_faders,
                last_physical: None,
            };
        }
        out.push(Effect::Broadcast(self.state_message()));
        out
    }

    fn bleep_effects(&mut self, pressed: bool, from_client: bool) -> Vec<Effect> {
        let held = pressed || self.client_bleep;
        self.engine.set_bleep(held);
        let mut out = vec![Effect::Hardware(HardwareCommand::ButtonLed {
            button: "bleep".into(),
            on: held,
        })];
        if from_client {
            // Censor: mute the mic on the device while held; afterwards restore its real state.
            out.extend(self.mic_mute_cmds());
        }
        out.push(Effect::Broadcast(self.state_message()));
        out
    }

    /// The mic is muted on the device if its fader is muted, the global mic mute (cough) is on,
    /// or Bleep is held in the app. All three are independent; releasing one keeps the others.
    fn mic_device_muted(&self) -> bool {
        let fader_muted = self
            .engine
            .state()
            .channels
            .iter()
            .find(|c| c.id == ChannelId(0))
            .is_some_and(|c| c.muted);
        fader_muted || self.engine.state().mic_muted || self.client_bleep
    }

    /// Preamp hardware input mute: cuts the mic before the DSP and silences Mic Monitor in headphones.
    /// Used when the fader is muted or cough is active. Bleep only cuts the voice from the broadcast/chat mixes
    /// so the user's sidetone/monitor remains active.
    fn mic_input_muted(&self) -> bool {
        let fader_muted = self
            .engine
            .state()
            .channels
            .iter()
            .find(|c| c.id == ChannelId(0))
            .is_some_and(|c| c.muted);
        fader_muted || self.engine.state().mic_muted
    }

    fn mic_mute_effects(&mut self, muted: bool) -> Vec<Effect> {
        self.engine.set_mic_muted(muted);
        let mut out = self.mic_mute_cmds();
        out.push(Effect::Hardware(HardwareCommand::ButtonLed {
            button: "cough".into(),
            on: muted,
        }));
        out.push(Effect::Broadcast(self.state_message()));
        out
    }

    /// Muting the mic takes two commands on the GoXLR: the Mic channel state (removes it from the
    /// mixes) and the mic *input* mute (also silences the Mic Monitor in the headphones). Sending
    /// only the first left the monitor open: that was the Mic Monitor / Mic Mute bug.
    /// For Bleep, `MicInputMute` is not set so the monitor path stays open.
    fn mic_mute_cmds(&self) -> Vec<Effect> {
        let channel_muted = self.mic_device_muted();
        let input_muted = self.mic_input_muted();
        vec![
            Effect::Hardware(HardwareCommand::ChannelMute { channel: 0, muted: channel_muted }),
            Effect::Hardware(HardwareCommand::MicInputMute(input_muted)),
        ]
    }

    fn effects_cmd(&self, sync_encoders: bool) -> HardwareCommand {
        let fx = &self.engine.state().effects;
        let p = self.engine.active_preset();
        HardwareCommand::Effects {
            settings: EffectSettings {
                enabled: fx.enabled,
                reverb: p.reverb,
                echo: p.echo,
                pitch: p.pitch,
                gender: p.gender,
                megaphone: p.megaphone,
                robot: p.robot,
                hardtune: p.hardtune,
                details: p.details,
            },
            preset: fx.preset,
            sync_encoders,
        }
    }

    /// Hardware effect buttons: FX / Megaphone / Robot / Hardtune toggle, 1–6 select a preset
    /// (which moves the knobs to that preset's positions).
    fn effect_button(&mut self, id: &str) -> Vec<Effect> {
        let fx = self.engine.state().effects.clone();
        let p = self.engine.active_preset();
        let change = match id {
            "effect_fx" => EffectsChange {
                enabled: Some(!fx.enabled),
                ..Default::default()
            },
            "effect_megaphone" => EffectsChange {
                megaphone: Some(!p.megaphone),
                ..Default::default()
            },
            "effect_robot" => EffectsChange {
                robot: Some(!p.robot),
                ..Default::default()
            },
            "effect_hardtune" => EffectsChange {
                hardtune: Some(!p.hardtune),
                ..Default::default()
            },
            _ => match id
                .strip_prefix("effect_select_")
                .and_then(|n| n.parse::<u8>().ok())
            {
                Some(n @ 1..=6) => EffectsChange {
                    preset: Some(n - 1),
                    ..Default::default()
                },
                _ => return Vec::new(),
            },
        };
        let sync = change.preset.is_some();
        if self.engine.set_effects(change).is_err() {
            return Vec::new();
        }
        vec![
            Effect::Hardware(self.effects_cmd(sync)),
            Effect::Broadcast(self.state_message()),
        ]
    }

    /// Scribble strip content for a fader: its channel's label and icon.
    fn scribble(&self, fader: Fader) -> Option<HardwareCommand> {
        let ch = self.channel(fader)?;
        let icon = ch
            .icon
            .as_deref()
            .unwrap_or(icons::default_for(ch.id.0, &ch.kind));
        Some(HardwareCommand::Scribble {
            fader,
            text: ch.name.clone(),
            glyph: icons::glyph(icon),
        })
    }

    fn fader_modes(&self) -> Vec<Effect> {
        Fader::ALL
            .into_iter()
            .map(|f| {
                let zone = format!("fader_{}", (b'a' + f.index() as u8) as char);
                let gradient = self
                    .engine
                    .state()
                    .lighting
                    .iter()
                    .find(|z| z.zone == zone)
                    .is_some_and(|z| z.gradient);
                Effect::Hardware(HardwareCommand::FaderGradient { fader: f, gradient })
            })
            .collect()
    }

    fn routing_row(&self, ch: ChannelId) -> HardwareCommand {
        let buses = self
            .engine
            .state()
            .routes
            .iter()
            .filter(|r| r.channel == ch && r.gain > 0.0)
            .map(|r| r.bus.0)
            .collect();
        HardwareCommand::Routing {
            channel: ch.0,
            buses,
        }
    }

    /// After a profile load: push volumes, mutes, routing, fader modes and colours to the
    /// device and the new state to every client, in one go.
    fn is_output(&self, id: ChannelId) -> bool {
        self.engine
            .state()
            .channels
            .iter()
            .any(|c| c.id == id && c.kind == mixforge_protocol::ChannelKind::Output)
    }

    fn full_profile_sync(&mut self) -> Vec<Effect> {
        let mut out = Vec::new();
        let channels: Vec<_> = self
            .engine
            .state()
            .channels
            .iter()
            .map(|c| {
                (
                    c.id,
                    c.volume,
                    c.muted,
                    c.kind == mixforge_protocol::ChannelKind::Output,
                )
            })
            .collect();
        for (id, volume, muted, is_output) in channels {
            // Output levels (Headphones, Line Out, Mic Monitor) are never pushed from a profile:
            // their stored value may be a placeholder that was never read from the device, and
            // a wrong headphone level can be dangerously loud.
            if is_output {
                continue;
            }
            out.push(Effect::Hardware(HardwareCommand::ChannelVolume {
                channel: id.0,
                volume,
            }));
            if id.0 == 0 {
                out.extend(self.mic_mute_cmds()); // respects mic mute (cough) and Bleep too
            } else {
                out.push(Effect::Hardware(HardwareCommand::ChannelMute {
                    channel: id.0,
                    muted,
                }));
            }
            out.push(Effect::Hardware(self.routing_row(id)));
        }
        if !self.motorized_faders {
            for slot in &mut self.faders {
                slot.picked_up = false;
            }
        }
        for f in Fader::ALL {
            if let Some(ch) = self.engine.fader_channel(f.index()) {
                out.push(Effect::Hardware(HardwareCommand::AssignFader {
                    fader: f,
                    channel: ch.0,
                }));
            }
        }
        out.extend(self.full_hardware_sync());
        out.extend(self.fader_modes());
        self.lighting_synced = true;
        out.push(Effect::Hardware(self.colour_map()));
        out.push(Effect::Hardware(self.effects_cmd(true)));
        if self.mic_dsp_known {
            out.push(Effect::Hardware(HardwareCommand::MicDsp(
                self.engine.state().mic.clone(),
            )));
        }
        out.push(Effect::Broadcast(self.state_message()));
        out
    }

    fn colour_map(&self) -> HardwareCommand {
        let zones = self
            .engine
            .state()
            .lighting
            .iter()
            .filter_map(|z| {
                Some((
                    z.zone.clone(),
                    Rgb::parse_hex(&z.active)?,
                    Rgb::parse_hex(&z.inactive)?,
                ))
            })
            .collect();
        HardwareCommand::ColourMap(zones)
    }

    fn snapshot(&self, id: ChannelId) -> Option<(f32, bool, String)> {
        self.engine
            .state()
            .channels
            .iter()
            .find(|c| c.id == id)
            .map(|c| {
                if self.fader_target_submix {
                    (c.submix_volume, c.submix_muted, c.name.clone())
                } else {
                    (c.volume, c.muted, c.name.clone())
                }
            })
    }

    fn channel(&self, fader: Fader) -> Option<&mixforge_protocol::Channel> {
        let id = self.engine.fader_channel(fader.index())?;
        self.engine.state().channels.iter().find(|c| c.id == id)
    }

    fn fader_of(&self, id: ChannelId) -> Option<Fader> {
        Fader::ALL
            .into_iter()
            .find(|f| self.engine.fader_channel(f.index()) == Some(id))
    }
}

fn fader_of_zone(zone: &str) -> Option<Fader> {
    match zone {
        "fader_a" => Some(Fader::A),
        "fader_b" => Some(Fader::B),
        "fader_c" => Some(Fader::C),
        "fader_d" => Some(Fader::D),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn hub() -> Hub {
        Hub::new(Engine::headless())
    }

    fn volume(hub: &Hub, id: u32) -> f32 {
        hub.engine()
            .state()
            .channels
            .iter()
            .find(|c| c.id.0 == id)
            .unwrap()
            .volume
    }

    fn broadcasts(effects: &[Effect]) -> usize {
        effects
            .iter()
            .filter(|e| matches!(e, Effect::Broadcast(ServerMessage::State(_))))
            .count()
    }

    fn hw(effects: &[Effect]) -> Vec<&HardwareCommand> {
        effects
            .iter()
            .filter_map(|e| {
                if let Effect::Hardware(c) = e {
                    Some(c)
                } else {
                    None
                }
            })
            .collect()
    }

    #[test]
    fn hardware_fader_updates_state_audio_and_all_clients() {
        let mut hub = hub();
        let fx = hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::B,
            position: 0.25,
        }));

        assert_eq!(volume(&hub, 1), 0.25, "state follows the physical fader");
        assert!(
            hub.engine().effective_gain(ChannelId(1)) > 0.0,
            "RT gain follows the state"
        );
        assert_eq!(broadcasts(&fx), 1, "every UI gets the new state");
        assert!(
            !hw(&fx)
                .iter()
                .any(|c| matches!(c, HardwareCommand::ChannelVolume { .. })),
            "the device's own move is not echoed back as a volume command"
        );
    }

    #[test]
    fn hardware_mute_button_toggles_and_lights_led() {
        let mut hub = hub();
        let fx = hub.handle(Input::Hardware(HardwareEvent::MuteButton {
            fader: Fader::A,
            pressed: true,
        }));
        assert!(hub.engine().state().channels[0].muted);
        assert_eq!(hub.engine().effective_gain(ChannelId(0)), 0.0);
        assert!(hw(&fx).contains(&&HardwareCommand::MuteLed {
            fader: Fader::A,
            on: true
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: true
        }));
        assert_eq!(broadcasts(&fx), 1);

        // Release does nothing; second press un-mutes.
        assert!(hub
            .handle(Input::Hardware(HardwareEvent::MuteButton {
                fader: Fader::A,
                pressed: false
            }))
            .is_empty());
        hub.handle(Input::Hardware(HardwareEvent::MuteButton {
            fader: Fader::A,
            pressed: true,
        }));
        assert!(!hub.engine().state().channels[0].muted);
    }

    #[test]
    fn ui_change_reaches_hardware_and_clients() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(2),
            volume: 0.5,
        }));
        let cmds = hw(&fx);
        assert!(cmds.contains(&&HardwareCommand::ChannelVolume {
            channel: 2,
            volume: 0.5
        }));
        assert!(cmds.iter().any(|c| matches!(
            c,
            HardwareCommand::FaderDisplay {
                fader: Fader::C,
                ..
            }
        )));
        assert_eq!(broadcasts(&fx), 1);

        let fx = hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(0),
            muted: true,
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::MuteLed {
            fader: Fader::A,
            on: true
        }));
    }

    #[test]
    fn soft_takeover_after_ui_change() {
        let mut hub = hub();
        // Physical fader D sits at 0.9 and the UI pulls Game down to 0.2.
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.9,
        }));
        hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(3),
            volume: 0.2,
        }));

        // A small nudge of the stale knob must NOT jump the volume back to ~0.9.
        let fx = hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.85,
        }));
        assert!(fx.is_empty());
        assert_eq!(volume(&hub, 3), 0.2);

        // Sweeping the knob across the software value picks it up again.
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.15,
        }));
        assert_eq!(volume(&hub, 3), 0.15);
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.4,
        }));
        assert_eq!(volume(&hub, 3), 0.4);
    }

    #[test]
    fn motorized_faders_skip_soft_takeover() {
        let mut hub = hub();
        hub.set_motorized_faders(true);
        hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(3),
            volume: 0.2,
        }));
        // Motor brought the knob to 0.2; a human pushing it up must apply immediately.
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.6,
        }));
        assert_eq!(volume(&hub, 3), 0.6);
    }

    #[test]
    fn smart_mute_drops_to_zero_and_restores_exact_volume() {
        let mut hub = hub();
        hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(2),
            volume: 0.637,
        }));

        let fx = hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(2),
            muted: true,
        }));
        let ch = &hub.engine().state().channels[2];
        assert!(ch.muted);
        assert_eq!(ch.volume, 0.0, "fader drops to 0");
        assert_eq!(
            ch.restore_volume,
            Some(0.637),
            "previous position remembered"
        );
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelVolume {
            channel: 2,
            volume: 0.0
        }));

        let fx = hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(2),
            muted: false,
        }));
        let ch = &hub.engine().state().channels[2];
        assert!(!ch.muted);
        assert_eq!(ch.volume, 0.637, "exact previous position restored");
        assert_eq!(ch.restore_volume, None);
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelVolume {
            channel: 2,
            volume: 0.637
        }));
    }

    #[test]
    fn hardware_mute_button_uses_smart_mute_too() {
        let mut hub = hub();
        hub.handle(Input::Hardware(HardwareEvent::MuteButton {
            fader: Fader::A,
            pressed: true,
        }));
        assert_eq!(volume(&hub, 0), 0.0);
        hub.handle(Input::Hardware(HardwareEvent::MuteButton {
            fader: Fader::A,
            pressed: true,
        }));
        assert_eq!(volume(&hub, 0), 0.8);
    }

    #[test]
    fn raising_fader_while_muted_unmutes() {
        let mut hub = hub();
        hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(1),
            muted: true,
        }));
        let fx = hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(1),
            volume: 0.3,
        }));
        let ch = &hub.engine().state().channels[1];
        assert!(!ch.muted && ch.restore_volume.is_none());
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 1,
            muted: false
        }));
    }

    #[test]
    fn custom_label_and_icon() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetChannelName {
            channel: ChannelId(1),
            name: "  Discord  ".into(),
        }));
        assert_eq!(hub.engine().state().channels[1].name, "Discord");
        assert!(hw(&fx).iter().any(
            |c| matches!(c, HardwareCommand::FaderDisplay { label, .. } if label == "Discord")
        ));
        hub.handle(Input::Client(ClientMessage::SetChannelIcon {
            channel: ChannelId(1),
            icon: Some("headset".into()),
        }));
        assert_eq!(
            hub.engine().state().channels[1].icon.as_deref(),
            Some("headset")
        );
        let fx = hub.handle(Input::Client(ClientMessage::SetChannelName {
            channel: ChannelId(1),
            name: "   ".into(),
        }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));
    }

    #[test]
    fn lighting_change_sends_full_colour_map_and_broadcasts() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "mute_a".into(),
            active: Some("#ff0000".into()),
            inactive: None,
            gradient: None,
        }));
        let zone = hub
            .engine()
            .state()
            .lighting
            .iter()
            .find(|z| z.zone == "mute_a")
            .unwrap();
        assert_eq!(zone.active, "#FF0000");
        assert_eq!(zone.inactive, "#103A30", "omitted colour unchanged");
        let Some(HardwareCommand::ColourMap(zones)) = hw(&fx).into_iter().next().cloned() else {
            panic!()
        };
        assert_eq!(zones.len(), 20, "complete map, not a partial update");
        assert!(zones.contains(&("mute_a".into(), Rgb(255, 0, 0), Rgb(0x10, 0x3A, 0x30))));
        assert_eq!(broadcasts(&fx), 1);
    }

    #[test]
    fn lighting_rejects_bad_input() {
        let mut hub = hub();
        for msg in [
            ClientMessage::SetLighting {
                zone: "mute_a".into(),
                active: Some("red".into()),
                inactive: None,
                gradient: None,
            },
            ClientMessage::SetLighting {
                zone: "nope".into(),
                active: Some("#FF0000".into()),
                inactive: None,
                gradient: None,
            },
        ] {
            let fx = hub.handle(Input::Client(msg));
            assert!(matches!(
                fx.as_slice(),
                [Effect::Reply(ServerMessage::Error { .. })]
            ));
        }
        hub.handle(Input::Client(ClientMessage::SetLightingAll {
            active: "#112233".into(),
            inactive: "#000000".into(),
        }));
        assert!(hub
            .engine()
            .state()
            .lighting
            .iter()
            .all(|z| z.active == "#112233"));
    }

    #[test]
    fn route_toggle_sends_the_whole_input_row() {
        let mut hub = hub();
        // Music defaults: Headphones (0), Broadcast (1), Line Out (2). Add Chat Mic (3).
        let fx = hub.handle(Input::Client(ClientMessage::SetRoute {
            channel: ChannelId(2),
            bus: BusId(3),
            gain: 1.0,
        }));
        let Some(HardwareCommand::Routing { channel, mut buses }) =
            hw(&fx).into_iter().next().cloned()
        else {
            panic!()
        };
        buses.sort();
        assert_eq!((channel, buses), (2, vec![0, 1, 2, 3]));
        let fx = hub.handle(Input::Client(ClientMessage::SetRoute {
            channel: ChannelId(2),
            bus: BusId(2),
            gain: 0.0,
        }));
        let Some(HardwareCommand::Routing { mut buses, .. }) = hw(&fx).into_iter().next().cloned()
        else {
            panic!()
        };
        buses.sort();
        assert_eq!(buses, vec![0, 1, 3]);
    }

    #[test]
    fn mic_route_to_headphones_controls_monitor() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetRoute {
            channel: ChannelId(0),
            bus: BusId(0),
            gain: 1.0,
        }));
        let cmds = hw(&fx);
        assert!(cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 9, volume } if *volume > 0.0)));
        if let Some(HardwareCommand::Routing { channel: _, buses }) = cmds.iter().find_map(|c| match c {
            HardwareCommand::Routing { channel, buses } if *channel == 0 => Some(HardwareCommand::Routing { channel: *channel, buses: buses.clone() }),
            _ => None,
        }) {
            assert!(buses.contains(&0), "Headphones must be in the Mic hardware routing row");
        }

        // Toggling Mic → Headphones off mutes the monitor (level 0).
        let fx = hub.handle(Input::Client(ClientMessage::SetRoute {
            channel: ChannelId(0),
            bus: BusId(0),
            gain: 0.0,
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelVolume { channel: 9, volume: 0.0 }));

        // And in reverse: unmuting Monitor turns the route back on.
        hub.handle(Input::Client(ClientMessage::SetMute { channel: ChannelId(9), muted: false }));
        assert!(hub.engine().state().routes.iter().any(|r| r.channel == ChannelId(0) && r.bus == BusId(0) && r.gain > 0.0));
    }

    #[test]
    fn fader_gradient_toggle_sends_display_mode() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "fader_b".into(),
            active: Some("#FF0000".into()),
            inactive: Some("#0000FF".into()),
            gradient: Some(false),
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::FaderGradient {
            fader: Fader::B,
            gradient: false
        }));
        assert!(hw(&fx)
            .iter()
            .any(|c| matches!(c, HardwareCommand::ColourMap(_))));
        // Colour-only edits don't resend the display mode.
        let fx = hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "fader_b".into(),
            active: Some("#00FF00".into()),
            inactive: None,
            gradient: None,
        }));
        assert!(!hw(&fx)
            .iter()
            .any(|c| matches!(c, HardwareCommand::FaderGradient { .. })));
    }

    #[test]
    fn profile_round_trip_restores_everything_and_syncs_hardware() {
        let mut hub = hub();
        hub.handle(Input::Client(ClientMessage::SetChannelName {
            channel: ChannelId(1),
            name: "Discord".into(),
        }));
        hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(2),
            volume: 0.42,
        }));
        hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(3),
            muted: true,
        }));
        hub.handle(Input::Client(ClientMessage::SetRoute {
            channel: ChannelId(0),
            bus: BusId(0),
            gain: 1.0,
        }));
        hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "cough".into(),
            active: Some("#ABCDEF".into()),
            inactive: None,
            gradient: None,
        }));
        let reply = hub.handle(Input::Client(ClientMessage::GetProfile));
        let [Effect::Reply(ServerMessage::Profile { profile })] = reply.as_slice() else {
            panic!()
        };
        let profile = profile.clone();

        let mut fresh = Hub::new(Engine::headless());
        let fx = fresh.handle(Input::Client(ClientMessage::LoadProfile {
            profile: profile.clone(),
        }));
        let s = fresh.engine().state();
        assert_eq!(s.channels[1].name, "Discord");
        assert_eq!(s.channels[2].volume, 0.42);
        assert!(
            s.channels[3].muted
                && s.channels[3].volume == 0.0
                && s.channels[3].restore_volume == Some(0.8)
        );
        assert!(s
            .routes
            .iter()
            .any(|r| r.channel == ChannelId(0) && r.bus == BusId(0)));
        assert_eq!(
            s.lighting
                .iter()
                .find(|z| z.zone == "cough")
                .unwrap()
                .active,
            "#ABCDEF"
        );
        let cmds = hw(&fx);
        assert_eq!(
            cmds.iter()
                .filter(|c| matches!(c, HardwareCommand::Routing { .. }))
                .count(),
            8
        );
        assert!(cmds
            .iter()
            .any(|c| matches!(c, HardwareCommand::ColourMap(_))));
        assert_eq!(
            cmds.iter()
                .filter(|c| matches!(c, HardwareCommand::FaderGradient { .. }))
                .count(),
            4
        );
        assert_eq!(broadcasts(&fx), 1);
        assert_eq!(
            fresh.engine().profile(),
            profile,
            "export of the loaded profile is identical"
        );
    }

    #[test]
    fn bad_profile_changes_nothing() {
        let mut hub = hub();
        let mut p = hub.engine().profile();
        p.channels[0].name = "Renamed".into();
        p.lighting[0].active = "not-a-colour".into();
        let fx = hub.handle(Input::Client(ClientMessage::LoadProfile { profile: p }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));
        assert_eq!(
            hub.engine().state().channels[0].name,
            "Mic",
            "nothing applied"
        );
    }

    #[test]
    fn assign_fader_rebinds_device_and_swaps_duplicates() {
        let mut hub = hub();
        // Put Headphones (8) on fader B.
        let fx = hub.handle(Input::Client(ClientMessage::AssignFader {
            fader: "B".into(),
            channel: ChannelId(8),
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::AssignFader {
            fader: Fader::B,
            channel: 8
        }));
        assert!(
            !hw(&fx)
                .iter()
                .any(|c| matches!(c, HardwareCommand::ChannelVolume { .. })),
            "never push an unverified level (e.g. headphones) when reassigning"
        );
        assert_eq!(hub.engine().fader_channel(1), Some(ChannelId(8)));
        // Put Mic (on A) onto D: A and D swap, so Game moves to A.
        let fx = hub.handle(Input::Client(ClientMessage::AssignFader {
            fader: "D".into(),
            channel: ChannelId(0),
        }));
        assert_eq!(hub.engine().fader_channel(3), Some(ChannelId(0)));
        assert_eq!(hub.engine().fader_channel(0), Some(ChannelId(3)));
        assert!(hw(&fx).contains(&&HardwareCommand::AssignFader {
            fader: Fader::A,
            channel: 3
        }));
        // Hardware events now follow the new binding: fader D moves Mic — after soft takeover
        // (this test hub has no motors), i.e. once the knob reaches Mic's current level.
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.79,
        }));
        hub.handle(Input::Hardware(HardwareEvent::FaderMoved {
            fader: Fader::D,
            position: 0.33,
        }));
        assert_eq!(volume(&hub, 0), 0.33);
    }

    #[test]
    fn outputs_and_feedback_routes_are_rejected() {
        let mut hub = hub();
        for (ch, bus) in [(8, 0), (5, 4), (1, 3)] {
            let fx = hub.handle(Input::Client(ClientMessage::SetRoute {
                channel: ChannelId(ch),
                bus: BusId(bus),
                gain: 1.0,
            }));
            assert!(
                matches!(fx.as_slice(), [Effect::Reply(ServerMessage::Error { .. })]),
                "{ch}->{bus}"
            );
        }
    }

    #[test]
    fn client_bleep_mutes_mic_while_held_then_restores() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::Bleep { pressed: true }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: true
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::ButtonLed {
            button: "bleep".into(),
            on: true
        }));
        assert!(hub.engine().state().bleep);
        let fx = hub.handle(Input::Client(ClientMessage::Bleep { pressed: false }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: false
        }));
        assert!(!hub.engine().state().bleep);
        // Hardware bleep: LED + state only (the device plays the tone and mutes itself).
        let fx = hub.handle(Input::Hardware(HardwareEvent::Button {
            id: "bleep".into(),
            pressed: true,
        }));
        assert!(!hw(&fx)
            .iter()
            .any(|c| matches!(c, HardwareCommand::ChannelMute { .. })));
        assert!(hub.engine().state().bleep);
    }

    #[test]
    fn first_lighting_change_syncs_fader_modes_and_schemes_apply_at_once() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "cough".into(),
            active: Some("#FFFFFF".into()),
            inactive: None,
            gradient: None,
        }));
        assert_eq!(
            hw(&fx)
                .iter()
                .filter(|c| matches!(c, HardwareCommand::FaderGradient { .. }))
                .count(),
            4
        );
        let fx = hub.handle(Input::Client(ClientMessage::SetLighting {
            zone: "cough".into(),
            active: Some("#000000".into()),
            inactive: None,
            gradient: None,
        }));
        assert_eq!(
            hw(&fx)
                .iter()
                .filter(|c| matches!(c, HardwareCommand::FaderGradient { .. }))
                .count(),
            0
        );

        let zones = vec![
            mixforge_protocol::LightingZone {
                zone: "fader_a".into(),
                active: "#1ACDFF".into(),
                inactive: "#A34DFF".into(),
                gradient: true,
            },
            mixforge_protocol::LightingZone {
                zone: "bleep".into(),
                active: "#FF8A33".into(),
                inactive: "#381E0B".into(),
                gradient: false,
            },
        ];
        let fx = hub.handle(Input::Client(ClientMessage::SetLightingScheme { zones }));
        assert_eq!(
            hw(&fx)
                .iter()
                .filter(|c| matches!(c, HardwareCommand::ColourMap(_)))
                .count(),
            1,
            "one device update"
        );
        let s = hub.engine().state();
        assert_eq!(
            s.lighting
                .iter()
                .find(|z| z.zone == "fader_a")
                .unwrap()
                .inactive,
            "#A34DFF"
        );
        assert_eq!(
            s.lighting
                .iter()
                .find(|z| z.zone == "bleep")
                .unwrap()
                .active,
            "#FF8A33"
        );
    }

    #[test]
    fn mic_mute_two_way_and_independent_of_fader_mute() {
        let mut hub = hub();
        // App button → device mute + LED + state.
        let fx = hub.handle(Input::Client(ClientMessage::SetMicMute { muted: true }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: true
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::ButtonLed {
            button: "cough".into(),
            on: true
        }));
        assert!(hub.engine().state().mic_muted);
        // Hardware cough press toggles it back off; the app sees it in the broadcast.
        let fx = hub.handle(Input::Hardware(HardwareEvent::Button {
            id: "cough".into(),
            pressed: true,
        }));
        assert!(!hub.engine().state().mic_muted);
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: false
        }));
        assert_eq!(broadcasts(&fx), 1);
        // Release does nothing.
        assert!(hub
            .handle(Input::Hardware(HardwareEvent::Button {
                id: "cough".into(),
                pressed: false
            }))
            .is_empty());
        // While mic mute is on, unmuting the Mic fader keeps the device muted.
        hub.handle(Input::Client(ClientMessage::SetMicMute { muted: true }));
        hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(0),
            muted: true,
        }));
        let fx = hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(0),
            muted: false,
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute {
            channel: 0,
            muted: true
        }));
    }

    fn fx_settings(fx: &[Effect]) -> (EffectSettings, u8, bool) {
        hw(fx)
            .into_iter()
            .find_map(|c| match c {
                HardwareCommand::Effects {
                    settings,
                    preset,
                    sync_encoders,
                } => Some((*settings, *preset, *sync_encoders)),
                _ => None,
            })
            .expect("effects command")
    }

    #[test]
    fn effects_from_app_and_hardware() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetEffects {
            enabled: Some(true),
            preset: None,
            reverb: Some(20),
            echo: None,
            pitch: Some(-5),
            gender: None,
            megaphone: Some(true),
            robot: None,
            hardtune: None,
        }));
        let (s, preset, sync) = fx_settings(&fx);
        assert!(s.enabled && s.megaphone && s.reverb == 20 && s.pitch == -5 && preset == 0);
        assert!(sync, "app changes move the physical knobs");

        // Hardware knob: reverb +2 → no knob sync back.
        let fx = hub.handle(Input::Hardware(HardwareEvent::EncoderTurned {
            id: "reverb".into(),
            delta: 2,
        }));
        let (s, _, sync) = fx_settings(&fx);
        assert_eq!(s.reverb, 22);
        assert!(!sync);
        // Clamped at the top.
        hub.handle(Input::Hardware(HardwareEvent::EncoderTurned {
            id: "reverb".into(),
            delta: 9,
        }));
        assert_eq!(hub.engine().active_preset().reverb, 24);

        // Hardware buttons: preset 3 (its own knob values) and FX off.
        let fx = hub.handle(Input::Hardware(HardwareEvent::Button {
            id: "effect_select_3".into(),
            pressed: true,
        }));
        let (s, preset, sync) = fx_settings(&fx);
        assert_eq!((preset, s.reverb, s.megaphone, sync), (2, 6, false, true));
        let fx = hub.handle(Input::Hardware(HardwareEvent::Button {
            id: "effect_fx".into(),
            pressed: true,
        }));
        assert!(!fx_settings(&fx).0.enabled);
        // Robot toggles on the active preset.
        hub.handle(Input::Hardware(HardwareEvent::Button {
            id: "effect_robot".into(),
            pressed: true,
        }));
        assert!(hub.engine().active_preset().robot);

        // Out of range is rejected.
        let fx = hub.handle(Input::Client(ClientMessage::SetEffects {
            enabled: None,
            preset: Some(7),
            reverb: None,
            echo: None,
            pitch: None,
            gender: None,
            megaphone: None,
            robot: None,
            hardtune: None,
        }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));
    }

    fn scribbles(fx: &[Effect]) -> Vec<(Fader, String, Option<char>)> {
        hw(fx)
            .into_iter()
            .filter_map(|c| match c {
                HardwareCommand::Scribble { fader, text, glyph } => {
                    Some((*fader, text.clone(), *glyph))
                }
                _ => None,
            })
            .collect()
    }

    #[test]
    fn scribble_strips_follow_name_icon_and_assignment() {
        let mut hub = hub();
        assert_eq!(
            scribbles(&hub.full_hardware_sync()).len(),
            4,
            "all four strips at startup"
        );
        let fx = hub.handle(Input::Client(ClientMessage::SetChannelName {
            channel: ChannelId(1),
            name: "Discord".into(),
        }));
        assert_eq!(
            scribbles(&fx),
            vec![(Fader::B, "Discord".into(), Some('\u{E8BD}'))]
        );
        let fx = hub.handle(Input::Client(ClientMessage::SetChannelIcon {
            channel: ChannelId(1),
            icon: Some("headset".into()),
        }));
        assert_eq!(
            scribbles(&fx),
            vec![(Fader::B, "Discord".into(), Some('\u{E95B}'))]
        );
        // A channel that isn't on a fader: no strip update.
        let fx = hub.handle(Input::Client(ClientMessage::SetChannelName {
            channel: ChannelId(6),
            name: "Synth".into(),
        }));
        assert!(scribbles(&fx).is_empty());
        // Assigning it to C draws it there.
        let fx = hub.handle(Input::Client(ClientMessage::AssignFader {
            fader: "C".into(),
            channel: ChannelId(6),
        }));
        assert!(scribbles(&fx).contains(&(Fader::C, "Synth".into(), Some('\u{E7F5}'))));
    }

    #[test]
    fn mic_mute_also_mutes_the_input_so_the_monitor_goes_silent() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetMicMute { muted: true }));
        assert!(hw(&fx).contains(&&HardwareCommand::MicInputMute(true)));
        let fx = hub.handle(Input::Client(ClientMessage::SetMute {
            channel: ChannelId(0),
            muted: false,
        }));
        assert!(
            hw(&fx).contains(&&HardwareCommand::MicInputMute(true)),
            "still muted by mic mute"
        );
        let fx = hub.handle(Input::Client(ClientMessage::SetMicMute { muted: false }));
        assert!(hw(&fx).contains(&&HardwareCommand::MicInputMute(false)));
        // Bleep from the app mutes the channel mix but leaves the preamp monitor open.
        let fx = hub.handle(Input::Client(ClientMessage::Bleep { pressed: true }));
        assert!(hw(&fx).contains(&&HardwareCommand::MicInputMute(false)));
        assert!(hw(&fx).contains(&&HardwareCommand::ChannelMute { channel: 0, muted: true }));
    }

    #[test]
    fn mic_settings_and_effect_details_reach_the_device() {
        let mut hub = hub();
        let mut m = mixforge_protocol::MicSettings {
            gate_threshold: -30,
            ..Default::default()
        };
        m.eq_gains[0] = 5;
        let fx = hub.handle(Input::Client(ClientMessage::SetMicSettings {
            settings: m.clone(),
        }));
        assert!(hw(&fx).contains(&&HardwareCommand::MicDsp(m.clone())));
        m.comp_ratio = 99;
        let fx = hub.handle(Input::Client(ClientMessage::SetMicSettings { settings: m }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));

        let mut d = mixforge_protocol::EffectDetails {
            hardtune_rate: 90,
            ..Default::default()
        };
        let fx = hub.handle(Input::Client(ClientMessage::SetEffectDetails {
            details: d,
        }));
        assert_eq!(fx_settings(&fx).0.details.hardtune_rate, 90);
        d.hardtune_window = 700;
        let fx = hub.handle(Input::Client(ClientMessage::SetEffectDetails {
            details: d,
        }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));
    }

    #[test]
    fn profile_without_mic_section_leaves_the_device_dsp_alone() {
        let mut hub = hub();
        let mut p = hub.engine.profile();
        p.mic = None;
        let fx = hub.handle(Input::Client(ClientMessage::LoadProfile {
            profile: p.clone(),
        }));
        assert!(!hw(&fx)
            .iter()
            .any(|c| matches!(c, HardwareCommand::MicDsp(_))));
        p.mic = Some(Default::default());
        let fx = hub.handle(Input::Client(ClientMessage::LoadProfile { profile: p }));
        assert!(hw(&fx)
            .iter()
            .any(|c| matches!(c, HardwareCommand::MicDsp(_))));
    }

    #[test]
    fn mic_monitor_is_synced_by_level_and_never_channel_muted() {
        let mut hub = hub();
        // Connect: stale output mutes are cleared and the Mic Monitor level is pushed.
        let sync = hub.full_hardware_sync();
        let cmds = hw(&sync);
        assert!(cmds.contains(&&HardwareCommand::ChannelMute { channel: 9, muted: false }));
        assert!(cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 9, .. })));
        assert!(!cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 8, .. })));

        // Turning the monitor off = level 0, no channel-state mute.
        let fx = hub.handle(Input::Client(ClientMessage::SetMute { channel: ChannelId(9), muted: true }));
        let cmds = hw(&fx);
        assert!(cmds.contains(&&HardwareCommand::ChannelVolume { channel: 9, volume: 0.0 }));
        assert!(!cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelMute { channel: 9, .. })));
        // On again: the previous level comes back.
        let fx = hub.handle(Input::Client(ClientMessage::SetMute { channel: ChannelId(9), muted: false }));
        assert!(hw(&fx).iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 9, volume } if *volume > 0.0)));

        // A profile load brings the Mic Monitor level along (but not Headphones).
        let mut p = hub.engine.profile();
        for c in &mut p.channels {
            if c.id == ChannelId(9) {
                c.volume = 0.65;
            }
        }
        let fx = hub.handle(Input::Client(ClientMessage::LoadProfile { profile: p }));
        let cmds = hw(&fx);
        assert!(cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 9, volume } if (*volume - 0.65).abs() < 1e-6)));
        assert!(!cmds.iter().any(|c| matches!(c, HardwareCommand::ChannelVolume { channel: 8, .. })));
    }

    #[test]
    fn sampler_buttons_reach_clients_and_their_leds_are_app_owned() {
        let mut hub = hub();
        let fx = hub.handle(Input::Hardware(HardwareEvent::Button { id: "sampler_top_left".into(), pressed: true }));
        assert!(matches!(fx.as_slice(), [Effect::Broadcast(ServerMessage::Button { pressed: true, .. })]));
        let fx = hub.handle(Input::Client(ClientMessage::SetButtonLeds {
            leds: vec![
                mixforge_protocol::ButtonLedState { button: "sampler_select_b".into(), on: true },
                mixforge_protocol::ButtonLedState { button: "cough".into(), on: true },
            ],
        }));
        assert_eq!(hw(&fx), vec![&HardwareCommand::ButtonLed { button: "sampler_select_b".into(), on: true }]);
    }

    #[test]
    fn broadcast_and_chat_mic_accept_volume_and_mute() {
        let mut hub = hub();
        for msg in [
            ClientMessage::SetVolume {
                channel: ChannelId(11),
                volume: 0.5,
            },
            ClientMessage::SetMute {
                channel: ChannelId(12),
                muted: true,
            },
            ClientMessage::AssignFader {
                fader: "A".into(),
                channel: ChannelId(11),
            },
        ] {
            let fx = hub.handle(Input::Client(msg));
            assert!(!fx.iter().any(|e| matches!(e, Effect::Reply(ServerMessage::Error { .. }))));
        }
    }

    #[test]
    fn invalid_ui_command_only_errors_to_sender() {
        let mut hub = hub();
        let fx = hub.handle(Input::Client(ClientMessage::SetVolume {
            channel: ChannelId(99),
            volume: 0.5,
        }));
        assert!(matches!(
            fx.as_slice(),
            [Effect::Reply(ServerMessage::Error { .. })]
        ));
    }

    #[test]
    fn mic_level_feeds_meter_without_state_broadcast() {
        let mut hub = hub();
        let _ = hub.engine.set_volume(ChannelId(0), 1.0);
        let fx = hub.handle(Input::Hardware(HardwareEvent::MicLevel(0.5)));
        assert!(fx.is_empty());
        let ServerMessage::Meters { levels } = hub.meters_message() else {
            panic!()
        };
        assert_eq!(
            levels
                .iter()
                .find(|m| m.channel == ChannelId(0))
                .unwrap()
                .peak,
            0.5
        );
    }

    #[test]
    fn disconnect_clears_hardware_and_notifies_clients() {
        let mut hub = hub();
        hub.set_hardware(vec![mixforge_usb::Model::Full.info(None)]);
        let fx = hub.handle(Input::Hardware(HardwareEvent::Disconnected));
        assert!(hub.engine().state().hardware.is_empty());
        assert_eq!(broadcasts(&fx), 1);
    }
}
