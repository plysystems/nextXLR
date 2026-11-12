//! Bootstrap audio I/O: default input → mic channel (slot 0) → bus 0 (Headphones) → default output.
//!
//! Phase 1 replaces this with a single-clock graph (one master device + resamplers for the rest).
//! Known limitations here: input and output are independent cpal streams, so sample-rate
//! mismatch and clock drift are not handled yet.

#![allow(dead_code)]
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread::JoinHandle;

use anyhow::{anyhow, Context, Result};
use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};

use crate::params::{MeterBank, ParamBank};

pub const MIC_SLOT: usize = 0;
pub const HEADPHONES_BUS: usize = 0;

pub struct AudioThread {
    shutdown: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
}

impl AudioThread {
    /// cpal streams are `!Send` on some backends, so they live on a dedicated thread.
    pub fn spawn(params: Arc<ParamBank>, meters: Arc<MeterBank>) -> Result<Self> {
        let shutdown = Arc::new(AtomicBool::new(false));
        let (ready_tx, ready_rx) = std::sync::mpsc::channel::<Result<()>>();
        let flag = shutdown.clone();

        let handle = std::thread::Builder::new()
            .name("mixforge-audio".into())
            .spawn(move || {
                let streams = match open_streams(params, meters) {
                    Ok(s) => {
                        let _ = ready_tx.send(Ok(()));
                        s
                    }
                    Err(e) => {
                        let _ = ready_tx.send(Err(e));
                        return;
                    }
                };
                while !flag.load(Ordering::Relaxed) {
                    std::thread::park_timeout(std::time::Duration::from_millis(250));
                }
                drop(streams);
            })?;

        ready_rx.recv().context("audio thread died during startup")??;
        Ok(Self { shutdown, handle: Some(handle) })
    }
}

impl Drop for AudioThread {
    fn drop(&mut self) {
        self.shutdown.store(true, Ordering::Relaxed);
        if let Some(h) = self.handle.take() {
            h.thread().unpark();
            let _ = h.join();
        }
    }
}

fn open_streams(params: Arc<ParamBank>, meters: Arc<MeterBank>) -> Result<(cpal::Stream, cpal::Stream)> {
    let host = cpal::default_host();
    let input = host.default_input_device().ok_or_else(|| anyhow!("no default input device"))?;
    let output = host.default_output_device().ok_or_else(|| anyhow!("no default output device"))?;

    let in_name = input.name().unwrap_or_default();
    let out_name = output.name().unwrap_or_default();
    let in_lower = in_name.to_lowercase();
    let out_lower = out_name.to_lowercase();

    // If the default input or output is a GoXLR endpoint (e.g. "Broadcast Stream Mix" or "System"),
    // software loopback must NOT run: the physical GoXLR hardware DSP routes all audio in hardware.
    // Piping Broadcast Stream Mix (which contains all mixed channels) into System creates a 100ms
    // delayed feedback loop (hall/reverb echo with Chat, Music, etc.)!
    if in_lower.contains("goxlr")
        || in_lower.contains("tc-helicon")
        || in_lower.contains("broadcast")
        || out_lower.contains("goxlr")
        || out_lower.contains("tc-helicon")
    {
        tracing::info!(
            input = %in_name,
            output = %out_name,
            "GoXLR audio devices detected — software loopback disabled to prevent hall/echo"
        );
        return Err(anyhow!("software loopback disabled on GoXLR audio devices"));
    }

    let in_cfg: cpal::StreamConfig = input.default_input_config()?.config();
    let out_cfg: cpal::StreamConfig = output.default_output_config()?.config();
    tracing::info!(
        input = %in_name,
        output = %out_name,
        in_rate = in_cfg.sample_rate.0,
        out_rate = out_cfg.sample_rate.0,
        "opening audio devices"
    );
    if in_cfg.sample_rate != out_cfg.sample_rate {
        tracing::warn!("input/output sample rates differ — resampling is not implemented yet");
    }

    // Pro audio ultra-low latency ring buffer: ~10-12 ms of audio at device sample rate
    // (e.g. 480–576 samples at 48 kHz instead of 4800 samples / 100 ms).
    let latency_samples = ((in_cfg.sample_rate.0 as usize) / 80).clamp(256, 1024);
    let (mut producer, mut consumer) = rtrb::RingBuffer::<f32>::new(latency_samples * 2);

    let in_channels = in_cfg.channels as usize;
    let in_params = params.clone();
    let in_stream = input.build_input_stream(
        &in_cfg,
        move |data: &[f32], _: &cpal::InputCallbackInfo| {
            let gain = in_params.channel_gain(MIC_SLOT);
            let mut peak = 0.0f32;
            for frame in data.chunks_exact(in_channels) {
                let s = frame.iter().sum::<f32>() / in_channels as f32 * gain;
                peak = peak.max(s.abs());
                let _ = producer.push(s); // ring full → drop sample (output stalled)
            }
            // Skipped once a better source (the GoXLR's own mic level) feeds this slot: with a
            // GoXLR the default input is "Broadcast Stream Mix", not the mic.
            meters.push_internal_peak(MIC_SLOT, peak);
        },
        |e| tracing::error!("input stream error: {e}"),
        None,
    )?;

    let out_channels = out_cfg.channels as usize;
    let out_stream = output.build_output_stream(
        &out_cfg,
        move |out: &mut [f32], _: &cpal::OutputCallbackInfo| {
            let route = params.route_gain(MIC_SLOT, HEADPHONES_BUS);
            // Clock drift & delay protection: keep buffer at target latency
            let available = consumer.slots();
            if available > latency_samples {
                let excess = available - latency_samples;
                for _ in 0..excess {
                    let _ = consumer.pop();
                }
            }
            for frame in out.chunks_exact_mut(out_channels) {
                let raw = consumer.pop().unwrap_or(0.0) * route;
                // Soft saturator / peak limiter prevents harsh digital clipping (+ sound quality)
                let s = if raw.abs() > 0.95 {
                    raw.signum() * (0.95 + 0.05 * (1.0 - (-((raw.abs() - 0.95) / 0.05).min(20.0)).exp()))
                } else {
                    raw
                };
                frame.fill(s);
            }
        },
        |e| tracing::error!("output stream error: {e}"),
        None,
    )?;

    in_stream.play()?;
    out_stream.play()?;
    Ok((in_stream, out_stream))
}
