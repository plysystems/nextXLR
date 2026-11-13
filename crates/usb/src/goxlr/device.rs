//! `GoXlrDevice`: the real GoXLR as a `HardwareDevice` (Windows, via the TC-Helicon driver).

use std::time::{Duration, Instant};

use anyhow::{bail, Context, Result};
use mixforge_protocol::HardwareInfo;

use super::protocol::{
    decode_response, encode_request, goxlr_channel, mic_level_to_linear, parse_firmware, ButtonLed,
    Command, GoXlrChannel, InputState, BUTTON_COUNT, FADER_MUTE_BITS,
};
use super::effects::{effect_values, encode_params, ENC_ECHO, ENC_GENDER, ENC_PITCH, ENC_REVERB};
use super::lighting::{build_colour_map, MapFormat};
use super::protocol::{FX_BIT, HARDTUNE_BIT, MEGAPHONE_BIT, PRESET_BITS, ROBOT_BIT};
use super::scribble;
use super::routing;
use super::tusb::TusbDevice;
use crate::{Fader, HardwareCommand, HardwareDevice, HardwareEvent, Model};

const RESPONSE_TIMEOUT: Duration = Duration::from_secs(1);

/// Default fader page, as GoXLR channels (matches the engine's default A–D assignment).
const DEFAULT_PAGE: [GoXlrChannel; 4] =
    [GoXlrChannel::Mic, GoXlrChannel::Chat, GoXlrChannel::Music, GoXlrChannel::Game];

pub struct GoXlrDevice {
    dev: TusbDevice,
    model: Model,
    serial: Option<String>,
    command_index: u16,
    /// An input-changed notification arrived and hasn't been read yet.
    input_pending: bool,
    last_input: Option<InputState>,
    leds: [ButtonLed; BUTTON_COUNT],
    /// Full GoXLR faders are motorised: after we set a fader-bound volume the motor travels,
    /// overshoots and settles. Reports for that fader are ignored until they stop changing
    /// for `MOTOR_SETTLE` (capped at `MOTOR_MAX`), so the travel isn't mistaken for a human.
    motor: [Option<MotorWindow>; 4],
    last_mic_poll: Instant,
    /// Colour-map layout supported by this device's firmware.
    map_format: MapFormat,
    firmware: Option<(u32, u32, u32)>,
    /// Firmware has the second stream mix → 26-byte routing rows.
    mix2: bool,
    /// Which GoXLR channel each physical fader currently controls.
    fader_page: [GoXlrChannel; 4],
    /// Fonts for the scribble strips (loaded on first use) and the last image per strip.
    scribble_fonts: Option<Result<scribble::Fonts, String>>,
    scribble_cache: [Option<[u8; scribble::BYTES]>; 4],
    /// Standby: lighting, LEDs and scribble strips are dark. Updates are only remembered.
    sleeping: bool,
    /// Last colour map sent (or requested while asleep) — restored on wake.
    colour_map: Option<Vec<u8>>,
    /// Last fader LED display mode per fader — restored on wake.
    fader_gradient: [Option<bool>; 4],
}

const MIC_POLL: Duration = Duration::from_millis(33);

#[derive(Clone, Copy)]
struct MotorWindow {
    quiet_until: Instant,
    deadline: Instant,
}

const MOTOR_START: Duration = Duration::from_millis(300);
const MOTOR_SETTLE: Duration = Duration::from_millis(150);
const MOTOR_MAX: Duration = Duration::from_millis(1500);

impl GoXlrDevice {
    /// Opens the GoXLR through the driver and runs the init handshake.
    /// Fails if the driver isn't installed or no GoXLR is attached.
    pub fn open() -> Result<Self> {
        let dev = TusbDevice::open_first()?;
        if dev.vendor_id != crate::VID_TC_HELICON {
            bail!("driver device has unexpected vendor id {:#06x}", dev.vendor_id);
        }
        let model = Model::from_pid(dev.product_id)
            .with_context(|| format!("unknown GoXLR product id {:#06x}", dev.product_id))?;
        let mut me = Self {
            dev,
            model,
            serial: None,
            command_index: 0,
            input_pending: true, // read initial fader positions on first wait
            last_input: None,
            // Colour2 = each button's "inactive" colour from the colour map; Colour1 = "active".
            leds: [ButtonLed::Colour2; BUTTON_COUNT],
            motor: [None; 4],
            last_mic_poll: Instant::now(),
            map_format: MapFormat::V1_3_40,
            firmware: None,
            mix2: false,
            fader_page: DEFAULT_PAGE,
            scribble_fonts: None,
            scribble_cache: [None; 4],
            sleeping: false,
            colour_map: None,
            fader_gradient: [None; 4],
        };
        me.handshake()?;
        me.serial = me.read_serial().ok();
        me.firmware = me
            .request(Command::GetFirmwareVersion, &[])
            .ok()
            .and_then(|b| parse_firmware(&b));
        if let Some(fw) = me.firmware {
            me.map_format = MapFormat::for_firmware(model, fw);
            me.mix2 = routing::supports_mix2(model, fw);
            tracing::info!(firmware = ?fw, format = ?me.map_format, "GoXLR firmware");
        }
        me.bind_faders()?;
        me.push_leds()?;
        Ok(me)
    }

