import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import type { ResumeDetailDto, ResumeListItemDto } from "@/lib/types/resumes";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export async function getResumes(
  page = 1,
  pageSize = 20
): Promise<ResumeListItemDto[]> {
  const sessionId = await getSessionId();
  if (!sessionId) return [];

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes?${params}`, {
    headers: authHeaders(sessionId),
    cache: "no-store",
  });
  if (!res.ok) return [];
  return res.json();
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
