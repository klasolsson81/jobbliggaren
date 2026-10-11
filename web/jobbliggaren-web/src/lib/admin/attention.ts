import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { AwaitingObservation, OverviewObservation } from "./overview";
import {
  servicesAt,
  type AdminJobImportReading,
  type AdminVolatileStoreReading,
  type AdminWorkerReading,
} from "./service-observation";

/**
 * What the attention card says of the Backup and Tjänster cards (#2084). Each state those cards show in the warning
 * form becomes one line, and a source or row that could not be read says so, so that the attention card never reads
 * as an all-clear beside a silent Worker or a backup that is off. Nothing here is a verdict of its own: every line
 * restates what its card shows, and a good state adds nothing.
 */
export type BackupRaised =
  | "lastSuccess.missing"
  | "lastSuccess.unreadable"
  | "lastSuccess.invalid"
  | "lastSuccess.overdue"
  | "nextRun.inactive"
  | "nextRun.notInstalled";

export type BackupAttention =
  | { readonly kind: "unread"; readonly why: "failed" | "loading" | "awaiting" }
  | { readonly kind: "read"; readonly raised: ReadonlyArray<BackupRaised>; readonly timerUnknown: boolean };

export function backupAttention(observation: AwaitingObservation<BackupObservationData>): BackupAttention {
  if (observation.kind !== "loaded" && observation.kind !== "empty") return { kind: "unread", why: observation.kind };
  const { lastSuccess, timer } = observation.data;
  const raised: BackupRaised[] = [];
  if (lastSuccess.state !== "recorded") raised.push(`lastSuccess.${lastSuccess.state}`);
  else if (lastSuccess.overdue) raised.push("lastSuccess.overdue");
  if (timer.state === "inactive" || timer.state === "notInstalled") raised.push(`nextRun.${timer.state}`);
  return { kind: "read", raised, timerUnknown: timer.state === "unknown" };
}

export type ServiceRow = "backgroundJobs" | "volatileStore" | "jobImport";

const SERVICE_ROWS: ReadonlyArray<ServiceRow> = ["backgroundJobs", "volatileStore", "jobImport"];

/**
 * One line per warning the Tjänster card shows, keyed by its row and kind: an overdue import run with errors raises
 * both its overdue line and its errors line, as the card shows both.
 */
export type ServiceRaised =
  | { readonly row: "backgroundJobs"; readonly kind: "silent"; readonly heartbeatAt: string }
  | { readonly row: "backgroundJobs"; readonly kind: "noServer" }
  | { readonly row: "volatileStore"; readonly kind: "unreachable" }
  | { readonly row: "jobImport"; readonly kind: "overdue"; readonly lastRunAt: string }
  | { readonly row: "jobImport"; readonly kind: "noneRecorded" }
  | { readonly row: "jobImport"; readonly kind: "errors"; readonly count: number };

export type ServicesAttention =
  | { readonly kind: "unread"; readonly why: "failed" | "loading" }
  | {
      readonly kind: "read";
      readonly raised: ReadonlyArray<ServiceRaised>;
      readonly undetermined: ReadonlyArray<ServiceRow>;
      /** The overdue row's limit, which its line words as the card does. */
      readonly importOverdueAfterSeconds: number;
    };

function workerRaised(row: AdminWorkerReading): ReadonlyArray<ServiceRaised> {
  if (row.kind === "silent") return [{ row: "backgroundJobs", kind: "silent", heartbeatAt: row.heartbeatAt }];
  return row.kind === "noServer" ? [{ row: "backgroundJobs", kind: "noServer" }] : [];
}

function storeRaised(row: AdminVolatileStoreReading): ReadonlyArray<ServiceRaised> {
  return row.kind === "unreachable" ? [{ row: "volatileStore", kind: "unreachable" }] : [];
}

function importRaised(row: AdminJobImportReading): ReadonlyArray<ServiceRaised> {
  if (row.kind === "noneRecorded") return [{ row: "jobImport", kind: "noneRecorded" }];
  if (row.kind !== "recorded" && row.kind !== "overdue") return [];
  const errors: ReadonlyArray<ServiceRaised> = row.errors > 0 ? [{ row: "jobImport", kind: "errors", count: row.errors }] : [];
  return row.kind === "overdue" ? [{ row: "jobImport", kind: "overdue", lastRunAt: row.lastRunAt }, ...errors] : errors;
}

/**
 * The Tjänster card's warnings at `now`, read as the card reads them: a row the page has kept past the API's limit is
 * withdrawn (`servicesAt`), so it is undetermined here too and never raises a line from an old verdict.
 */
export function servicesAttention(observation: OverviewObservation<ServicesObservationDto>, now: number | undefined): ServicesAttention {
  if (observation.kind !== "loaded" && observation.kind !== "empty") return { kind: "unread", why: observation.kind };
  const services = servicesAt(observation.data, now);
  return {
    kind: "read",
    raised: [...workerRaised(services.backgroundJobs), ...storeRaised(services.volatileStore), ...importRaised(services.jobImport)],
    undetermined: SERVICE_ROWS.filter((row) => services[row].kind === "unknown"),
    importOverdueAfterSeconds: services.importOverdueAfterSeconds,
  };
}

/**
 * True when the Backup or Tjänster card shows a state in the warning form at `now`. An unread source, or a row
 * withdrawn as too old, raises nothing.
 */
export function backupOrServicesRaised(
  backup: AwaitingObservation<BackupObservationData>,
  services: OverviewObservation<ServicesObservationDto>,
  now: number | undefined,
): boolean {
  const fromBackup = backupAttention(backup);
  const fromServices = servicesAttention(services, now);
  return (fromBackup.kind === "read" && fromBackup.raised.length > 0)
    || (fromServices.kind === "read" && fromServices.raised.length > 0);
}
