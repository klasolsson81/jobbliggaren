import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { AwaitingObservation, OverviewObservation } from "./overview";
import {
  toServicesReading,
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

export type ServiceRaised =
  | { readonly row: "backgroundJobs"; readonly reading: Extract<AdminWorkerReading, { readonly kind: "silent" | "noServer" }> }
  | { readonly row: "volatileStore"; readonly reading: Extract<AdminVolatileStoreReading, { readonly kind: "unreachable" }> }
  | { readonly row: "jobImport"; readonly reading: Extract<AdminJobImportReading, { readonly kind: "overdue" | "noneRecorded" }> };

export type ServicesAttention =
  | { readonly kind: "unread"; readonly why: "failed" | "loading" }
  | {
      readonly kind: "read";
      readonly raised: ReadonlyArray<ServiceRaised>;
      readonly undetermined: ReadonlyArray<ServiceRow>;
      /** The overdue row's limit, which its line words as the card does. */
      readonly importOverdueAfterSeconds: number;
    };

export function servicesAttention(observation: OverviewObservation<ServicesObservationDto>): ServicesAttention {
  if (observation.kind !== "loaded" && observation.kind !== "empty") return { kind: "unread", why: observation.kind };
  const { backgroundJobs, volatileStore, jobImport, importOverdueAfterSeconds } = toServicesReading(observation.data);
  const raised: ServiceRaised[] = [];
  const undetermined: ServiceRow[] = [];
  if (backgroundJobs.kind === "silent" || backgroundJobs.kind === "noServer") raised.push({ row: "backgroundJobs", reading: backgroundJobs });
  if (backgroundJobs.kind === "unknown") undetermined.push("backgroundJobs");
  if (volatileStore.kind === "unreachable") raised.push({ row: "volatileStore", reading: volatileStore });
  if (volatileStore.kind === "unknown") undetermined.push("volatileStore");
  if (jobImport.kind === "overdue" || jobImport.kind === "noneRecorded") raised.push({ row: "jobImport", reading: jobImport });
  if (jobImport.kind === "unknown") undetermined.push("jobImport");
  return { kind: "read", raised, undetermined, importOverdueAfterSeconds };
}

/** True when the Backup or Tjänster card shows a state in the warning form. An unread source raises nothing. */
export function backupOrServicesRaised(
  backup: AwaitingObservation<BackupObservationData>,
  services: OverviewObservation<ServicesObservationDto>,
): boolean {
  const fromBackup = backupAttention(backup);
  const fromServices = servicesAttention(services);
  return (fromBackup.kind === "read" && fromBackup.raised.length > 0)
    || (fromServices.kind === "read" && fromServices.raised.length > 0);
}
