import { client } from "../api/client";
import type { MixerState } from "../api/types";

/** Grid view of the routing matrix. The drag & drop node view comes in Phase 2. */
export function RoutingMatrix({ state }: { state: MixerState }) {
  const routed = new Set(state.routes.map((r) => `${r.channel}:${r.bus}`));

  return (
    <table className="matrix">
      <thead>
        <tr>
          <th />
          {state.buses.map((b) => (
            <th key={b.id}>{b.name}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {state.channels.map((c) => (
          <tr key={c.id}>
            <th scope="row">{c.name}</th>
            {state.buses.map((b) => {
              const on = routed.has(`${c.id}:${b.id}`);
              return (
                <td key={b.id}>
                  <button
                    className={`cell ${on ? "on" : ""}`}
                    aria-pressed={on}
                    aria-label={`Route ${c.name} to ${b.name}`}
                    onClick={() =>
                      client.send({ type: "set_route", channel: c.id, bus: b.id, gain: on ? 0 : 1 })
                    }
                  />
                </td>
              );
            })}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
