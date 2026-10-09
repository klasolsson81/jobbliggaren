import "server-only";

import { z } from "zod";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { auditLogPagedResultSchema, failedJobsResponseSchema } from "@/lib/dto/admin";
import {
  accountOverviewSchema,
  overviewAuditEventSchema,
  type AdminOverviewSnapshot,
} from "@/lib/dto/admin-overview";
import type { OverviewObservation } from "@/lib/admin/overview";

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

export async function loadAdminOverview(signal?: AbortSignal): Promise<OverviewRead> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  const [accounts, audit, jobs] = await Promise.all([
    read(sessionId, "/api/v1/admin/overview/accounts", accountOverviewSchema, signal),
    read(sessionId, "/api/v1/admin/audit-log?page=1&pageSize=5", auditLogPagedResultSchema, signal),
    read(sessionId, "/api/v1/admin/jobs/failed", failedJobsResponseSchema, signal),
  ]);
  for (const result of [accounts, audit, jobs]) {
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
  return { kind: "ok", data: { accounts: observed(accounts), audit: auditObservation, jobs: jobObservation }, loadedAt: Date.now() };
}