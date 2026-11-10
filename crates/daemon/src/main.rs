#![windows_subsystem = "windows"]

//! `mixforged` — owns the state hub and exposes it over `\\.\pipe\mixforge` (desktop app)
//! and `ws://<bind>/ws` (remote panels, Stream Deck). Same JSON protocol on both.
//!
//! Environment:
//! - `MIXFORGE_BIND`     bind address (default `127.0.0.1:14565`)
//! - `MIXFORGE_NO_AUDIO` `1` = run the engine without audio devices
//! - `MIXFORGE_SIM_HW`   `full`/`1` or `mini` = attach a simulated GoXLR / GoXLR Mini (enables `/dev/sim/...`)

mod hub;
mod icons;
mod session;

use std::time::Duration;

use anyhow::Result;
use axum::extract::{Query, State};
use axum::response::IntoResponse;
use axum::routing::{get, post};
use axum::Router;
use hub::{Effect, Hub, Input};
use mixforge_engine::Engine;
use mixforge_protocol::{ClientMessage, ServerMessage, DEFAULT_PORT};
use mixforge_usb::{Fader, HardwareDevice, HardwareEvent, HardwareThread, SimDevice, SimHandle};
use serde_json::json;
use tokio::sync::{broadcast, mpsc, oneshot};
use tracing_subscriber::EnvFilter;

/// What the hub task receives. Client inputs carry a reply channel for sender-only answers.
enum HubMsg {
    Client {
        msg: ClientMessage,
        reply: oneshot::Sender<Vec<ServerMessage>>,
    },
    Hardware(HardwareEvent),
}

#[derive(Clone)]
struct AppState {
    hub: mpsc::UnboundedSender<HubMsg>,
    events: broadcast::Sender<ServerMessage>,
    sim: Option<SimHandle>,
}

fn env_flag(name: &str) -> bool {
    std::env::var(name).is_ok_and(|v| v == "1")
}

