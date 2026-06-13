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
import {
  responseToResult,
  type ApiResult,
} from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export async function getPipeline(): Promise<ApiResult<PipelineGroupDto[]>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/applications/pipeline`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    return await responseToResult(
      res,
      pipelineResponseSchema,
      "GET /api/v1/applications/pipeline"
    );
  } catch {
    return { kind: "error" };
  }
}

export async function getApplications(
  page = 1,
  pageSize = 20,
  status?: string
): Promise<ApiResult<GetApplicationsResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

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
    return await responseToResult(
      res,
      getApplicationsResultSchema,
      "GET /api/v1/applications"
    );
  } catch {
    return { kind: "error" };
  }
}

export async function getApplicationById(
  id: string
): Promise<ApiResult<ApplicationDetailDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd). Malformat id kan ändå inte existera → 404.
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/${encodeURIComponent(id)}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      applicationDetailDtoSchema,
      `GET /api/v1/applications/${id}`,
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}
