import { useMixer } from "./api/client";
import { ChannelStrip } from "./components/ChannelStrip";
import { RoutingMatrix } from "./components/RoutingMatrix";

export function App() {
  const { status, state, lastError } = useMixer();

  return (
    <div className="app">
      <header className="topbar">
        <h1>Mixforge</h1>
        <span className={`status status-${status}`}>
          {status === "open" ? "Connected" : status === "connecting" ? "Connecting…" : "Daemon offline"}
        </span>
        <span className="hardware">
          {state?.hardware.length
            ? state.hardware.map((h) => h.model).join(", ")
            : "No hardware: software mixer"}
        </span>
      </header>

      {lastError && <div className="error">{lastError}</div>}

      {!state ? (
        <p className="empty">
          Waiting for <code>mixforged</code> on ws://127.0.0.1:14565/ws. Start it with{" "}
          <code>cargo run -p mixforged</code>.
        </p>
      ) : (
        <main>
          <section>
            <h2>Mixer</h2>
            <div className="strips">
              {state.channels.map((c) => (
                <ChannelStrip key={c.id} channel={c} />
              ))}
            </div>
          </section>
          <section>
            <h2>Routing</h2>
            <RoutingMatrix state={state} />
          </section>
        </main>
      )}
    </div>
  );
}
