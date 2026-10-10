import "server-only";

import { z } from "zod";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { auditLogPagedResultSchema, failedJobsResponseSchema } from "@/lib/dto/admin";
import {
  accountOverviewSchema,
  backupStatusResponseSchema,
  overviewAuditEventSchema,
  type AdminOverviewSnapshot,
  type BackupObservationData,
  type BackupStatusResponse,
} from "@/lib/dto/admin-overview";
import type { AwaitingObservation, OverviewObservation } from "@/lib/admin/overview";

const SOURCE_DEADLINE_MS = 10_000;
const SAMPLED_AT_HEADER = "X-Admin-Sampled-At";
const instant = z.iso.datetime({ offset: true });

type Observed<T> = { readonly data: T; readonly sampledAt: string };
export type OverviewRead =
  | { readonly kind: "ok"; readonly data: AdminOverviewSnapshot; readonly loadedAt: number }
  | { readonly kind: "unauthorized" }
  | { readonly kind: "forbidden" };

async function read<T>(
  sessionId: string,
  path: string,
  schema: z.ZodType<T>,
  signal?: AbortSignal,
): Promise<ApiResult<Observed<T>>> {
  const deadline = AbortSignal.timeout(SOURCE_DEADLINE_MS);
  const linked = signal ? AbortSignal.any([signal, deadline]) : deadline;
  try {
    const response = await authedFetch(sessionId, path, { signal: linked });
    const result = await responseToResult(response, schema, `GET ${path}`);
    if (result.kind !== "ok") return result;
    const sampledAt = instant.safeParse(response.headers.get(SAMPLED_AT_HEADER));
    return sampledAt.success
      ? { kind: "ok", data: { data: result.data, sampledAt: sampledAt.data } }
      : { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

function observed<T>(result: ApiResult<Observed<T>>, empty = false): OverviewObservation<T> {
  return result.kind === "ok"
    ? { kind: empty ? "empty" : "loaded", ...result.data, refreshFailed: false }
    : { kind: "failed" };
}

/**
 * The Backup card's source. The observation's time is the host's own (`observedAt`), never the time of this
 * read: the generic header stamps the response, and an old sample must stay old however late it is read.
 * A host that has not reported is `awaiting`, which is not a failure.
 */
function backupObservation(result: ApiResult<Observed<BackupStatusResponse>>): AwaitingObservation<BackupObservationData> {
  if (result.kind !== "ok") return { kind: "failed" };
  const status = result.data.data;
  if (status.status === "NotObserved") return { kind: "awaiting" };
  if (status.status === "Failed") return { kind: "failed" };

  const { lastSuccess, timer } = status;
  return {
    kind: "loaded",
    sampledAt: status.observedAt,
    refreshFailed: false,
    data: {
      stale: status.stale,
      lastSuccess: lastSuccess.state === "Recorded"
        ? { state: "recorded", completedAt: lastSuccess.completedAt, overdue: lastSuccess.overdue }
        : { state: lastSuccess.state === "Missing" ? "missing" : lastSuccess.state === "Unreadable" ? "unreadable" : "invalid" },
      timer: timer.state === "Scheduled"
        ? { state: "scheduled", nextRunAt: timer.nextRunAt }
        : { state: timer.state === "Inactive" ? "inactive" : timer.state === "NotInstalled" ? "notInstalled" : "unknown" },
    },
  };
}

/**
 * Resolves true as soon as one of the reads has succeeded, and false once every one has settled without a success.
 * `read` never rejects: a failure is a result.
 */
function anySucceeds(reads: ReadonlyArray<Promise<ApiResult<unknown>>>): Promise<boolean> {
  return new Promise((resolve) => {
    let pending = reads.length;
    for (const pendingRead of reads) {
      void pendingRead.then((result) => {
        if (result.kind === "ok") resolve(true);
        else if (--pending === 0) resolve(false);
      });
    }
  });
}

export async function loadAdminOverview(signal?: AbortSignal): Promise<OverviewRead> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  const accountsRead = read(sessionId, "/api/v1/admin/overview/accounts", accountOverviewSchema, signal);
  const auditRead = read(sessionId, "/api/v1/admin/audit-log?page=1&pageSize=5", auditLogPagedResultSchema, signal);
  const jobsRead = read(sessionId, "/api/v1/admin/jobs/failed", failedJobsResponseSchema, signal);
  // The host is asked only once an admin read has SUCCEEDED, so the authentication work a caller with a forged
  // or an ordinary session can start stays at the three reads above (#2064); it then runs beside the slower of
  // them instead of after them. If none succeeded there is nothing to prove the caller and nothing to wait for,
  // and the card reads as failed without a request.
  const backupRead = anySucceeds([accountsRead, auditRead, jobsRead]).then((authorized) => authorized
    ? read(sessionId, "/api/v1/admin/overview/backup", backupStatusResponseSchema, signal)
    : { kind: "error" as const });
  const [accounts, audit, jobs, backup] = await Promise.all([accountsRead, auditRead, jobsRead, backupRead]);
  for (const result of [accounts, audit, jobs, backup]) {
    if (result.kind === "unauthorized" || result.kind === "forbidden") return { kind: result.kind };
  }

  // Project before crossing the RSC/BFF boundary; the existing audit contract also carries PII.
  const safeAudit = audit.kind === "ok"
    ? z.array(overviewAuditEventSchema).max(5).safeParse(audit.data.data.items.map((event) => ({
        id: event.id,
        occurredAt: event.occurredAt,
        eventType: event.eventType,
        aggregateType: event.aggregateType,
        aggregateId: event.aggregateId,
      })))
    : null;
  const auditObservation = audit.kind === "ok" && safeAudit?.success
    ? observed({ kind: "ok", data: { data: safeAudit.data, sampledAt: audit.data.sampledAt } }, safeAudit.data.length === 0)
    : { kind: "failed" as const };
  const jobObservation = jobs.kind === "ok"
    ? observed({ kind: "ok", data: { data: { totalCount: jobs.data.data.totalCount }, sampledAt: jobs.data.sampledAt } })
    : { kind: "failed" as const };

  return {
    kind: "ok",
    data: { accounts: observed(accounts), audit: auditObservation, jobs: jobObservation, backup: backupObservation(backup) },
    loadedAt: Date.now(),
  };
}