# Mixforge — Architecture (native Windows)

> Working name **Mixforge**. "GoXLR" is a TC-Helicon / Music Tribe trademark, so keep it out of
> the product name and branding.

## 1. Tech stack

| Layer | Choice | Why |
|---|---|---|
| **Desktop UI** | **C# / .NET 8 + WinUI 3** (Windows App SDK), MVVM via `CommunityToolkit.Mvvm` | Native Fluent look (Mica, dark mode, DPI scaling). It is Microsoft's current UI stack, with first-class tray, notifications and per-monitor DPI. Win2D handles the 60 fps meters. |
| **Audio engine** | **Rust** (existing `mixforge-engine`). WASAPI is accessed via `cpal` today and will move to the `windows` crate for exclusive mode / process loopback | The real-time code must not pause for garbage collection, so it stays out of .NET. Rust gives memory safety on the audio thread plus easy C/C++ FFI for VST3, RNNoise and DSP |
| **Engine host** | **`mixforged`**, a Windows background process (tray/service) | Audio and hardware keep running when the window closes. Stream Deck, OBS and the phone panel connect to it, not to the UI |
| **UI ↔ engine IPC** | **Named pipe** `\\.\pipe\mixforge` (local UI) + WebSocket (remote panels). Same JSON protocol on both | A pipe opens no TCP port, carries a Windows ACL (current user only) and has lower overhead. WebSocket stays for Stream Deck and phones |
| **GoXLR control** | Rust, **TUSBAUDIO driver API** (`goxlr_audioapi_x64.dll`) | The GoXLR is **not a HID device**. Windows talks to it through TC-Helicon's Thesycon driver using vendor class requests. Swapping in WinUSB would break its audio, so we use the driver's DLL, as goxlr-utility does |
| Per-app audio | WASAPI process loopback (`AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`) + `IAudioPolicyConfig` | Native per-app capture/redirect without virtual cables |
| Virtual devices | Ship later as a signed virtual audio driver, or reuse the GoXLR's own WDM endpoints (Chat/Music/Game/System) | A kernel driver needs EV signing and is a project of its own |
| Noise suppression / FX | `nnnoiseless` (RNNoise), DeepFilterNet; Signalsmith Stretch | Same as before |
| VST3 | Separate sandbox host process; plugin editors embedded via HWND | A crashing plugin can't kill audio |
| OBS / MIDI / Stream Deck | `obws`, `midir`, Elgato plugin over WebSocket | Unchanged |

### Why not all-C#?
C# with NAudio/CSCore can do WASAPI, but the .NET garbage collector can pause any thread,
including the audio thread, and a pause at a 128-frame buffer is an audible click. All-C++
(JUCE) is the other credible option: it has a mature VST3 host, but its UI toolkit looks dated and
the C++ is less safe. **WinUI 3 for the UI plus a Rust engine** is the best split, and the
engine code already exists and runs against your GoXLR.

### What happens to the React UI?
It is no longer the desktop app, which removes localhost:1420 from the product. It stays only
as the optional phone/tablet web panel, served by the daemon from Phase 3.

## 2. Process model

```
 ┌──────────── Mixforge.exe (WinUI 3, C#) ─────────────┐
 │ Views ⇄ ViewModels (ObservableObject)               │
 │            ▲ DispatcherQueue (UI thread)            │
 │ MixerStore ── reconcile State / Meters by id        │
 │ PipeTransport  \\.\pipe\mixforge  (JSON lines)      │
 └───────────────────────┬─────────────────────────────┘
                         │ commands ↓   state/meters ↑
 ┌───────────────────────┴──────── mixforged (Rust, tray / service) ───────────────────────────┐
 │                                                                                             │
 │   pipe / ws clients ──┐                                                                     │
 │   GoXLR hw thread ────┼──► mpsc ──► HUB (single owner of state) ──┬──► Engine params ──► RT │
 │   OBS / MIDI / OS ────┘                                           ├──► HardwareCommand ──► hw│
 │                                                                   └──► broadcast State ──► all│
 │                                                                                             │
 │   GoXLR hw thread: TUSBAUDIO notification event → read fader/button state → HardwareEvent   │
 │   Audio RT thread: WASAPI callbacks, lock-free params/meters (never touches the hub)        │
 └─────────────────────────────────────────────────────────────────────────────────────────────┘
```

**Rule 1: one owner.** Only the hub task mutates state. UI, hardware, OBS and MIDI all submit
inputs into one queue. Details and code are in [STATE_SYNC.md](STATE_SYNC.md).

**Rule 2: the UI is a projection.** The WinUI app holds no state of its own. It renders the hub's
snapshots and sends intents.

## 3. Audio engine
- Graph: `Source → ChannelStrip (DSP) → matrix gain[ch][bus] → Bus → Sink`.
- The RT callback never locks, allocates or logs. Parameters are atomics; graph swaps use `arc-swap`.
- One master clock (the GoXLR when present). Other devices go through `rubato` resamplers.
- 128–256 frames at 48 kHz. `Processor` reports latency for delay compensation.

## 4. GoXLR hardware (Windows)
- USB IDs: VID `0x1220`, PID `0x8FE0` (Full) / `0x8FE4` (Mini).
- Transport: `goxlr_audioapi_x64.dll` (path from the registry, CLSID
  `{024D0372-641F-4B7B-8140-F4DFE458C982}`), calls `TUSBAUDIO_ClassVendorRequestOut/In`.
- Input: `TUSBAUDIO_RegisterDeviceNotification` with a Win32 event. On signal, read the
  button/fader state and diff it against the previous state into `HardwareEvent`s. This is
  event-driven, not busy polling.
- Only one program may control the device. The official GoXLR app and goxlr-utility must not run
  at the same time as Mixforge.
- Hardware mode: the GoXLR's internal mixer does the routing and volume, and the hub sends
  `ChannelVolume` / `Route` commands. Hybrid mode: the GoXLR is an interface plus controller, and
  the Rust engine processes the audio.

## 5. Protocol
The same JSON messages go over the named pipe (one message per line) and the WebSocket. See
`crates/protocol`. Every mutation is broadcast to all clients as a new `state`.

## 6. Repository layout
```
crates/protocol   wire types
crates/engine     real-time audio
crates/usb        GoXLR discovery, HardwareDevice trait, hardware thread, simulated device
crates/daemon     mixforged: hub (state owner), pipe + WebSocket servers
apps/windows      WinUI 3 desktop app (C#)             ← next
apps/ui           React web panel (phones/tablets; optional)
```
