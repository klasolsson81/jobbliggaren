import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  listRecentSearchesResultSchema,
  type ListRecentSearchesResult,
} from "@/lib/dto/recent-searches";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

/**
 * Klient-side timeout för list-anropet. ADR 0060 Beslut 4 accepterar N+1
 * COUNT-projektion under cap=20. Vid full-text-q-criteria mot stor JobAds-
 * korpus utan trigram-index kan COUNTs ackumulera till >30s — vi degraderar
 * /jobb och /sokningar civilt istället för att binda Vercel-funktionen
 * tills den 504:ar (UX > väntan på data). Backend N+1-optimering = separat
 * BE-prompt; FE-timeout = defense-in-depth.
 */
const LIST_TIMEOUT_MS = 8_000;

/**
 * ADR 0060 — hämtar användarens auto-fångade RecentJobSearches.
 * Konsumerar `GET /api/v1/me/recent-searches` (auth-gated, JobSeeker-scopad,
 * cap=20 rader).
 */
export async function getRecentSearches(): Promise<
  ApiResult<ListRecentSearchesResult>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/me/recent-searches`,
      {
        headers: authHeaders(sessionId),
        cache: "no-store",
        signal: AbortSignal.timeout(LIST_TIMEOUT_MS),
      }
    );
    return await responseToResult(
      res,
      listRecentSearchesResultSchema,
      "GET /api/v1/me/recent-searches"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Tar bort en RecentJobSearch (hard-delete på server). 404 vid okänt id
 * ELLER cross-tenant (ADR 0031 — oskiljbart i öppet svar).
 */
export async function deleteRecentSearch(
  id: string
): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/me/recent-searches/${id}`,
      { method: "DELETE", headers: authHeaders(sessionId), cache: "no-store" }
    );
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 404) return { kind: "notFound" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}
