import "server-only";

import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  auditLogPagedResultSchema,
  failedJobsResponseSchema,
  recurringJobsResponseSchema,
  type AuditLogFilter,
  type AuditLogPagedResult,
  type FailedJobsResponse,
  type RecurringJobStatusDto,
} from "@/lib/dto/admin";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

export async function getAuditLog(
  filter: AuditLogFilter = {}
): Promise<ApiResult<AuditLogPagedResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams();
  if (filter.page !== undefined) params.set("page", String(filter.page));
  if (filter.pageSize !== undefined)
    params.set("pageSize", String(filter.pageSize));
  if (filter.from) params.set("from", filter.from);
  if (filter.to) params.set("to", filter.to);
  if (filter.userId) params.set("userId", filter.userId);
  if (filter.eventType) params.set("eventType", filter.eventType);
  if (filter.aggregateType)
    params.set("aggregateType", filter.aggregateType);

  const query = params.toString();
  const url = `${env.BACKEND_URL}/api/v1/admin/audit-log${query ? `?${query}` : ""}`;

  try {
    const res = await fetch(url, {
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
    });
    return await responseToResult(
      res,
      auditLogPagedResultSchema,
      "GET /api/v1/admin/audit-log"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Recurring (scheduled) Hangfire jobs (TD-83 / issue #204, PR1 read-side).
 * Mirrors `getAuditLog`: session → Bearer, no-store, ACL via the backend
 * admin group (`RequireAuthorization(Admin)`). The endpoint returns a bare
 * array (server-sorted by id); on any non-2xx / network / shape error the
 * result is `{ kind: "error" }` and backend error bodies are never echoed.
 */
export async function getRecurringJobs(): Promise<
  ApiResult<RecurringJobStatusDto[]>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const url = `${env.BACKEND_URL}/api/v1/admin/jobs/recurring`;

  try {
    const res = await fetch(url, {
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
    });
    return await responseToResult(
      res,
      recurringJobsResponseSchema,
      "GET /api/v1/admin/jobs/recurring"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Failed Hangfire jobs (TD-83 / issue #204, PR1 read-side). Returns an
 * envelope with `totalCount` / `returned` (capped at the 50 most recent) +
 * PII-free `errorCategory` per item. Same Bearer/no-store contract as above;
 * backend error bodies are never echoed to the UI.
 */
export async function getFailedJobs(): Promise<ApiResult<FailedJobsResponse>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const url = `${env.BACKEND_URL}/api/v1/admin/jobs/failed`;

  try {
    const res = await fetch(url, {
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
    });
    return await responseToResult(
      res,
      failedJobsResponseSchema,
      "GET /api/v1/admin/jobs/failed"
    );
  } catch {
    return { kind: "error" };
  }
}
