import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  applicationDetailDtoSchema,
  getApplicationsResultSchema,
  pipelineResponseSchema,
  type ApplicationDetailDto,
  type GetApplicationsResult,
  type PipelineGroupDto,
} from "@/lib/dto/applications";
import { parseResponse } from "@/lib/dto/_helpers";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export async function getPipeline(): Promise<PipelineGroupDto[]> {
  const sessionId = await getSessionId();
  if (!sessionId) return [];

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/applications/pipeline`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    if (!res.ok) return [];
    return await parseResponse(
      res,
      pipelineResponseSchema,
      "GET /api/v1/applications/pipeline"
    );
  } catch {
    return [];
  }
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

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications?${params}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    if (!res.ok) return null;
    return await parseResponse(
      res,
      getApplicationsResultSchema,
      "GET /api/v1/applications"
    );
  } catch {
    return null;
  }
}

export async function getApplicationById(
  id: string
): Promise<ApplicationDetailDto | null> {
  const sessionId = await getSessionId();
  if (!sessionId) return null;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/applications/${id}`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    if (res.status === 404) return null;
    if (!res.ok) return null;
    return await parseResponse(
      res,
      applicationDetailDtoSchema,
      `GET /api/v1/applications/${id}`
    );
  } catch {
    return null;
  }
}