    fn handshake(&mut self) -> Result<()> {
        // Activates the vendor interface (and audio on Windows).
        self.dev.vendor_in(0, 0, 0, 24).context("initial read")?;
        // Soft reset, then read its response.
        self.dev.vendor_out(1, 0, 0, &[]).context("reset")?;
        if !self.dev.wait_data(RESPONSE_TIMEOUT, &mut self.input_pending) {
            bail!("no response to reset");
        }
        self.dev.vendor_in(3, 0, 0, 1040).context("reset response")?;
        Ok(())
    }

    fn request(&mut self, command: Command, body: &[u8]) -> Result<Vec<u8>> {
        match self.request_once(command, body) {
            Err(e) if e.to_string().contains("index mismatch") => {
                tracing::debug!("{e}; resyncing command index");
                self.request_once(Command::ResetCommandIndex, &[])?;
                self.request_once(command, body)
            }
            other => other,
        }
    }

    fn request_once(&mut self, command: Command, body: &[u8]) -> Result<Vec<u8>> {
        if command == Command::ResetCommandIndex {
            self.command_index = 0;
        } else {
            self.command_index = self.command_index.wrapping_add(1).max(1);
        }
        let index = self.command_index;
        self.dev.vendor_out(2, 0, 0, &encode_request(command, index, body))?;
        if !self.dev.wait_data(RESPONSE_TIMEOUT, &mut self.input_pending) {
            bail!("timeout waiting for response to {command:?}");
        }
        let raw = self.dev.vendor_in(3, 0, 0, 1040)?;
        let (got, body) = decode_response(&raw).context("short response")?;
        if got != index {
            bail!("index mismatch: sent {index}, got {got}");
        }
        Ok(body.to_vec())
    }

    fn read_serial(&mut self) -> Result<String> {
        let body = self.request(Command::GetSerialNumber, &[])?;
        let raw = &body[..body.len().min(24)];
        let end = raw.iter().position(|&b| b == 0).unwrap_or(raw.len());
        Ok(String::from_utf8_lossy(&raw[..end]).into_owned())
    }

    /// Make the physical faders control the channels the hub has bound to them.
    fn bind_faders(&mut self) -> Result<()> {
        for (fader, channel) in Fader::ALL.into_iter().zip(self.fader_page) {
            self.request(Command::SetFader(fader), &[channel as u8, 0, 0, 0])?;
        }
        Ok(())
    }

    fn push_leds(&mut self) -> Result<()> {
        if self.sleeping {
            return Ok(()); // remembered in `self.leds`, pushed on wake
        }
        let body = self.leds.map(|l| l as u8);
        self.request(Command::SetButtonStates, &body)?;
        Ok(())
    }

    /// Standby: black colour map (buttons, faders, encoders, logo), every button LED on its
    /// (now black) inactive colour and blank scribble strips. Each step is best effort so one
    /// failing write never leaves the device half-lit.
    fn enter_sleep(&mut self) -> Result<()> {
        if self.sleeping {
            return Ok(());
        }
        let mut first_err = None;
        let black = vec![0u8; self.map_format.byte_len()];
        if let Err(e) = self.request(Command::SetColourMap, &black) {
            first_err.get_or_insert(e);
        }
        let off = [ButtonLed::Colour2 as u8; BUTTON_COUNT];
        if let Err(e) = self.request(Command::SetButtonStates, &off) {
            first_err.get_or_insert(e);
        }
        if self.model != Model::Mini {
            let blank = [0u8; scribble::BYTES];
            for fader in Fader::ALL {
                if let Err(e) = self.request(Command::SetScribble(fader), &blank) {
                    first_err.get_or_insert(e);
                }
            }
        }
        self.sleeping = true;
        tracing::info!("GoXLR entered standby");
        first_err.map_or(Ok(()), Err)
    }

