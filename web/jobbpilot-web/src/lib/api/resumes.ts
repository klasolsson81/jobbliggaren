import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import type {
  GetResumesResult,
  ResumeDetailDto,
} from "@/lib/types/resumes";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

function isPagedResumes(value: unknown): value is GetResumesResult {
  if (value === null || typeof value !== "object") return false;
  const v = value as Record<string, unknown>;
  return (
    Array.isArray(v.items) &&
    typeof v.totalCount === "number" &&
    typeof v.page === "number" &&
    typeof v.pageSize === "number"
  );
}

export async function getResumes(
  page = 1,
  pageSize = 20
): Promise<GetResumesResult | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes?${params}`, {
    headers: authHeaders(sessionId),
    cache: "no-store",
  });
  if (!res.ok) return null;

  // Lättviktig runtime-validering — `res.json()` är effektivt unknown och
  // CLAUDE.md §4.1 förbjuder any. Skydd mot kontrakts-skew mellan backend
  // och frontend (TD-55-lärdom).
  const payload: unknown = await res.json();
  return isPagedResumes(payload) ? payload : null;
}

export async function getResumeById(
  id: string
): Promise<ResumeDetailDto | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes/${id}`, {
    headers: authHeaders(sessionId),
    cache: "no-store",
  });
  if (res.status === 404) return null;
  if (!res.ok) return null;
  return res.json();
}
