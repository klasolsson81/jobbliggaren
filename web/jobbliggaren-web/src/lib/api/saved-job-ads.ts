import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  listSavedJobAdsResultSchema,
  type ListSavedJobAdsResult,
} from "@/lib/dto/saved-job-ads";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

/**
 * F6 P5 Punkt 2 Del A — hämta inloggad användares bokmärken.
 * `GET /api/v1/me/saved-job-ads`, auth-gated, JobSeeker-scopad.
 * ADR 0048 in-handler-join: jobAd-fältet är null när annonsen
 * soft-deletats (`JobAd.DeletedAt != null`) → UI renderar fallback.
 */
export async function getSavedJobAds(): Promise<
  ApiResult<ListSavedJobAdsResult>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/saved-job-ads");
    return await responseToResult(
      res,
      listSavedJobAdsResultSchema,
      "GET /api/v1/me/saved-job-ads"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Spara en JobAd som bokmärke. Idempotent (redan-sparad → 204).
 * `POST /api/v1/me/saved-job-ads/{jobAdId}`.
 */
export async function saveJobAd(jobAdId: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd). Malformat id kan ändå inte existera → 404.
  if (!isValidId(jobAdId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/me/saved-job-ads/${encodeURIComponent(jobAdId)}`,
      { method: "POST" }
    );
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 404) return { kind: "notFound" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Ta bort ett bokmärke. Idempotent (redan-borttaget → 204).
 * `DELETE /api/v1/me/saved-job-ads/{jobAdId}`.
 */
export async function unsaveJobAd(jobAdId: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(jobAdId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/me/saved-job-ads/${encodeURIComponent(jobAdId)}`,
      { method: "DELETE" }
    );
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Kollar om en specifik JobAd är sparad av inloggad användare.
 * Effektivare än att hämta hela listan när vi bara behöver state
 * för en enskild knapp (modal-footer). Implementeras som filter
 * över list-anropet — backend-yta för "is saved single" finns ej
 * (YAGNI: list-svaret är cap-fritt men typiskt &lt;20 rader i praktiken
 * tills användare bokmärker mycket; vid skala lyft separat endpoint).
 */
export async function isJobAdSaved(jobAdId: string): Promise<boolean> {
  const result = await getSavedJobAds();
  if (result.kind !== "ok") return false;
  return result.data.some((s) => s.jobAdId === jobAdId);
}