    /// Leave standby: restore colour map, fader display modes, button LEDs and scribbles.
    fn leave_sleep(&mut self) -> Result<()> {
        if !self.sleeping {
            return Ok(());
        }
        self.sleeping = false;
        if let Some(map) = self.colour_map.clone() {
            self.request(Command::SetColourMap, &map)?;
        }
        if self.model != Model::Mini {
            for fader in Fader::ALL {
                if let Some(g) = self.fader_gradient[fader.index()] {
                    self.request(Command::SetFaderDisplayMode(fader), &[u8::from(g), 0])?;
                }
                if let Some(image) = self.scribble_cache[fader.index()] {
                    self.request(Command::SetScribble(fader), &image)?;
                }
            }
        }
        self.push_leds()?;
        tracing::info!("GoXLR left standby");
        Ok(())
    }

    fn read_inputs(&mut self) -> Result<Vec<HardwareEvent>> {
        let body = self.request(Command::GetButtonStates, &[])?;
        let state = InputState::parse(&body).context("short button-state response")?;
        let mut events = state.diff(self.last_input.as_ref());
        let now = Instant::now();
        events.retain(|ev| {
            let HardwareEvent::FaderMoved { fader, .. } = ev else { return true };
            let slot = &mut self.motor[fader.index()];
            match slot {
                Some(w) if now < w.quiet_until && now < w.deadline => {
                    w.quiet_until = now + MOTOR_SETTLE; // still moving: motor travel
                    false
                }
                Some(_) => {
                    *slot = None; // motor settled; this movement is human
                    true
                }
                None => true,
            }
        });
        self.last_input = Some(state);
        Ok(events)
    }
}

impl HardwareDevice for GoXlrDevice {
    fn info(&self) -> HardwareInfo {
        self.model.info(self.serial.clone())
    }

    fn wait_events(&mut self, timeout: Duration) -> Result<Vec<HardwareEvent>> {
        if !self.input_pending {
            let n = self.dev.wait_notifications(timeout);
            self.input_pending = n.input_changed;
        }
        let mut events = Vec::new();
        if self.input_pending {
            self.input_pending = false;
            events = self.read_inputs()?;
        }
        // The mic level is only available on request: poll it at ~30 Hz.
        if self.last_mic_poll.elapsed() >= MIC_POLL {
            self.last_mic_poll = Instant::now();
            let body = self.request(Command::GetMicrophoneLevel, &[])?;
            if body.len() >= 2 {
                let raw = u16::from_le_bytes([body[0], body[1]]);
                events.push(HardwareEvent::MicLevel(mic_level_to_linear(raw)));
            }
        }
        Ok(events)
    }

