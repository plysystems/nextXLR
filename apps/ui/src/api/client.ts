import { useSyncExternalStore } from "react";
import type { ClientMessage, MixerState, ServerMessage } from "./types";

export type ConnectionStatus = "connecting" | "open" | "closed";

const DEFAULT_URL = `ws://${location.hostname || "127.0.0.1"}:14565/ws`;

/**
 * Thin WebSocket client. The daemon owns all state: we render its latest snapshot and send
 * intents. Meters are kept in a separate store so 30 Hz updates don't re-render the whole app.
 */
class MixerClient {
  status: ConnectionStatus = "connecting";
  state: MixerState | null = null;
  meters = new Map<number, number>();
  lastError: string | null = null;

  private ws: WebSocket | null = null;
  private retryMs = 500;
  private listeners = new Set<() => void>();
  private meterListeners = new Set<() => void>();
  private meterVersion = 0;

  constructor(private url: string) {
    this.connect();
  }

  send(msg: ClientMessage) {
    if (this.ws?.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(msg));
  }

  subscribe = (fn: () => void) => {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  };

  subscribeMeters = (fn: () => void) => {
    this.meterListeners.add(fn);
    return () => this.meterListeners.delete(fn);
  };

  getMeterVersion = () => this.meterVersion;

  private snapshot = { status: this.status, state: this.state, lastError: this.lastError };
  getSnapshot = () => this.snapshot;

  private emit() {
    this.snapshot = { status: this.status, state: this.state, lastError: this.lastError };
    this.listeners.forEach((fn) => fn());
  }

  private connect() {
    this.status = "connecting";
    this.emit();
    const ws = new WebSocket(this.url);
    this.ws = ws;

    ws.onopen = () => {
      this.retryMs = 500;
      this.status = "open";
      this.emit();
    };
    ws.onclose = () => {
      this.status = "closed";
      this.emit();
      setTimeout(() => this.connect(), this.retryMs);
      this.retryMs = Math.min(this.retryMs * 2, 5000);
    };
    ws.onmessage = (ev) => this.handle(JSON.parse(ev.data) as ServerMessage);
  }

  private handle(msg: ServerMessage) {
    switch (msg.type) {
      case "state": {
        const { type: _type, ...state } = msg;
        this.state = state;
        this.emit();
        break;
      }
      case "meters":
        for (const m of msg.levels) {
          // Peak hold with decay, done client-side so every client can choose its own ballistics.
          const prev = this.meters.get(m.channel) ?? 0;
          this.meters.set(m.channel, Math.max(m.peak, prev * 0.85));
        }
        this.meterVersion++;
        this.meterListeners.forEach((fn) => fn());
        break;
      case "error":
        this.lastError = msg.message;
        this.emit();
        break;
      case "hello":
        console.info(`connected to ${msg.server} ${msg.version} (api v${msg.api})`);
        break;
    }
  }
}

const params = new URLSearchParams(location.search);
export const client = new MixerClient(params.get("ws") ?? DEFAULT_URL);

export function useMixer() {
  return useSyncExternalStore(client.subscribe, client.getSnapshot);
}

export function useMeter(channel: number): number {
  useSyncExternalStore(client.subscribeMeters, client.getMeterVersion);
  return client.meters.get(channel) ?? 0;
}
