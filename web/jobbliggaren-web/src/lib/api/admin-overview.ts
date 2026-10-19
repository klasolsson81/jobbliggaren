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
import { hostObservationSchema, type HostObservationDto } from "@/lib/dto/admin-host";
import { servicesObservationSchema, type ServicesObservationDto } from "@/lib/dto/admin-services";
import { emailAllowanceSchema, emailDeliverySchema, type EmailAllowanceDto, type EmailDeliveryDto } from "@/lib/dto/admin-email";
import { ATTENTION_EMAIL_DELIVERY_PERIOD, withEmailPeriod } from "@/lib/admin/email-delivery";
import type { AwaitingObservation, OverviewObservation } from "@/lib/admin/overview";

/** Every admin source read gets this long, its own and the e-post page's alike (ADR 0150 Amendment 2026-10-08). */
export const SOURCE_DEADLINE_MS = 10_000;
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

const LAST_SUCCESS_STATES = { Missing: "missing", Unreadable: "unreadable", Invalid: "invalid" } as const;
const TIMER_STATES = { Inactive: "inactive", NotInstalled: "notInstalled", Unknown: "unknown" } as const;

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
      lastSuccess: lastSuccess.state === "Recorded"
        ? { state: "recorded", completedAt: lastSuccess.completedAt, overdue: lastSuccess.overdue }
        : { state: LAST_SUCCESS_STATES[lastSuccess.state] },
      timer: timer.state === "Scheduled"
        ? { state: "scheduled", nextRunAt: timer.nextRunAt }
        : { state: TIMER_STATES[timer.state] },
    },
  };
}

/**
 * The Server card's source. Like Backup's, its time is the host's own: the newest reading's `sampledAt`, never the
 * time of this read (the generic header stamps the response, and an old sample must stay old however late it is
 * read). When no reading has a value there is no sample to date and nothing to mistake for fresh, so the API
 * read instant stands in. What crosses to the browser is the schema's output: states, instants and numbers.
 */
function hostObservation(result: ApiResult<Observed<HostObservationDto>>): OverviewObservation<HostObservationDto> {
  if (result.kind !== "ok") return { kind: "failed" };
  const host = result.data.data;
  const newest = newestSample([host.cpu, host.memory, host.disk]);
  return { kind: "loaded", data: host, sampledAt: newest ?? result.data.sampledAt, refreshFailed: false };
}

/**
 * The Tjänster card's source, dated as the host's is: by the newest row's own `sampledAt`, and by the API read
 * instant only when every row is unknown and there is no sample to age. What crosses to the browser is the schema's
 * output: states, instants, an error count and three limits.
 */
function servicesObservation(result: ApiResult<Observed<ServicesObservationDto>>): OverviewObservation<ServicesObservationDto> {
  if (result.kind !== "ok") return { kind: "failed" };
  const services = result.data.data;
  const newest = newestSample([services.backgroundJobs, services.volatileStore, services.jobImport]);
  return { kind: "loaded", data: services, sampledAt: newest ?? result.data.sampledAt, refreshFailed: false };
}

/**
 * The e-post card's source (#1981 PR 1). An observed count is dated by when the provider answered (`observedAt`, which
 * a remembered answer keeps), never by the time of this read; a deployment without a provider, or a read the provider
 * did not answer, has no answer to date, so the API read instant stands in. What crosses to the browser is the
 * schema's output: the DTO as the API sent it.
 */
function emailObservation(result: ApiResult<Observed<EmailAllowanceDto>>): OverviewObservation<EmailAllowanceDto> {
  if (result.kind !== "ok") return { kind: "failed" };
  const allowance = result.data.data;
  const sampledAt = allowance.usage.status === "Observed" ? allowance.usage.observedAt : result.data.sampledAt;
  return { kind: "loaded", data: allowance, sampledAt, refreshFailed: false };
}

/**
 * The attention card's failed sends (#1981 PR 2): the provider's counts for the last 24 hours, and nothing else; the
 * overview never reads the failures list, so no recipient reaches it. Dated as the e-post card is: an observed count
 * by when we asked the provider, any other state by the API read instant. An answer for another period fails.
 */
function deliveryObservation(result: ApiResult<Observed<EmailDeliveryDto>>): OverviewObservation<EmailDeliveryDto> {
  if (result.kind !== "ok" || result.data.data.period !== ATTENTION_EMAIL_DELIVERY_PERIOD) return { kind: "failed" };
  const delivery = result.data.data;
  const sampledAt = delivery.status === "Observed" ? delivery.observedAt : result.data.sampledAt;
  return { kind: "loaded", data: delivery, sampledAt, refreshFailed: false };
}

/** The newest of the parts' own sample times; null when none of them was sampled. */
function newestSample(parts: ReadonlyArray<{ readonly sampledAt: string | null }>): string | null {
  const times = parts.flatMap((part) => (part.sampledAt === null ? [] : [part.sampledAt]));
  return times.length === 0 ? null : times.reduce((a, b) => (Date.parse(a) >= Date.parse(b) ? a : b));
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
  // A source beyond the first three is asked only once an admin read has SUCCEEDED, so the authentication work a
  // caller with a forged or an ordinary session can start stays at the three reads above (#2064); it then runs
  // beside the slower of them instead of after them. If none succeeded there is nothing to prove the caller and
  // nothing to wait for, and the source reads as failed without a request. Every later source goes through `gated`.
  const authorized = anySucceeds([accountsRead, auditRead, jobsRead]);
  const gated = <T>(path: string, schema: z.ZodType<T>): Promise<ApiResult<Observed<T>>> =>
    authorized.then((proven) => (proven ? read(sessionId, path, schema, signal) : { kind: "error" as const }));
  const backupRead = gated("/api/v1/admin/overview/backup", backupStatusResponseSchema);
  const hostRead = gated("/api/v1/admin/overview/host", hostObservationSchema);
  const servicesRead = gated("/api/v1/admin/overview/services", servicesObservationSchema);
  const emailRead = gated("/api/v1/admin/email/allowance", emailAllowanceSchema);
  const deliveryRead = gated(withEmailPeriod("/api/v1/admin/email/delivery", ATTENTION_EMAIL_DELIVERY_PERIOD), emailDeliverySchema);
  const [accounts, audit, jobs, backup, host, services, email, delivery] =
    await Promise.all([accountsRead, auditRead, jobsRead, backupRead, hostRead, servicesRead, emailRead, deliveryRead]);
  for (const result of [accounts, audit, jobs, backup, host, services, email, delivery]) {
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
    data: {
      accounts: observed(accounts), audit: auditObservation, jobs: jobObservation, backup: backupObservation(backup),
      host: hostObservation(host), services: servicesObservation(services), email: emailObservation(email),
      delivery: deliveryObservation(delivery),
    },
    loadedAt: Date.now(),
  };
}