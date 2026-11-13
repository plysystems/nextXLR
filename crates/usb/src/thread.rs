//! Dedicated hardware I/O thread. USB calls block, so they never run on the async runtime or
//! the audio thread. Events flow out through a callback; commands flow in through a channel.

use std::sync::mpsc::{self, Receiver, Sender};
use std::thread::JoinHandle;
use std::time::Duration;

use crate::{HardwareCommand, HardwareDevice, HardwareEvent};

enum Msg {
    Cmd(HardwareCommand),
    /// Answered once every command queued before it has been applied.
    Flush(Sender<()>),
}

pub struct HardwareThread {
    commands: Sender<Msg>,
    handle: Option<JoinHandle<()>>,
}

impl HardwareThread {
    pub fn send(&self, cmd: HardwareCommand) {
        let _ = self.commands.send(Msg::Cmd(cmd));
    }

    /// Blocks until all previously sent commands reached the device (or `timeout` elapsed).
    /// Used before shutdown / suspend so the standby command is really on the hardware.
    pub fn flush(&self, timeout: Duration) -> bool {
        let (tx, rx) = mpsc::channel();
        if self.commands.send(Msg::Flush(tx)).is_err() {
            return false;
        }
        rx.recv_timeout(timeout).is_ok()
    }

    /// Puts the device into standby and waits until the command has been written.
    pub fn sleep_blocking(&self, timeout: Duration) -> bool {
        self.send(HardwareCommand::Sleep);
        self.flush(timeout)
    }
}

impl Drop for HardwareThread {
    fn drop(&mut self) {
        // Dropping the sender ends the loop on its next wake-up.
        let (dead, _) = mpsc::channel();
        self.commands = dead;
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}

/// `on_event` is called from the hardware thread; it should just forward into the state hub.
pub fn spawn_hardware_thread<D, F>(mut device: D, on_event: F) -> std::io::Result<HardwareThread>
where
    D: HardwareDevice + 'static,
    F: Fn(HardwareEvent) + Send + 'static,
{
    let (tx, rx): (Sender<Msg>, Receiver<Msg>) = mpsc::channel();
    let handle = std::thread::Builder::new().name("mixforge-hw".into()).spawn(move || {
        let mut consecutive_errors = 0usize;
        loop {
            // 1. Outgoing: drain everything the hub wants on the device.
            loop {
                match rx.try_recv() {
                    Ok(Msg::Cmd(cmd)) => {
                        if let Err(e) = device.apply(&cmd) {
                            tracing::warn!("hardware command {cmd:?} failed: {e:#}");
                        }
                    }
                    Ok(Msg::Flush(ack)) => {
                        let _ = ack.send(());
                    }
                    Err(mpsc::TryRecvError::Empty) => break,
                    Err(mpsc::TryRecvError::Disconnected) => return,
                }
            }
            // 2. Incoming: wait (short timeout so commands stay responsive) for input changes.
            match device.wait_events(Duration::from_millis(10)) {
                Ok(events) => {
                    consecutive_errors = 0;
                    events.into_iter().for_each(&on_event);
                }
                Err(e) => {
                    consecutive_errors += 1;
                    if consecutive_errors == 1 {
                        tracing::warn!("hardware read warning: {e:#}");
                    }
                    if consecutive_errors >= 50 {
                        tracing::error!("hardware read failed permanently (after 50 retries): {e:#}");
                        on_event(HardwareEvent::Disconnected);
                        return;
                    }
                    std::thread::sleep(Duration::from_millis(20));
                }
            }
        }
    })?;
    Ok(HardwareThread { commands: tx, handle: Some(handle) })
}
