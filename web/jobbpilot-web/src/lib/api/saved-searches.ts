import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  listSavedSearchesResultSchema,
  savedSearchDtoSchema,
  type ListSavedSearchesResult,
  type SavedSearchDto,
} from "@/lib/dto/saved-searches";
import {
  listJobAdsResultSchema,
  type ListJobAdsResult,
} from "@/lib/dto/job-ads";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * Hämtar användarens sparade sökningar. Konsumerar
 * `GET /api/v1/saved-searches` (auth-gated, JobSeeker-scopad).
 */
export async function getSavedSearches(): Promise<
  ApiResult<ListSavedSearchesResult>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/saved-searches`, {
      headers: authHeaders(sessionId),
      cache: "no-store",
    });
    return await responseToResult(
      res,
      listSavedSearchesResultSchema,
      "GET /api/v1/saved-searches"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Hämtar en sparad sökning. `GET /api/v1/saved-searches/{id}`.
 * 404 (okänt id ELLER annan användares — oskiljbart per ADR 0031).
 */
export async function getSavedSearch(
  id: string
): Promise<ApiResult<SavedSearchDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/saved-searches/${id}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      savedSearchDtoSchema,
      "GET /api/v1/saved-searches/{id}",
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Kör en sparad sökning och returnerar matchande jobbannonser (paginerat).
 * Konsumerar `POST /api/v1/saved-searches/{id}/run` (query = ADR 0039
 * Beslut 2, ingen skriv-sidoeffekt; ListReadPolicy rate-limit → 429
 * mappas till `rateLimited` per ADR 0030). Återanvänder JobAd-listans
 * DTO-schema (samma JobAdSearch-komposition i backend).
 */
export async function runSavedSearch(
  id: string,
  page: number,
  pageSize: number
): Promise<ApiResult<ListJobAdsResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/saved-searches/${id}/run?${params.toString()}`,
      { method: "POST", headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      listJobAdsResultSchema,
      "POST /api/v1/saved-searches/{id}/run",
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}
