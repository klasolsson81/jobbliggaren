import type { HostMetricState, HostObservationDto } from "@/lib/dto/admin-host";
import type { AdminValueRegion } from "./view-models";

/**
 * The Server card's readings as the page holds them (ADR 0150 D2, #1982). A reading is a value with the
 * instant the host was sampled, or the reason it has none, never a number that stands for an unknown:
 * `collecting` is the CPU's first window after the API started, `notObservable` is a deployment that cannot
 * supply the reading (and is never worded as "Kommer snart", which says a capability is not built), and
 * `failed` is a tick that could not produce a trustworthy value.
 */
export type AdminHostReading<V> =
  | { readonly kind: "value"; readonly value: V; readonly sampledAt: string; readonly stale: boolean }
  | { readonly kind: "collecting" }
  | { readonly kind: "notObservable" }
  | { readonly kind: "failed" };

export interface AdminCpuValue {
  readonly percent: number;
  readonly windowSeconds: number;
}

export interface AdminMemoryValue {
  readonly percent: number;
  readonly usedBytes: number;
  readonly totalBytes: number;
}

export interface AdminDiskValue {
  readonly percent: number;
  readonly freeBytes: number;
  readonly totalBytes: number;
}

export interface AdminServerReading {
  readonly cpu: AdminHostReading<AdminCpuValue>;
  readonly memory: AdminHostReading<AdminMemoryValue>;
  readonly disk: AdminHostReading<AdminDiskValue>;
  /** The limit the API applied, so a reading the page keeps ages by the same rule. */
  readonly staleAfterMs: number;
}

interface Wire<V> {
  readonly state: HostMetricState;
  readonly sampledAt: string | null;
  readonly value: V | null;
}

function reading<V>(metric: Wire<V>): AdminHostReading<V> {
  switch (metric.state) {
    case "Available":
    case "Stale":
      // The schema guarantees a value and a sample time together in exactly these two states.
      return metric.value !== null && metric.sampledAt !== null
        ? { kind: "value", value: metric.value, sampledAt: metric.sampledAt, stale: metric.state === "Stale" }
        : { kind: "failed" };
    case "Collecting":
      return { kind: "collecting" };
    case "NotObservable":
      return { kind: "notObservable" };
    case "Failed":
      return { kind: "failed" };
  }
}

export function toServerReading(dto: HostObservationDto): AdminServerReading {
  return {
    cpu: reading(dto.cpu),
    memory: reading(dto.memory),
    disk: reading(dto.disk),
    staleAfterMs: dto.staleAfterSeconds * 1000,
  };
}

const METERS = ["cpu", "memory", "disk"] as const;

function values(server: AdminServerReading): ReadonlyArray<{ readonly sampledAt: string; readonly stale: boolean }> {
  return METERS.flatMap((meter) => {
    const metric = server[meter];
    return metric.kind === "value" ? [metric] : [];
  });
}

/**
 * The same readings aged to `now`: a value the page has kept past the API's stale limit is stale although the
 * API said it was young when it answered. The API's own `Stale` is kept.
 */
export function ageServerReading(server: AdminServerReading, now: number): AdminServerReading {
  const age = <V,>(metric: AdminHostReading<V>): AdminHostReading<V> =>
    metric.kind === "value" && !metric.stale && now - Date.parse(metric.sampledAt) > server.staleAfterMs
      ? { ...metric, stale: true }
      : metric;
  return { ...server, cpu: age(server.cpu), memory: age(server.memory), disk: age(server.disk) };
}

/** The newest sample time among the readings that have one; null when none has. */
export function newestSampledAt(server: AdminServerReading): string | null {
  const times = values(server).map((metric) => metric.sampledAt);
  return times.length === 0 ? null : times.reduce((a, b) => (Date.parse(a) >= Date.parse(b) ? a : b));
}

export function hasStaleReading(server: AdminServerReading): boolean {
  return values(server).some((metric) => metric.stale);
}

/**
 * The Server region for a set of readings, aged to `now` when the page has a clock (the preview has none, so
 * only the API's own `Stale` shows there). It carries the newest sample time and whether any reading is
 * stale, which the card prints once, as ADR 0150 D2 lets a loaded region do.
 */
export function serverRegion(server: AdminServerReading, now?: number): AdminValueRegion<AdminServerReading> {
  const aged = now ? ageServerReading(server, now) : server;
  const sampledAt = newestSampledAt(aged);
  return { kind: "loaded", data: aged, ...(sampledAt ? { sampledAt } : {}), stale: hasStaleReading(aged) };
}
