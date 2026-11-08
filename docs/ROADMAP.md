# Roadmap (native Windows)

## Phase 1: Foundation. "Real hardware, native app, in sync"
- [x] Cargo workspace, protocol, daemon, engine with lock-free params/meters
- [x] Single-owner state hub; hardware thread; soft takeover; simulated device; sync tests
- [x] **GoXLR driver** (`crates/usb/src/goxlr`): TUSBAUDIO DLL bindings, packet framing,
      notification-driven input reads, fader binding, channel volume/mute, mute LEDs
- [ ] GoXLR driver, part 2: hardware routing table, scribble strips, colour maps, cough/bleep,
      reconnect on unplug
- [x] Named-pipe server `\\.\pipe\mixforge` (JSON lines, local clients only) next to the WebSocket
- [ ] Pipe: explicit current-user-only security descriptor (today: Windows default DACL)
- [x] `apps/windows`: WinUI 3 app (store, ChannelViewModel, PipeTransport, strips, meters, routing)
- [ ] App: tray icon, auto-start/launch the daemon, settings page, drag & drop routing view
- [ ] Input/output device selection; the Mic channel uses the real mic, not the default endpoint
- [ ] Single master clock + `rubato` drift compensation
- [ ] Persisted profiles in `%APPDATA%\Mixforge`

## Phase 2: Pro audio
- [ ] DSP chain per channel: gate, EQ, compressor, limiter
- [ ] RNNoise on the mic strip (wet/dry, VAD-driven gate)
- [ ] VST3 sandbox host + embedded editor windows (HWND parenting into WinUI)
- [ ] Sampler with unlimited banks; voice changer (pitch + formant)
- [ ] Drag & drop routing matrix (WinUI canvas / Win2D)

## Phase 3: Smart routing & OS integration
- [ ] Audio session enumeration, per-app capture (process loopback) + endpoint redirection
- [ ] App rules ("Discord.exe → Chat"), fader paging with soft takeover + LED page colours
- [ ] Virtual audio devices (signed driver) or GoXLR WDM endpoints
- [ ] Remote web panel (React app served by the daemon) with token pairing

## Phase 4: Ecosystem
- [ ] OBS (obs-websocket v5), Stream Deck plugin, MIDI learn + feedback
- [ ] Auto-profile switching on foreground app (`SetWinEventHook`)
- [ ] MSIX installer (daemon as startup task), auto-update, crash reporting