    fn apply(&mut self, cmd: &HardwareCommand) -> Result<()> {
        match cmd {
            HardwareCommand::ChannelVolume { channel, volume } => {
                if let Some(ch) = goxlr_channel(*channel) {
                    let v = (volume.clamp(0.0, 1.0) * 255.0).round() as u8;
                    if self.model == Model::Full {
                        if let Some(i) = self.fader_page.iter().position(|&c| c == ch) {
                            let now = Instant::now();
                            self.motor[i] = Some(MotorWindow {
                                quiet_until: now + MOTOR_START,
                                deadline: now + MOTOR_MAX,
                            });
                        }
                    }
                    self.request(Command::SetChannelVolume(ch), &[v])?;
                }
            }
            HardwareCommand::ChannelMute { channel, muted } => {
                if let Some(ch) = goxlr_channel(*channel) {
                    self.request(Command::SetChannelState(ch), &[u8::from(*muted)])?;
                }
            }
            HardwareCommand::MuteLed { fader, on } => {
                let led = if *on { ButtonLed::Colour1 } else { ButtonLed::Colour2 };
                self.leds[FADER_MUTE_BITS[fader.index()] as usize] = led;
                self.push_leds()?;
            }
            // The fader LED bar follows the device's own channel volume, which ChannelVolume
            // already set. Scribble-strip labels need a 1024-byte bitmap renderer (later).
            HardwareCommand::FaderDisplay { .. } => {}
            HardwareCommand::ColourMap(zones) => {
                let map = build_colour_map(zones, self.map_format);
                if !self.sleeping {
                    self.request(Command::SetColourMap, &map)?;
                }
                self.colour_map = Some(map);
            }
            HardwareCommand::Routing { channel, buses } => {
                let Some((left_in, right_in)) = routing::input_ids(*channel) else { return Ok(()) };
                let (left, right) = routing::build_rows(buses, self.mix2);
                self.request(Command::SetRouting(left_in), &left)?;
                self.request(Command::SetRouting(right_in), &right)?;
            }
            HardwareCommand::AssignFader { fader, channel } => {
                if let Some(ch) = goxlr_channel(*channel) {
                    // The full GoXLR then drives its motor to the new channel's level; hide that
                    // travel so it isn't mistaken for a human move (same window as volume changes).
                    if self.model == Model::Full {
                        let now = Instant::now();
                        self.motor[fader.index()] = Some(MotorWindow {
                            quiet_until: now + MOTOR_START,
                            deadline: now + MOTOR_MAX,
                        });
                    }
                    self.request(Command::SetFader(*fader), &[ch as u8, 0, 0, 0])?;
                    self.fader_page[fader.index()] = ch;
                }
            }
            HardwareCommand::Effects { settings, preset, sync_encoders } => {
                if self.model == Model::Mini {
                    return Ok(()); // the Mini has no voice-effects DSP
                }
                self.request(Command::SetEffectParameters, &encode_params(&effect_values(settings)))?;
                if *sync_encoders {
                    for (enc, v) in [(ENC_PITCH, settings.pitch), (ENC_GENDER, settings.gender), (ENC_REVERB, settings.reverb), (ENC_ECHO, settings.echo)] {
                        self.request(Command::SetEncoderValue(enc), &[v as u8])?;
                    }
                }
                let led = |on: bool| if on { ButtonLed::Colour1 } else { ButtonLed::Colour2 };
                self.leds[FX_BIT as usize] = led(settings.enabled);
                self.leds[MEGAPHONE_BIT as usize] = led(settings.megaphone);
                self.leds[ROBOT_BIT as usize] = led(settings.robot);
                self.leds[HARDTUNE_BIT as usize] = led(settings.hardtune);
                for (i, bit) in PRESET_BITS.iter().enumerate() {
                    self.leds[*bit as usize] = led(i == *preset as usize);
                }
                self.push_leds()?;
            }
            HardwareCommand::MicDsp(settings) => {
                let values = super::mic::mic_values(settings, self.model == Model::Full);
                self.request(Command::SetEffectParameters, &encode_params(&values))?;
            }
            HardwareCommand::MicInputMute(muted) => {
                self.request(Command::SetEffectParameters, &encode_params(&[(super::mic::MIC_INPUT_MUTE, *muted as i32)]))?;
            }
            HardwareCommand::Scribble { fader, text, glyph } => {
                if self.model == Model::Mini {
                    return Ok(()); // the Mini has no scribble strips
                }
                let fonts = self.scribble_fonts.get_or_insert_with(|| scribble::Fonts::load_system().map_err(|e| e.to_string()));
                let Ok(fonts) = fonts else {
                    tracing::warn!("scribble strips disabled: fonts not available");
                    return Ok(());
                };
                let image = scribble::render(fonts, text, *glyph);
                if self.sleeping {
                    self.scribble_cache[fader.index()] = Some(image); // shown on wake
                } else if self.scribble_cache[fader.index()] != Some(image) {
                    self.request(Command::SetScribble(*fader), &image)?;
                    self.scribble_cache[fader.index()] = Some(image);
                }
            }
            HardwareCommand::ButtonLed { button, on } => {
                let Some(bit) = super::protocol::button_bit(button) else { return Ok(()) };
                self.leds[bit as usize] = if *on { ButtonLed::Colour1 } else { ButtonLed::Colour2 };
                self.push_leds()?;
            }
            HardwareCommand::FaderGradient { fader, gradient } => {
                if self.model == Model::Mini {
                    return Ok(()); // GoXLR Mini has manual faders without display mode registers
                }
                // [gradient, meter]: the level meter on the fader LEDs stays off.
                self.fader_gradient[fader.index()] = Some(*gradient);
                if !self.sleeping {
                    self.request(Command::SetFaderDisplayMode(*fader), &[u8::from(*gradient), 0])?;
                }
            }
            HardwareCommand::Sleep => self.enter_sleep()?,
            HardwareCommand::Wake => self.leave_sleep()?,
        }
        Ok(())
    }
}
