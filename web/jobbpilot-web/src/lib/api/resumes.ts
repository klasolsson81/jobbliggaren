import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  getResumesResultSchema,
  resumeDetailDtoSchema,
  type GetResumesResult,
  type ResumeDetailDto,
} from "@/lib/dto/resumes";
import {
  parseResponse,
  responseToResult,
  type ApiResult,
} from "@/lib/dto/_helpers";

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
): Promise<ApiResult<ResumeDetailDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes/${id}`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    return await responseToResult(
      res,
      resumeDetailDtoSchema,
      `GET /api/v1/resumes/${id}`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}