#[tokio::main]
async fn main() -> Result<()> {
    #[cfg(windows)]
    unsafe {
        extern "system" {
            fn AttachConsole(dw_process_id: u32) -> i32;
        }
        const ATTACH_PARENT_PROCESS: u32 = 0xFFFF_FFFF;
        let _ = AttachConsole(ATTACH_PARENT_PROCESS);
    }

    tracing_subscriber::fmt()
        .with_env_filter(EnvFilter::try_from_default_env().unwrap_or_else(|_| "info".into()))
        .init();

    let engine = if env_flag("MIXFORGE_NO_AUDIO") {
        Engine::headless()
    } else {
        Engine::start()
    };
    let mut hub = Hub::new(engine);

    let (hub_tx, hub_rx) = mpsc::unbounded_channel::<HubMsg>();
    let (events, _) = broadcast::channel::<ServerMessage>(256);

    // ---- Hardware --------------------------------------------------------------------------
    let mut hardware_info = mixforge_usb::discover();
    let mut sim = None;
    let forward = {
        let tx = hub_tx.clone();
        move |ev| {
            let _ = tx.send(HubMsg::Hardware(ev));
        }
    };
    let sim_model = match std::env::var("MIXFORGE_SIM_HW").as_deref() {
        Ok("1") | Ok("full") => Some(mixforge_usb::Model::Full),
        Ok("mini") => Some(mixforge_usb::Model::Mini),
        _ => None,
    };
    let hw_thread: Option<HardwareThread> = if let Some(model) = sim_model {
        let (device, handle) = SimDevice::with_model(model);
        hardware_info = vec![device.info()];
        sim = Some(handle);
        Some(mixforge_usb::spawn_hardware_thread(device, forward)?)
    } else if !hardware_info.is_empty() {
        open_goxlr(&mut hardware_info, forward)
    } else {
        tracing::info!("no GoXLR detected — running as software mixer");
        None
    };
    for h in &hardware_info {
        tracing::info!(model = %h.model, kind = ?h.kind, serial = ?h.serial, "control surface");
    }
    hub.set_motorized_faders(
        hw_thread.is_some()
            && hardware_info
                .iter()
                .any(|h| h.capabilities.motorized_faders),
    );
    hub.set_hardware(hardware_info);
    if let Some(hw) = &hw_thread {
        for e in hub.full_hardware_sync() {
            if let Effect::Hardware(cmd) = e {
                hw.send(cmd);
            }
        }
    }

    // Startup state: the default configuration (and default mic profile) the user chose in the app.
    for e in load_startup_defaults(&mut hub) {
        if let (Effect::Hardware(cmd), Some(hw)) = (e, &hw_thread) {
            hw.send(cmd);
        }
    }

    tokio::spawn(run_hub(hub, hub_rx, events.clone(), hw_thread, hub_tx.clone(), sim_model.is_some()));

    // ---- API -------------------------------------------------------------------------------
    let app = AppState {
        hub: hub_tx,
        events,
        sim,
    };

    #[cfg(windows)]
    {
        let app = app.clone();
        tokio::spawn(async move {
            if let Err(e) = session::serve_pipe(app).await {
                tracing::error!("named pipe server stopped: {e} (is another mixforged running?)");
            }
        });
    }

    let router = Router::new()
        .route("/ws", get(session::ws_handler))
        .route("/health", get(|| async { "ok" }))
        .route("/api/status", get(api_status))
        .route("/api/fader", get(api_fader).post(api_fader))
        .route("/api/mute", get(api_mute).post(api_mute))
        .route("/api/route", get(api_route).post(api_route))
        .route("/api/bleep", get(api_bleep).post(api_bleep))
        .route("/api/mic-mute", get(api_mic_mute).post(api_mic_mute))
        .route("/api/profile", get(api_profile).post(api_profile))
        .route("/dev/sim/fader", post(sim_fader))
        .route("/dev/sim/mute", post(sim_mute))
        .route("/dev/sim/received", get(sim_received))
        .layer(axum::middleware::from_fn(cors_middleware))
        .with_state(app);

    let bind =
        std::env::var("MIXFORGE_BIND").unwrap_or_else(|_| format!("127.0.0.1:{DEFAULT_PORT}"));
    let listener = tokio::net::TcpListener::bind(&bind).await?;
    tracing::info!("WebSocket API on ws://{bind}/ws");

    axum::serve(listener, router)
        .with_graceful_shutdown(async {
            let _ = tokio::signal::ctrl_c().await;
            tracing::info!("shutting down");
        })
        .await?;
    Ok(())
}

#[cfg(windows)]
fn open_goxlr(
    info: &mut Vec<mixforge_protocol::HardwareInfo>,
    forward: impl Fn(HardwareEvent) + Send + 'static,
) -> Option<HardwareThread> {
    match mixforge_usb::goxlr::GoXlrDevice::open() {
        Ok(device) => {
            *info = vec![device.info()];
            match mixforge_usb::spawn_hardware_thread(device, forward) {
                Ok(t) => Some(t),
                Err(e) => {
                    tracing::error!("could not start hardware thread: {e}");
                    None
                }
            }
        }
        Err(e) => {
            tracing::warn!(
                "GoXLR found but could not be opened: {e:#}. Is the official GoXLR App or \
                 goxlr-utility running? Only one program can control the device."
            );
            None
        }
    }
}

#[cfg(not(windows))]
fn open_goxlr(
    _info: &mut Vec<mixforge_protocol::HardwareInfo>,
    _forward: impl Fn(HardwareEvent) + Send + 'static,
) -> Option<HardwareThread> {
    tracing::warn!("GoXLR control is only implemented on Windows so far");
    None
}

