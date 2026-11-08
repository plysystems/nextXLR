//! Simulated control surface: lets tests (and UI development without a GoXLR) inject
//! hardware events and inspect what the hub sent to the device.

use std::sync::{Arc, Mutex};
use std::time::Duration;

use mixforge_protocol::HardwareInfo;

use crate::{HardwareCommand, HardwareDevice, HardwareEvent, Model};

#[derive(Default)]
struct Shared {
    pending: Vec<HardwareEvent>,
    received: Vec<HardwareCommand>,
}

pub struct SimDevice {
    model: Model,
    shared: Arc<Mutex<Shared>>,
}

/// Test-side handle to a `SimDevice` living on the hardware thread.
#[derive(Clone)]
pub struct SimHandle {
    shared: Arc<Mutex<Shared>>,
}

impl SimDevice {
    /// Simulates a full GoXLR.
    pub fn new() -> (Self, SimHandle) {
        Self::with_model(Model::Full)
    }

    pub fn with_model(model: Model) -> (Self, SimHandle) {
        let shared = Arc::new(Mutex::new(Shared::default()));
        (Self { model, shared: shared.clone() }, SimHandle { shared })
    }
}

impl SimHandle {
    pub fn inject(&self, event: HardwareEvent) {
        self.shared.lock().unwrap().pending.push(event);
    }

    pub fn take_received(&self) -> Vec<HardwareCommand> {
        std::mem::take(&mut self.shared.lock().unwrap().received)
    }
}

impl HardwareDevice for SimDevice {
    fn info(&self) -> HardwareInfo {
        let mut info = self.model.info(Some("SIM".into()));
        info.model = format!("{} (simulated)", info.model);
        info.capabilities.motorized_faders = false;
        info
    }

    fn wait_events(&mut self, timeout: Duration) -> anyhow::Result<Vec<HardwareEvent>> {
        let events = std::mem::take(&mut self.shared.lock().unwrap().pending);
        if events.is_empty() {
            std::thread::sleep(timeout);
        }
        Ok(events)
    }

    fn apply(&mut self, cmd: &HardwareCommand) -> anyhow::Result<()> {
        self.shared.lock().unwrap().received.push(cmd.clone());
        Ok(())
    }
}
