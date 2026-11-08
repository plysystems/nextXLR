// Mirrors crates/protocol/src/lib.rs. Replace with ts-rs generated types (roadmap Phase 1).

export type ChannelId = number;
export type BusId = number;

export type ChannelKind =
  | { kind: "microphone" }
  | { kind: "system" }
  | { kind: "application"; process: string }
  | { kind: "sampler" }
  | { kind: "virtual" };

export type Channel = ChannelKind & {
  id: ChannelId;
  name: string;
  volume: number;
  muted: boolean;
};

export interface Bus {
  id: BusId;
  name: string;
}

export interface Route {
  channel: ChannelId;
  bus: BusId;
  gain: number;
}

export interface HardwareInfo {
  model: string;
  serial: string | null;
}

export interface MixerState {
  channels: Channel[];
  buses: Bus[];
  routes: Route[];
  hardware: HardwareInfo[];
}

export interface ChannelMeter {
  channel: ChannelId;
  peak: number;
}

export type ClientMessage =
  | { type: "get_state" }
  | { type: "set_volume"; channel: ChannelId; volume: number }
  | { type: "set_mute"; channel: ChannelId; muted: boolean }
  | { type: "set_route"; channel: ChannelId; bus: BusId; gain: number };

export type ServerMessage =
  | { type: "hello"; server: string; version: string; api: number }
  | ({ type: "state" } & MixerState)
  | { type: "meters"; levels: ChannelMeter[] }
  | { type: "error"; message: string };
