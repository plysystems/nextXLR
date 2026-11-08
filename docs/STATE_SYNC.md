# Two-way sync: hardware ⇄ state ⇄ UI

## Why the first test build didn't sync

The behaviour you saw wasn't a race or event-loop bug. **The links were never built**, because
the bootstrap only did discovery:

| Direction | What was missing |
|---|---|
| **GoXLR → UI** | `mixforge-usb` only *enumerated* the device (it read the USB descriptor to print "GoXLR detected"). Nothing ever opened the TC-Helicon driver API, registered for input notifications, or read fader/button state. The `HardwareDevice` trait had **no implementation and no thread running it**, so a fader move produced no event anywhere in the program. |
| **UI → GoXLR** | UI commands changed the daemon's `MixerState` and the engine's atomics, and nothing else. No code path sent anything to the device. The GoXLR does its routing in its *own* DSP, so its sound was unchanged. |
| **UI → audio** | The software engine only processes **one** path: default input → slot 0 → bus 0 → default output. On your PC the default input is the GoXLR "Broadcast Stream Mix" endpoint, not the mic. Chat/Music/Game/System are placeholders with no audio source behind them, and Mic→Headphones is off by default. So most UI controls had no audible target. |
| **Architecture** | State lived in an `Arc<Mutex<Engine>>` that each WebSocket handler mutated directly. Nothing gave a hardware event a defined way back in, and no fan-out sent changes to the hardware. |

## The fix: a single-owner hub (implemented)

`crates/daemon/src/hub.rs`:

1. **One queue, one owner.** Every input becomes `Input::Client(..)` or `Input::Hardware(..)` on a
   single `mpsc` queue, consumed by one task that owns the `Engine`. There is no shared mutable
   state and no lock ordering, and changes apply in a strict order.
2. **One mutation path.** `Hub::apply(change, origin)` updates the engine (audio follows
   immediately through the lock-free params), then returns `Effect`s:
   - `Hardware(..)`: fader display, mute LED, device mixer volume/route
   - `Broadcast(State)`: every UI, panel and Stream Deck
   - `Reply(..)`: errors / `get_state`, sent only to the requester
3. **Echo suppression by origin.** A hardware fader move is *not* sent back to the device as a
   volume command. The mute LED is software-owned, so it is always set.
4. **Soft takeover.** GoXLR faders have no motors. After the UI changes a fader-bound volume the
   physical knob is stale. Small nudges are ignored until the knob comes within 3% of the
   software value or crosses it. Without this, touching a fader makes the volume jump.
5. **Hardware thread.** USB I/O is blocking, so it runs on its own thread (`mixforge-usb::thread`).
   It forwards `HardwareEvent`s into the hub queue and drains `HardwareCommand`s out to the
   device. The real GoXLR implementation waits on the TUSBAUDIO notification event instead of
   polling.
6. **Lossless clients.** Each client subscribes to the broadcast *before* requesting its initial
   snapshot. If a client falls behind (`Lagged`), it gets a fresh full snapshot instead of drifting.

Tests (`cargo test -p mixforged`) cover hardware→state→clients, mute button + LED,
UI→hardware, soft takeover, error isolation and disconnect.

With `MIXFORGE_SIM_HW=1` the daemon attaches a simulated GoXLR, so the loop can be exercised
without the real driver:
```bash
curl -X POST "http://127.0.0.1:14565/dev/sim/fader?fader=B&pos=0.3"   # UI's Chat fader jumps to 30
curl -X POST "http://127.0.0.1:14565/dev/sim/mute?fader=A"            # Mic mutes in every UI
curl http://127.0.0.1:14565/dev/sim/received                          # what the hub sent the device
```

## WinUI 3 client side (C#)

The client has three jobs: marshal onto the UI thread, reconcile by id, and keep server echoes
from fighting a slider the user is dragging.

