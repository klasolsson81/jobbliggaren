import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { AdminValueRegion } from "./view-models";

/**
 * The Tjänster card's rows as the page holds them (#1982, ADR 0150 D2). A row is what the API observed, with the
 * instant its source was sampled, or `unknown` when the API could not tell. No row says that a service works:
 * a Worker heartbeat shows a live process, a reachable store answers its check, and a recorded import run shows
 * that the job ran and was recorded. None of them is verified function, so none of them is worded as one.
 */
interface Sampled {
  readonly sampledAt: string;
  /** True once the page holds the sample longer than the limit the API applied. */
  readonly stale: boolean;
}

type Unknown = { readonly kind: "unknown" };

export type AdminWorkerReading =
  | ({ readonly kind: "alive"; readonly heartbeatAt: string } & Sampled)
  | ({ readonly kind: "silent"; readonly heartbeatAt: string } & Sampled)
  | ({ readonly kind: "noServer" } & Sampled)
  | Unknown;

export type AdminVolatileStoreReading =
  | ({ readonly kind: "reachable" } & Sampled)
  | ({ readonly kind: "unreachable" } & Sampled)
  | Unknown;

export type AdminJobImportReading =
  | ({ readonly kind: "recorded"; readonly lastRunAt: string; readonly errors: number } & Sampled)
  | ({ readonly kind: "overdue"; readonly lastRunAt: string; readonly errors: number } & Sampled)
  | ({ readonly kind: "noneRecorded" } & Sampled)
  | Unknown;

export interface AdminServicesReading {
  readonly backgroundJobs: AdminWorkerReading;
  readonly volatileStore: AdminVolatileStoreReading;
  readonly jobImport: AdminJobImportReading;
  /** The limit the API applied, so a row the page keeps ages by the same rule. */
  readonly staleAfterMs: number;
  /** The age past which the API calls the import overdue; the overdue row words it. */
  readonly importOverdueAfterSeconds: number;
}

const UNKNOWN: Unknown = { kind: "unknown" };

// The schema gives each state exactly its fields. A row that reaches a mapper without them is unreachable through
// the schema; it is read as unknown, so the page never invents a time.
function worker({ state, sampledAt, heartbeatAt }: ServicesObservationDto["backgroundJobs"]): AdminWorkerReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  if (state === "NoServer") return { kind: "noServer", sampledAt, stale: false };
  if (heartbeatAt === null) return UNKNOWN;
  return { kind: state === "Alive" ? "alive" : "silent", heartbeatAt, sampledAt, stale: false };
}

function volatileStore({ state, sampledAt }: ServicesObservationDto["volatileStore"]): AdminVolatileStoreReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  return { kind: state === "Reachable" ? "reachable" : "unreachable", sampledAt, stale: false };
}

function jobImport({ state, sampledAt, lastRunAt, errors }: ServicesObservationDto["jobImport"]): AdminJobImportReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  if (state === "NoneRecorded") return { kind: "noneRecorded", sampledAt, stale: false };
  if (lastRunAt === null || errors === null) return UNKNOWN;
  return { kind: state === "Recorded" ? "recorded" : "overdue", lastRunAt, errors, sampledAt, stale: false };
}

export function toServicesReading(dto: ServicesObservationDto): AdminServicesReading {
  return {
    backgroundJobs: worker(dto.backgroundJobs),
    volatileStore: volatileStore(dto.volatileStore),
    jobImport: jobImport(dto.jobImport),
    staleAfterMs: dto.staleAfterSeconds * 1000,
    importOverdueAfterSeconds: dto.importOverdueAfterSeconds,
  };
}

function sampledRows(services: AdminServicesReading): ReadonlyArray<Sampled> {
  return [services.backgroundJobs, services.volatileStore, services.jobImport]
    .flatMap((row) => (row.kind === "unknown" ? [] : [row]));
}

/**
 * The same rows aged to `now`: a sample the page has kept past the API's limit is old although the API judged it
 * young when it answered. The verdict itself is kept: it is what was observed, and the card marks it old.
 */
export function ageServicesReading(services: AdminServicesReading, now: number): AdminServicesReading {
  const age = <R extends Sampled>(row: R): R =>
    !row.stale && now - Date.parse(row.sampledAt) > services.staleAfterMs ? { ...row, stale: true } : row;
  const { backgroundJobs, volatileStore: store, jobImport: run } = services;
  return {
    ...services,
    backgroundJobs: backgroundJobs.kind === "unknown" ? backgroundJobs : age(backgroundJobs),
    volatileStore: store.kind === "unknown" ? store : age(store),
    jobImport: run.kind === "unknown" ? run : age(run),
  };
}

/** The newest sample time among the rows that have one; null when every row is unknown. */
export function newestServiceSample(services: AdminServicesReading): string | null {
  const times = sampledRows(services).map((row) => row.sampledAt);
  return times.length === 0 ? null : times.reduce((a, b) => (Date.parse(a) >= Date.parse(b) ? a : b));
}

export function hasStaleServiceRow(services: AdminServicesReading): boolean {
  return sampledRows(services).some((row) => row.stale);
}

/**
 * The Tjänster region for a set of rows, aged to `now` when the page has a clock (the preview has none). It carries
 * the newest sample time and whether any row is old, which the card prints once, as ADR 0150 D2 lets a loaded
 * region do.
 */
export function servicesRegion(services: AdminServicesReading, now?: number): AdminValueRegion<AdminServicesReading> {
  const aged = now ? ageServicesReading(services, now) : services;
  const sampledAt = newestServiceSample(aged);
  return { kind: "loaded", data: aged, ...(sampledAt ? { sampledAt } : {}), stale: hasStaleServiceRow(aged) };
}
