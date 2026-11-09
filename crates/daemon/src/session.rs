//! Client sessions. One transport-independent core (`run_session`) speaks the JSON protocol
//! over a pair of string channels; thin adapters connect it to a WebSocket or a named pipe.

use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::State;
use axum::response::IntoResponse;
use futures_util::{SinkExt, StreamExt};
use mixforge_protocol::{ClientMessage, ServerMessage, API_VERSION};
use tokio::sync::{broadcast, mpsc, oneshot};

use crate::{AppState, HubMsg};

async fn ask_hub(app: &AppState, msg: ClientMessage) -> Vec<ServerMessage> {
    let (tx, rx) = oneshot::channel();
    if app.hub.send(HubMsg::Client { msg, reply: tx }).is_err() {
        return vec![ServerMessage::Error {
            message: "engine stopped".into(),
        }];
    }
    rx.await.unwrap_or_default()
}

async fn emit(out: &mpsc::Sender<String>, msg: &ServerMessage) -> bool {
    match serde_json::to_string(msg) {
        Ok(json) => out.send(json).await.is_ok(),
        Err(_) => true,
    }
}

/// Protocol loop for one client. Ends when `incoming` closes or `out` is dropped.
pub async fn run_session(
    app: AppState,
    mut incoming: mpsc::Receiver<String>,
    out: mpsc::Sender<String>,
) {
    // Subscribe BEFORE fetching the snapshot so no change can fall between the two.
    let mut events = app.events.subscribe();

    let hello = ServerMessage::Hello {
        server: "mixforged".into(),
        version: env!("CARGO_PKG_VERSION").into(),
        api: API_VERSION,
    };
    if !emit(&out, &hello).await {
        return;
    }
    for m in ask_hub(&app, ClientMessage::GetState).await {
        if !emit(&out, &m).await {
            return;
        }
    }

    loop {
        tokio::select! {
            text = incoming.recv() => {
                let Some(text) = text else { break };
                let replies = match serde_json::from_str::<ClientMessage>(&text) {
                    Ok(msg) => ask_hub(&app, msg).await,
                    Err(e) => vec![ServerMessage::Error { message: format!("bad message: {e}") }],
                };
                for r in &replies {
                    if !emit(&out, r).await { return; }
                }
            }
            event = events.recv() => match event {
                Ok(m) => if !emit(&out, &m).await { break },
                Err(broadcast::error::RecvError::Lagged(n)) => {
                    // Missed events → resync with a full snapshot instead of drifting.
                    tracing::debug!("client lagged {n} events, resyncing");
                    for r in ask_hub(&app, ClientMessage::GetState).await {
                        if !emit(&out, &r).await { return; }
                    }
                }
                Err(broadcast::error::RecvError::Closed) => break,
            },
        }
    }
}

// ---- WebSocket adapter (remote panels, Stream Deck) ---------------------------------------

pub async fn ws_handler(ws: WebSocketUpgrade, State(app): State<AppState>) -> impl IntoResponse {
    ws.on_upgrade(move |socket| ws_session(socket, app))
}

async fn ws_session(socket: WebSocket, app: AppState) {
    let (mut tx, mut rx) = socket.split();
    let (in_tx, in_rx) = mpsc::channel::<String>(64);
    let (out_tx, mut out_rx) = mpsc::channel::<String>(256);

    let writer = async move {
        while let Some(s) = out_rx.recv().await {
            if tx.send(Message::Text(s)).await.is_err() {
                break;
            }
        }
    };
    let reader = async move {
        while let Some(Ok(msg)) = rx.next().await {
            match msg {
                Message::Text(t) => {
                    if in_tx.send(t).await.is_err() {
                        break;
                    }
                }
                Message::Close(_) => break,
                _ => {}
            }
        }
    };
    tokio::select! {
        _ = run_session(app, in_rx, out_tx) => {}
        _ = writer => {}
        _ = reader => {}
    }
}

// ---- Named pipe adapter (local desktop app) -----------------------------------------------

#[cfg(windows)]
pub const PIPE_NAME: &str = r"\\.\pipe\mixforge";

/// Serves `\\.\pipe\mixforge`: one JSON message per line, both directions.
/// `first_pipe_instance` makes startup fail if another process already owns the name,
/// so nobody can squat the pipe and impersonate the daemon. Remote (SMB) clients are rejected.
#[cfg(windows)]
pub async fn serve_pipe(app: AppState) -> std::io::Result<()> {
    use tokio::net::windows::named_pipe::ServerOptions;

    let mut server = ServerOptions::new()
        .first_pipe_instance(true)
        .create(PIPE_NAME)?;
    tracing::info!("named pipe on {PIPE_NAME}");
    loop {
        if let Err(e) = server.connect().await {
            tracing::warn!("named pipe connect error: {e}, retrying");
            tokio::time::sleep(std::time::Duration::from_millis(50)).await;
            continue;
        }
        // Create the next instance before serving, so a new client can always connect.
        match ServerOptions::new().create(PIPE_NAME) {
            Ok(next) => {
                let client = std::mem::replace(&mut server, next);
                tokio::spawn(pipe_session(client, app.clone()));
            }
            Err(e) => {
                tracing::error!("failed to create next named pipe instance: {e}");
                tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            }
        }
    }
}

#[cfg(windows)]
async fn pipe_session(pipe: tokio::net::windows::named_pipe::NamedPipeServer, app: AppState) {
    use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};

    let (read, mut write) = tokio::io::split(pipe);
    let (in_tx, in_rx) = mpsc::channel::<String>(64);
    let (out_tx, mut out_rx) = mpsc::channel::<String>(256);

    let writer = async move {
        while let Some(mut s) = out_rx.recv().await {
            s.push('\n');
            if write.write_all(s.as_bytes()).await.is_err() {
                break;
            }
        }
    };
    let reader = async move {
        let mut lines = BufReader::new(read).lines();
        while let Ok(Some(line)) = lines.next_line().await {
            if !line.trim().is_empty() && in_tx.send(line).await.is_err() {
                break;
            }
        }
    };
    tracing::debug!("pipe client connected");
    tokio::select! {
        _ = run_session(app, in_rx, out_tx) => {}
        _ = writer => {}
        _ = reader => {}
    }
    tracing::debug!("pipe client disconnected");
}