/// `%LOCALAPPDATA%\NextXLR\default-profile.json` (whole setup) and
/// `default-mic-profile.json` (mic processing), written by the app's "Set as default".
fn load_startup_defaults(hub: &mut Hub) -> Vec<Effect> {
    let Ok(base) = std::env::var("LOCALAPPDATA") else {
        return Vec::new();
    };
    // NEXT : XLR data folder (older builds used "GoXLRNext"; the app moves it on first start).
    let dir = ["NextXLR", "GoXLRNext"]
        .iter()
        .map(|d| std::path::Path::new(&base).join(d))
        .find(|d| d.exists())
        .unwrap_or_else(|| std::path::Path::new(&base).join("NextXLR"));
    let read = |name: &str| -> Option<serde_json::Value> {
        let text = std::fs::read_to_string(dir.join(name)).ok()?;
        let v: serde_json::Value = serde_json::from_str(&text).ok()?;
        Some(
            v.get("profile")
                .or_else(|| v.get("mic"))
                .cloned()
                .unwrap_or(v),
        )
    };
    let mut out = Vec::new();
    if let Some(v) = read("default-profile.json") {
        match serde_json::from_value::<mixforge_protocol::Profile>(v) {
            Ok(profile) => {
                tracing::info!("loading default profile");
                out.extend(hub.handle(Input::Client(ClientMessage::LoadProfile { profile })));
            }
            Err(e) => tracing::warn!("default profile ignored: {e}"),
        }
    }
    if let Some(v) = read("default-mic-profile.json") {
        match serde_json::from_value::<mixforge_protocol::MicSettings>(v) {
            Ok(settings) => {
                tracing::info!("loading default mic profile");
                out.extend(hub.handle(Input::Client(ClientMessage::SetMicSettings { settings })));
            }
            Err(e) => tracing::warn!("default mic profile ignored: {e}"),
        }
    }
    out.retain(|e| !matches!(e, Effect::Reply(ServerMessage::Error { .. })));
    out
}

/// The single owner of all state. Nothing else mutates the mixer — that is what keeps UI,
/// audio and hardware consistent: every change, whatever its source, takes this one path.
async fn run_hub(
    mut hub: Hub,
    mut rx: mpsc::UnboundedReceiver<HubMsg>,
    events: broadcast::Sender<ServerMessage>,
    mut hw: Option<HardwareThread>,
    hub_tx: mpsc::UnboundedSender<HubMsg>,
    is_sim: bool,
) {
    let mut meter_tick = tokio::time::interval(Duration::from_millis(33));
    meter_tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);

    let mut hw_reconnect_tick = tokio::time::interval(Duration::from_secs(2));
    hw_reconnect_tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);

    loop {
        tokio::select! {
            msg = rx.recv() => {
                let Some(msg) = msg else { break };
                let (input, reply) = match msg {
                    HubMsg::Client { msg, reply } => (Input::Client(msg), Some(reply)),
                    HubMsg::Hardware(ev) => {
                        if matches!(ev, HardwareEvent::Disconnected) {
                            tracing::warn!("GoXLR disconnected — hardware thread dropped");
                            hw = None;
                        } else if !matches!(ev, HardwareEvent::FaderMoved { .. } | HardwareEvent::MicLevel(_)) {
                            tracing::debug!(?ev, "← hardware");
                        }
                        (Input::Hardware(ev), None)
                    }
                };
                let mut replies = Vec::new();
                for effect in hub.handle(input) {
                    match effect {
                        Effect::Broadcast(m) => { let _ = events.send(m); }
                        Effect::Reply(m) => replies.push(m),
                        Effect::Hardware(cmd) => {
                            tracing::debug!(?cmd, "→ hardware");
                            if let Some(h) = &hw { h.send(cmd); }
                        }
                    }
                }
                if let Some(reply) = reply { let _ = reply.send(replies); }
            }
            _ = meter_tick.tick() => {
                if events.receiver_count() > 0 {
                    let _ = events.send(hub.meters_message());
                }
            }
            _ = hw_reconnect_tick.tick() => {
                #[cfg(windows)]
                if hw.is_none() && !is_sim {
                    let mut info = mixforge_usb::discover();
                    if !info.is_empty() {
                        let tx = hub_tx.clone();
                        let forward = move |ev| {
                            let _ = tx.send(HubMsg::Hardware(ev));
                        };
                        if let Some(new_hw) = open_goxlr(&mut info, forward) {
                            tracing::info!("GoXLR reconnected successfully!");
                            hub.set_motorized_faders(
                                info.iter().any(|h| h.capabilities.motorized_faders),
                            );
                            hub.set_hardware(info);
                            for e in hub.full_hardware_sync() {
                                if let Effect::Hardware(cmd) = e {
                                    new_hw.send(cmd);
                                }
                            }
                            hw = Some(new_hw);
                            let _ = events.send(hub.state_message());
                        }
                    }
                }
            }
        }
    }
}

