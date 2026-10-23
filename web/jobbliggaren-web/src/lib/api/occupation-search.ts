import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  occupationSearchResultSchema,
  type OccupationSearchResult,
} from "@/lib/dto/occupation-search";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * #2198 — `GET /api/v1/job-ads/taxonomy/occupations/search?q=`: the occupation picker's search.
 * Its own module, never beside the tree's `revalidate` fetcher in `taxonomy.ts`: the answer varies
 * per typed word, and a cached fetch would store each word as a cache entry in the web container
 * (security-auditor Major 2). `authedFetch` forces `no-store`; the backend sends
 * `private, no-store`. Called only from the BFF route the debounced client read hits.
 */
export async function searchOccupations(
  q: string,
  signal?: AbortSignal,
): Promise<ApiResult<OccupationSearchResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const params = new URLSearchParams({ q });
    const res = await authedFetch(
      sessionId,
      `/api/v1/job-ads/taxonomy/occupations/search?${params}`,
      { signal },
    );
    return await responseToResult(
      res,
      occupationSearchResultSchema,
      "GET /api/v1/job-ads/taxonomy/occupations/search",
    );
  } catch {
    return { kind: "error" };
  }
}