```csharp
// MixerStore.cs — the only place incoming messages touch view models.
public sealed class MixerStore
{
    private readonly DispatcherQueue _ui;
    private readonly IMixerTransport _transport;          // PipeTransport or WebSocketTransport
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public MixerStore(DispatcherQueue ui, IMixerTransport transport)
    {
        _ui = ui;
        _transport = transport;
        _transport.MessageReceived += OnMessage;           // raised on a background thread
    }

    private void OnMessage(ServerMessage msg) => _ui.TryEnqueue(() =>
    {
        switch (msg)
        {
            case StateMessage s: Reconcile(s.Channels); break;
            case MetersMessage m:
                foreach (var l in m.Levels) Find(l.Channel)?.PushPeak(l.Peak);
                break;
        }
    });

    // Update existing VMs in place (keeps bindings, focus and animations); add/remove by id.
    private void Reconcile(IReadOnlyList<Channel> channels)
    {
        foreach (var c in channels)
        {
            var vm = Find(c.Id);
            if (vm is null) Channels.Add(new ChannelViewModel(c, _transport));
            else vm.ApplyFromServer(c);
        }
        for (int i = Channels.Count - 1; i >= 0; i--)
            if (!channels.Any(c => c.Id == Channels[i].Id)) Channels.RemoveAt(i);
    }

    private ChannelViewModel? Find(uint id) => Channels.FirstOrDefault(c => c.Id == id);
}

// ChannelViewModel.cs
public partial class ChannelViewModel : ObservableObject
{
    private readonly IMixerTransport _transport;
    private bool _applyingServer;      // suppresses re-sending values that came FROM the server
    private Channel? _deferred;        // server update that arrived mid-drag

    public uint Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _peak;

    private bool _isDragging;
    public bool IsDragging             // bound to the slider's PointerPressed / PointerReleased
    {
        get => _isDragging;
        set
        {
            _isDragging = value;
            if (!value && _deferred is { } d) { _deferred = null; ApplyFromServer(d); }
        }
    }

    public ChannelViewModel(Channel c, IMixerTransport transport)
    {
        _transport = transport;
        Id = c.Id;
        ApplyFromServer(c);
    }

    public void ApplyFromServer(Channel c)
    {
        if (IsDragging) { _deferred = c; return; }   // user wins while touching the control
        _applyingServer = true;
        Name = c.Name; Volume = c.Volume; Muted = c.Muted;
        _applyingServer = false;
    }

    // Generated by CommunityToolkit when the property changes.
    partial void OnVolumeChanged(double value)
    {
        if (!_applyingServer) _transport.SendCoalesced($"vol:{Id}", new SetVolume(Id, value));
    }

    partial void OnMutedChanged(bool value)
    {
        if (!_applyingServer) _transport.Send(new SetMute(Id, value));
    }

    // Peak hold with decay; meters bypass the state path entirely.
    public void PushPeak(double p) => Peak = Math.Max(p, Peak * 0.85);
}
```

`SendCoalesced(key, msg)` keeps only the latest message per key and flushes about every 16 ms, so
a fast drag sends at most 60 updates per second. The hub still broadcasts each resulting state,
which keeps the other views (phone panel, Stream Deck, physical fader LEDs) moving live.

### Pipe transport sketch
```csharp
var pipe = new NamedPipeClientStream(".", "mixforge", PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync(ct);
var reader = new StreamReader(pipe);
var writer = new StreamWriter(pipe) { AutoFlush = true };
while (await reader.ReadLineAsync(ct) is { } line)
    MessageReceived?.Invoke(JsonSerializer.Deserialize<ServerMessage>(line, Json.Options)!);
// On disconnect: back off, reconnect, send {"type":"get_state"} to resync.
```

## Mic mute = two device commands

Muting the mic on a GoXLR takes **both** `SetChannelState(Mic, Muted)` (removes it from the
mixes) **and** the DSP key `MicInputMute` (0x158) (mutes the input itself, which also silences the
Mic Monitor in the headphones). Sending only the first left the monitor open. The hub derives one
effective state — fader mute ∨ mic mute (cough) ∨ !@#$?* held — and every path (app button,
cough button, mute button, censor, profile load) sends both commands via `Hub::mic_mute_cmds`.

## Mic DSP is write-only

Gate / compressor / EQ / de-esser values can't be read back from the device. The daemon only
pushes them after they're known (set in the app, or a profile/default that contains a `mic`
section), so loading an older profile never resets the device's mic processing to defaults.
