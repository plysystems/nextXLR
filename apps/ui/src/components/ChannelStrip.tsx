import { client, useMeter } from "../api/client";
import type { Channel } from "../api/types";

function peakToPercent(peak: number) {
  // -60 dBFS .. 0 dBFS → 0..100 %
  if (peak <= 0) return 0;
  const db = 20 * Math.log10(peak);
  return Math.max(0, Math.min(100, ((db + 60) / 60) * 100));
}

export function ChannelStrip({ channel }: { channel: Channel }) {
  const peak = useMeter(channel.id);
  const subtitle = channel.kind === "application" ? channel.process : channel.kind;

  return (
    <div className={`strip ${channel.muted ? "muted" : ""}`}>
      <div className="strip-name">{channel.name}</div>
      <div className="strip-kind">{subtitle}</div>

      <div className="strip-body">
        <div className="meter" aria-label={`${channel.name} level`}>
          <div className="meter-fill" style={{ height: `${peakToPercent(peak)}%` }} />
        </div>
        <input
          className="fader"
          type="range"
          min={0}
          max={1}
          step={0.001}
          value={channel.volume}
          aria-label={`${channel.name} volume`}
          onChange={(e) =>
            client.send({ type: "set_volume", channel: channel.id, volume: Number(e.target.value) })
          }
        />
      </div>

      <div className="strip-value">{Math.round(channel.volume * 100)}</div>
      <button
        className="mute"
        aria-pressed={channel.muted}
        onClick={() => client.send({ type: "set_mute", channel: channel.id, muted: !channel.muted })}
      >
        {channel.muted ? "Muted" : "Mute"}
      </button>
    </div>
  );
}
