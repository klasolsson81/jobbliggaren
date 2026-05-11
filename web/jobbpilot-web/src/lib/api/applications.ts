import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import type {
  ApplicationDetailDto,
  GetApplicationsResult,
  PipelineGroupDto,
} from "@/lib/types/applications";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

function isPagedApplications(value: unknown): value is GetApplicationsResult {
  if (value === null || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  return (
    Array.isArray(v.items) &&
    typeof v.totalCount === "number" &&
    typeof v.page === "number" &&
    typeof v.pageSize === "number"
  );
}

export async function getPipeline(): Promise<PipelineGroupDto[]> {
  const sessionId = await getSessionId();
  if (!sessionId) return [];

  const res = await fetch(`${env.BACKEND_URL}/api/v1/applications/pipeline`, {
    headers: authHeaders(sessionId),
    cache: "no-store",
  });
  if (!res.ok) return [];
  return res.json();
}

export async function getApplications(
  page = 1,
  pageSize = 20,
  status?: string
): Promise<GetApplicationsResult | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });
  if (status) params.set("status", status);

  const res = await fetch(
    `${env.BACKEND_URL}/api/v1/applications?${params}`,
    { headers: authHeaders(sessionId), cache: "no-store" }
  );
  if (!res.ok) return null;

  // Lättviktig runtime-validering — `res.json()` är effektivt unknown och
  // CLAUDE.md §4.1 förbjuder any. Skydd mot kontrakts-skew (TD-55-lärdom).
  const payload: unknown = await res.json();
  return isPagedApplications(payload) ? payload : null;
}

export async function getApplicationById(
  id: string
): Promise<ApplicationDetailDto | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  const res = await fetch(`${env.BACKEND_URL}/api/v1/applications/${id}`, {
    headers: authHeaders(sessionId),
    cache: "no-store",
  });
  if (res.status === 404) return null;
  if (!res.ok) return null;
  return res.json();
}