// ---- Dev-only simulated hardware (MIXFORGE_SIM_HW=1) --------------------------------------

#[derive(serde::Deserialize)]
struct SimFaderQuery {
    fader: char,
    pos: f32,
}

#[derive(serde::Deserialize)]
struct SimMuteQuery {
    fader: char,
}

fn parse_fader(c: char) -> Option<Fader> {
    match c.to_ascii_uppercase() {
        'A' => Some(Fader::A),
        'B' => Some(Fader::B),
        'C' => Some(Fader::C),
        'D' => Some(Fader::D),
        _ => None,
    }
}

async fn sim_fader(
    State(app): State<AppState>,
    Query(q): Query<SimFaderQuery>,
) -> impl IntoResponse {
    let (Some(sim), Some(fader)) = (&app.sim, parse_fader(q.fader)) else {
        return axum::Json(json!({ "ok": false, "error": "sim disabled or bad fader" }));
    };
    sim.inject(HardwareEvent::FaderMoved {
        fader,
        position: q.pos.clamp(0.0, 1.0),
    });
    axum::Json(json!({ "ok": true }))
}

async fn sim_mute(State(app): State<AppState>, Query(q): Query<SimMuteQuery>) -> impl IntoResponse {
    let (Some(sim), Some(fader)) = (&app.sim, parse_fader(q.fader)) else {
        return axum::Json(json!({ "ok": false, "error": "sim disabled or bad fader" }));
    };
    sim.inject(HardwareEvent::MuteButton {
        fader,
        pressed: true,
    });
    sim.inject(HardwareEvent::MuteButton {
        fader,
        pressed: false,
    });
    axum::Json(json!({ "ok": true }))
}

/// What the hub has sent to the simulated device since the last call (LEDs, fader displays…).
async fn sim_received(State(app): State<AppState>) -> impl IntoResponse {
    let Some(sim) = &app.sim else {
        return axum::Json(json!({ "ok": false, "error": "sim disabled" }));
    };
    let cmds: Vec<String> = sim
        .take_received()
        .iter()
        .map(|c| format!("{c:?}"))
        .collect();
    axum::Json(json!({ "ok": true, "commands": cmds }))
}

// -------------------------------------------------------------------------------------------------
// Stream Deck & HTTP REST API Handlers
// -------------------------------------------------------------------------------------------------

async fn get_current_mixer_state(app: &AppState) -> Option<mixforge_protocol::MixerState> {
    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::GetState,
        reply: tx,
    });
    let replies = rx.await.ok()?;
    for r in replies {
        if let ServerMessage::State(s) = r {
            return Some(s);
        }
    }
    None
}

fn resolve_channel_id(
    input: &str,
    state: Option<&mixforge_protocol::MixerState>,
) -> Option<mixforge_protocol::ChannelId> {
    let clean = input.trim();
    if let Ok(id) = clean.parse::<u32>() {
        return Some(mixforge_protocol::ChannelId(id));
    }
    if let Some(st) = state {
        // Match fader letter A, B, C, D
        if clean.len() == 1 {
            let ch = clean.chars().next().unwrap().to_ascii_uppercase().to_string();
            if let Some(fa) = st.faders.iter().find(|f| f.fader.eq_ignore_ascii_case(&ch)) {
                return Some(fa.channel);
            }
        }
        // Match by channel name
        let clean_norm = clean.to_lowercase().replace(['_', ' ', '-'], "");
        for c in &st.channels {
            let name_norm = c.name.to_lowercase().replace(['_', ' ', '-'], "");
            if name_norm == clean_norm || name_norm.contains(&clean_norm) || clean_norm.contains(&name_norm) {
                return Some(c.id);
            }
        }
    }
    None
}

fn resolve_bus_id(
    input: &str,
    state: Option<&mixforge_protocol::MixerState>,
) -> Option<mixforge_protocol::BusId> {
    let clean = input.trim();
    if let Ok(id) = clean.parse::<u32>() {
        return Some(mixforge_protocol::BusId(id));
    }
    if let Some(st) = state {
        let clean_norm = clean.to_lowercase().replace(['_', ' ', '-'], "");
        for b in &st.buses {
            let name_norm = b.name.to_lowercase().replace(['_', ' ', '-'], "");
            if name_norm == clean_norm || name_norm.contains(&clean_norm) || clean_norm.contains(&name_norm) {
                return Some(b.id);
            }
        }
    }
    None
}

