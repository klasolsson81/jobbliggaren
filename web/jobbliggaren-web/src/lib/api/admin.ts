import "server-only";

import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  auditLogPagedResultSchema,
  type AuditLogFilter,
  type AuditLogPagedResult,
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
