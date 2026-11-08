//! Per-channel level meters from Windows Core Audio endpoint meters.
//!
//! The GoXLR driver exposes one playback endpoint per PC channel ("System (TC-HELICON GoXLR)",
//! "Game …", "Chat …", "Music …", "Sample …"). `IAudioMeterInformation::GetPeakValue` on each
//! endpoint returns the peak of everything Windows is sending to it — no capture stream, no
//! loopback, practically free. Values are pre-fader (what the PC sends, before the GoXLR mixer).
//!
//! Endpoints are re-enumerated every few seconds so hot-plug and default-device changes work.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use windows::Win32::Devices::FunctionDiscovery::PKEY_Device_FriendlyName;
use windows::Win32::Media::Audio::Endpoints::IAudioMeterInformation;
use windows::Win32::Media::Audio::{eCapture, eConsole, eRender, IMMDeviceEnumerator, MMDeviceEnumerator, DEVICE_STATE_ACTIVE};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_ALL, COINIT_MULTITHREADED, STGM_READ,
};

use crate::params::MeterBank;

const POLL: Duration = Duration::from_millis(16);
const RESCAN: Duration = Duration::from_secs(5);

/// Channel slot ← endpoint whose friendly name starts with `prefix` and mentions the GoXLR.
#[derive(Clone, Copy)]
pub struct EndpointBinding {
    pub slot: usize,
    pub prefix: &'static str,
    /// Recording endpoint (the GoXLR's mixes) instead of a playback endpoint.
    pub capture: bool,
}

pub struct EndpointMeters {
    stop: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
}

impl EndpointMeters {
    pub fn spawn(meters: Arc<MeterBank>, bindings: Vec<EndpointBinding>) -> std::io::Result<Self> {
        let stop = Arc::new(AtomicBool::new(false));
        let flag = stop.clone();
        let handle = std::thread::Builder::new()
            .name("mixforge-meters".into())
            .spawn(move || run(meters, bindings, flag))?;
        Ok(Self { stop, handle: Some(handle) })
    }
}

impl Drop for EndpointMeters {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}

fn run(meters: Arc<MeterBank>, bindings: Vec<EndpointBinding>, stop: Arc<AtomicBool>) {
    // SAFETY: COM is initialised once for this thread and uninitialised on exit.
    if unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) }.is_err() {
        tracing::warn!("endpoint meters: COM init failed");
        return;
    }
    let mut active: Vec<(usize, IAudioMeterInformation)> = Vec::new();
    let mut last_scan: Option<Instant> = None;

    while !stop.load(Ordering::Relaxed) {
        if last_scan.is_none_or(|t| t.elapsed() >= RESCAN) {
            match scan(&bindings) {
                Ok(found) => {
                    if found.len() != active.len() {
                        tracing::info!("endpoint meters: {} channel endpoint(s) bound", found.len());
                    }
                    active = found;
                }
                Err(e) => tracing::debug!("endpoint scan failed: {e}"),
            }
            last_scan = Some(Instant::now());
        }
        for (slot, meter) in &active {
            // SAFETY: plain COM call on an interface owned by this thread.
            if let Ok(peak) = unsafe { meter.GetPeakValue() } {
                meters.push_external_peak(*slot, peak);
            }
        }
        std::thread::sleep(POLL);
    }
    drop(active);
    unsafe { CoUninitialize() };
}

fn scan(bindings: &[EndpointBinding]) -> windows::core::Result<Vec<(usize, IAudioMeterInformation)>> {
    let mut out = Vec::new();
    // SAFETY: standard MMDevice enumeration; all interfaces stay on this thread.
    unsafe {
        let enumerator: IMMDeviceEnumerator = CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
        for (flow, capture) in [(eRender, false), (eCapture, true)] {
            let devices = enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE)?;
            for i in 0..devices.GetCount()? {
                let device = devices.Item(i)?;
                let name = device
                    .OpenPropertyStore(STGM_READ)
                    .and_then(|s| s.GetValue(&PKEY_Device_FriendlyName))
                    .map(|v| v.to_string())
                    .unwrap_or_default();
                let name_lower = name.to_lowercase();
                let is_goxlr = name_lower.contains("goxlr") || name_lower.contains("tc-helicon");
                if !is_goxlr {
                    continue;
                }
                let matching_bindings: Vec<&EndpointBinding> = bindings
                    .iter()
                    .filter(|b| b.capture == capture && name_lower.contains(&b.prefix.to_lowercase()))
                    .collect();

                for b in matching_bindings {
                    if out.iter().any(|(slot, _)| *slot == b.slot) {
                        continue;
                    }
                    match device.Activate::<IAudioMeterInformation>(CLSCTX_ALL, None) {
                        Ok(meter) => {
                            tracing::debug!(endpoint = %name, slot = b.slot, "metering endpoint");
                            out.push((b.slot, meter));
                        }
                        Err(e) => tracing::debug!(endpoint = %name, "no meter interface: {e}"),
                    }
                }
            }
        }

        // Fallback for testing without GoXLR hardware:
        // Bind default render device to slot 4 (System) and default capture device to slot 0 (Mic).
        if out.is_empty() {
            if let Ok(dev) = enumerator.GetDefaultAudioEndpoint(eRender, eConsole) {
                if let Ok(meter) = dev.Activate::<IAudioMeterInformation>(CLSCTX_ALL, None) {
                    out.push((4, meter)); // slot 4 = System
                }
            }
            if let Ok(dev) = enumerator.GetDefaultAudioEndpoint(eCapture, eConsole) {
                if let Ok(meter) = dev.Activate::<IAudioMeterInformation>(CLSCTX_ALL, None) {
                    out.push((0, meter)); // slot 0 = Mic
                }
            }
        }
    }
    Ok(out)
}
