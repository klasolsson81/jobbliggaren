import "server-only";

import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import type { AuditLogFilter, AuditLogPagedResult } from "@/lib/types/admin";

export type AuditLogResponse =
  | { kind: "ok"; data: AuditLogPagedResult }
  | { kind: "forbidden" }
  | { kind: "unauthorized" }
  | { kind: "error" };

export async function getAuditLog(
  filter: AuditLogFilter = {}
): Promise<AuditLogResponse> {
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

    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    if (!res.ok) return { kind: "error" };

    const data = (await res.json()) as AuditLogPagedResult;
    return { kind: "ok", data };
  } catch {
    return { kind: "error" };
  }
}