async fn api_status(State(app): State<AppState>) -> impl IntoResponse {
    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::GetState,
        reply: tx,
    });
    match rx.await {
        Ok(replies) => {
            for r in replies {
                if let ServerMessage::State(state) = r {
                    return axum::Json(json!({ "ok": true, "state": state }));
                }
            }
            axum::Json(json!({ "ok": true }))
        }
        Err(e) => axum::Json(json!({ "ok": false, "error": e.to_string() })),
    }
}

#[derive(serde::Deserialize)]
struct ApiFaderQuery {
    channel: String,
    volume: Option<f32>,
    delta: Option<f32>,
}

async fn api_fader(
    State(app): State<AppState>,
    Query(q): Query<ApiFaderQuery>,
) -> impl IntoResponse {
    let state = get_current_mixer_state(&app).await;
    let Some(ch_id) = resolve_channel_id(&q.channel, state.as_ref()) else {
        return axum::Json(json!({ "ok": false, "error": format!("unknown channel '{}'", q.channel) }));
    };

    let target_vol = if let Some(delta) = q.delta {
        let cur = state
            .as_ref()
            .and_then(|s| s.channels.iter().find(|c| c.id == ch_id))
            .map(|c| c.volume)
            .unwrap_or(0.8);
        (cur + delta).clamp(0.0, 1.0)
    } else if let Some(mut vol) = q.volume {
        if vol > 1.0 {
            vol /= 100.0;
        }
        vol.clamp(0.0, 1.0)
    } else {
        return axum::Json(json!({ "ok": false, "error": "either 'volume' or 'delta' required" }));
    };

    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::SetVolume {
            channel: ch_id,
            volume: target_vol,
        },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "channel": ch_id.0, "volume": target_vol }))
}

#[derive(serde::Deserialize)]
struct ApiMuteQuery {
    channel: String,
    muted: Option<bool>,
    action: Option<String>,
}

async fn api_mute(
    State(app): State<AppState>,
    Query(q): Query<ApiMuteQuery>,
) -> impl IntoResponse {
    let state = get_current_mixer_state(&app).await;
    let Some(ch_id) = resolve_channel_id(&q.channel, state.as_ref()) else {
        return axum::Json(json!({ "ok": false, "error": format!("unknown channel '{}'", q.channel) }));
    };

    let target_muted = if q.action.as_deref() == Some("toggle") {
        let cur = state
            .as_ref()
            .and_then(|s| s.channels.iter().find(|c| c.id == ch_id))
            .map(|c| c.muted)
            .unwrap_or(false);
        !cur
    } else if let Some(m) = q.muted {
        m
    } else if q.action.as_deref() == Some("mute") {
        true
    } else if q.action.as_deref() == Some("unmute") {
        false
    } else {
        let cur = state
            .as_ref()
            .and_then(|s| s.channels.iter().find(|c| c.id == ch_id))
            .map(|c| c.muted)
            .unwrap_or(false);
        !cur
    };

    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::SetMute {
            channel: ch_id,
            muted: target_muted,
        },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "channel": ch_id.0, "muted": target_muted }))
}

#[derive(serde::Deserialize)]
struct ApiRouteQuery {
    channel: String,
    bus: String,
    gain: Option<f32>,
    action: Option<String>,
}

async fn api_route(
    State(app): State<AppState>,
    Query(q): Query<ApiRouteQuery>,
) -> impl IntoResponse {
    let state = get_current_mixer_state(&app).await;
    let Some(ch_id) = resolve_channel_id(&q.channel, state.as_ref()) else {
        return axum::Json(json!({ "ok": false, "error": format!("unknown channel '{}'", q.channel) }));
    };
    let Some(b_id) = resolve_bus_id(&q.bus, state.as_ref()) else {
        return axum::Json(json!({ "ok": false, "error": format!("unknown bus '{}'", q.bus) }));
    };

    let target_gain = if q.action.as_deref() == Some("toggle") {
        let routed = state
            .as_ref()
            .and_then(|s| s.routes.iter().find(|r| r.channel == ch_id && r.bus == b_id))
            .map(|r| r.gain > 0.0)
            .unwrap_or(false);
        if routed { 0.0 } else { 1.0 }
    } else if let Some(g) = q.gain {
        g
    } else {
        1.0
    };

    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::SetRoute {
            channel: ch_id,
            bus: b_id,
            gain: target_gain,
        },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "channel": ch_id.0, "bus": b_id.0, "gain": target_gain }))
}

