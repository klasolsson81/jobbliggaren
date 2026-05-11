import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { isPagedResult } from "@/lib/types/paged";
import type {
  ApplicationDetailDto,
  ApplicationDto,
  GetApplicationsResult,
  PipelineGroupDto,
} from "@/lib/types/applications";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
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

  const payload: unknown = await res.json();
  return isPagedResult<ApplicationDto>(payload) ? payload : null;
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
