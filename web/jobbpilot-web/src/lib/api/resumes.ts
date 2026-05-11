import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  getResumesResultSchema,
  resumeDetailDtoSchema,
  type GetResumesResult,
  type ResumeDetailDto,
} from "@/lib/dto/resumes";
import { parseResponse } from "@/lib/dto/_helpers";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
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

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes?${params}`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    if (!res.ok) return null;
    return await parseResponse(
      res,
      getResumesResultSchema,
      "GET /api/v1/resumes"
    );
  } catch {
    return null;
  }
}

export async function getResumeById(
  id: string
): Promise<ResumeDetailDto | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes/${id}`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    if (res.status === 404) return null;
    if (!res.ok) return null;
    return await parseResponse(
      res,
      resumeDetailDtoSchema,
      `GET /api/v1/resumes/${id}`
    );
  } catch {
    return null;
  }
}