#[derive(serde::Deserialize)]
struct ApiBleepQuery {
    active: Option<bool>,
    pressed: Option<bool>,
}

async fn api_bleep(
    State(app): State<AppState>,
    Query(q): Query<ApiBleepQuery>,
) -> impl IntoResponse {
    let is_pressed = q.pressed.or(q.active).unwrap_or(true);
    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::Bleep { pressed: is_pressed },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "bleep": is_pressed }))
}

#[derive(serde::Deserialize)]
struct ApiMicMuteQuery {
    muted: Option<bool>,
    action: Option<String>,
}

async fn api_mic_mute(
    State(app): State<AppState>,
    Query(q): Query<ApiMicMuteQuery>,
) -> impl IntoResponse {
    let target_muted = if q.action.as_deref() == Some("toggle") {
        let state = get_current_mixer_state(&app).await;
        let cur = state
            .as_ref()
            .and_then(|s| s.channels.iter().find(|c| c.name.eq_ignore_ascii_case("Mic")))
            .map(|c| c.muted)
            .unwrap_or(false);
        !cur
    } else {
        q.muted.unwrap_or(true)
    };

    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::SetMicMute { muted: target_muted },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "mic_muted": target_muted }))
}

#[derive(serde::Deserialize)]
struct ApiProfileQuery {
    name: String,
}

async fn api_profile(
    State(app): State<AppState>,
    Query(q): Query<ApiProfileQuery>,
) -> impl IntoResponse {
    let Ok(base) = std::env::var("LOCALAPPDATA") else {
        return axum::Json(json!({ "ok": false, "error": "LOCALAPPDATA not set" }));
    };
    let dir = std::path::Path::new(&base).join("NextXLR");
    let candidate_paths = [
        dir.join("setups").join(format!("{}.json", q.name)),
        dir.join("profiles").join(format!("{}.json", q.name)),
        dir.join(format!("{}.json", q.name)),
    ];

    let found = candidate_paths.iter().find(|p| p.exists());
    let Some(path) = found else {
        return axum::Json(json!({ "ok": false, "error": format!("profile '{}' not found", q.name) }));
    };

    let Ok(text) = std::fs::read_to_string(path) else {
        return axum::Json(json!({ "ok": false, "error": "could not read profile file" }));
    };

    let Ok(v) = serde_json::from_str::<serde_json::Value>(&text) else {
        return axum::Json(json!({ "ok": false, "error": "invalid json in profile" }));
    };

    let profile_val = v.get("profile").cloned().unwrap_or(v);
    let Ok(profile) = serde_json::from_value::<mixforge_protocol::Profile>(profile_val) else {
        return axum::Json(json!({ "ok": false, "error": "could not parse Profile format" }));
    };

    let (tx, rx) = oneshot::channel();
    let _ = app.hub.send(HubMsg::Client {
        msg: ClientMessage::LoadProfile { profile },
        reply: tx,
    });
    let _ = rx.await;

    axum::Json(json!({ "ok": true, "profile": q.name }))
}

async fn cors_middleware(
    req: axum::extract::Request,
    next: axum::middleware::Next,
) -> axum::response::Response {
    let mut response = next.run(req).await;
    let headers = response.headers_mut();
    headers.insert(
        axum::http::header::ACCESS_CONTROL_ALLOW_ORIGIN,
        axum::http::HeaderValue::from_static("*"),
    );
    headers.insert(
        axum::http::header::ACCESS_CONTROL_ALLOW_METHODS,
        axum::http::HeaderValue::from_static("GET, POST, OPTIONS"),
    );
    headers.insert(
        axum::http::header::ACCESS_CONTROL_ALLOW_HEADERS,
        axum::http::HeaderValue::from_static("*"),
    );
    response
}
