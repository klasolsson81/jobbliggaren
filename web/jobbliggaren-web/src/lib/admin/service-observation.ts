import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { JpFormatter } from "@/lib/i18n/format";
import { formatStaleLimit } from "./host-observation";
import type { AdminValueRegion } from "./view-models";

/**
 * The Tjänster card's rows as the page holds them (#1982, ADR 0150 D2). A row is what the API observed, with the
 * instant its source was sampled, or `unknown` when the API could not tell. No row says that a service works:
 * a Worker heartbeat shows a live process, a reachable store answers its check, and a recorded import run shows
 * that the job ran and was recorded. None of them is verified function, so none of them is worded as one.
 */
interface Sampled {
  readonly sampledAt: string;
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
  /** The limit the API applied, so a row the page keeps is withdrawn by the same rule. */
  readonly staleAfterMs: number;
  /** The age past which the API calls the import overdue; the overdue row words it. */
  readonly importOverdueAfterSeconds: number;
}

const UNKNOWN: Unknown = { kind: "unknown" };

// The schema gives each state exactly its fields. A row that reaches a mapper without them is unreachable through
// the schema; it is read as unknown, so the page never invents a time.
function worker({ state, sampledAt, heartbeatAt }: ServicesObservationDto["backgroundJobs"]): AdminWorkerReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  if (state === "NoServer") return { kind: "noServer", sampledAt };
  if (heartbeatAt === null) return UNKNOWN;
  return { kind: state === "Alive" ? "alive" : "silent", heartbeatAt, sampledAt };
}

function volatileStore({ state, sampledAt }: ServicesObservationDto["volatileStore"]): AdminVolatileStoreReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  return { kind: state === "Reachable" ? "reachable" : "unreachable", sampledAt };
}

function jobImport({ state, sampledAt, lastRunAt, errors }: ServicesObservationDto["jobImport"]): AdminJobImportReading {
  if (state === "Unknown" || sampledAt === null) return UNKNOWN;
  if (state === "NoneRecorded") return { kind: "noneRecorded", sampledAt };
  if (lastRunAt === null || errors === null) return UNKNOWN;
  return { kind: state === "Recorded" ? "recorded" : "overdue", lastRunAt, errors, sampledAt };
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

/** True when the page holds `row` longer than the limit the API applied, judged by the row's own sample. */
function isOld(row: Sampled, staleAfterMs: number, now: number): boolean {
  return now - Date.parse(row.sampledAt) > staleAfterMs;
}

/**
 * The rows as they may be shown at `now`. A row the page has kept longer than the API's limit is withdrawn: it
 * becomes unknown, as the API itself would answer for a sample that old, and is never shown as an old verdict.
 * Each row is judged by its own sample, so a young row beside an old one keeps its verdict and its age.
 */
export function withdrawOldServiceRows(services: AdminServicesReading, now: number): AdminServicesReading {
  const old = (row: Sampled) => isOld(row, services.staleAfterMs, now);
  const { backgroundJobs: heartbeat, volatileStore: store, jobImport: run } = services;
  return {
    ...services,
    backgroundJobs: heartbeat.kind !== "unknown" && old(heartbeat) ? UNKNOWN : heartbeat,
    volatileStore: store.kind !== "unknown" && old(store) ? UNKNOWN : store,
    jobImport: run.kind !== "unknown" && old(run) ? UNKNOWN : run,
  };
}

/** The newest sample time among the rows that have one; null when every row is unknown. */
export function newestServiceSample(services: AdminServicesReading): string | null {
  const times = sampledRows(services).map((row) => row.sampledAt);
  return times.length === 0 ? null : times.reduce((a, b) => (Date.parse(a) >= Date.parse(b) ? a : b));
}

/** True when any row's own sample is older than the API's limit at `now`. */
export function hasOldServiceRow(services: AdminServicesReading, now: number): boolean {
  return sampledRows(services).some((row) => isOld(row, services.staleAfterMs, now));
}

/**
 * The Tjänster region at `now` (the preview has no clock and withdraws nothing). The newest sample time and whether
 * any row is old are read from the rows as the API sent them, before the old ones are withdrawn, so a card whose rows
 * have all been withdrawn still says when it was sampled and that the sample is old (ADR 0150 D2).
 */
export function servicesRegion(services: AdminServicesReading, now?: number): AdminValueRegion<AdminServicesReading> {
  const sampledAt = newestServiceSample(services);
  const clocked = now !== undefined && now > 0;
  return {
    kind: "loaded",
    data: clocked ? withdrawOldServiceRows(services, now) : services,
    ...(sampledAt ? { sampledAt } : {}),
    stale: clocked && hasOldServiceRow(services, now),
  };
}

/** The rows as the attention card may read them at `now`: the same withdrawal as the card's (`servicesRegion`). */
export function servicesAt(dto: ServicesObservationDto, now: number | undefined): AdminServicesReading {
  const services = toServicesReading(dto);
  return now !== undefined && now > 0 ? withdrawOldServiceRows(services, now) : services;
}

/** A limit as the card words it, with the number and its unit kept on one line ("25 minuter"). */
export function formatServiceLimit(format: JpFormatter, seconds: number): string {
  return formatStaleLimit(format, seconds).replaceAll(" ", "\u00A0");
}
