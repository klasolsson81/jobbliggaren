import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  taxonomyTreeSchema,
  taxonomyLabelsResultSchema,
  type TaxonomyTree,
  type TaxonomyLabelsResult,
} from "@/lib/dto/taxonomy";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * ADR 0043 — Taxonomi-ACL. Server-side fetchers mot
 * `GET /api/v1/job-ads/taxonomy` + `/taxonomy/labels` (auth-gated,
 * Bearer-session). Konsumeras av Server Components / route-handlers — INTE
 * useEffect (CLAUDE.md §4.3/§5.2). Mönstret speglar `lib/api/job-ads.ts`
 * (`ApiResult<T>`, Zod-validering vid ACL-gränsen, 401/429-mappning).
 *
 * Träd-svaret är statisk referensdata: backend skickar ETag +
 * `Cache-Control: private, max-age=3600`. Vi använder Next.js fetch-cache
 * (`next: { revalidate }`) så samma render-pass / närliggande requests inte
 * re-hämtar ~300 KB. Snapshotten ändras bara vid deploy (ADR 0043 Beslut
 * B) → en timmes revalidate är vältajmad och konservativ.
 */

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

// Speglar backend Cache-Control max-age=3600 (statisk snapshot, ADR 0043
// Beslut B — ändras bara vid deploy).
const TAXONOMY_TREE_REVALIDATE_SECONDS = 3600;

/**
 * Hämtar hela picker-trädet (Län + Yrkesområde→Yrke). Konsumeras av
 * `/jobb`-sidan (Server Component) och passas ned till klient-väljaren.
 */
export async function getTaxonomyTree(): Promise<ApiResult<TaxonomyTree>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/taxonomy`,
      {
        headers: authHeaders(sessionId),
        next: { revalidate: TAXONOMY_TREE_REVALIDATE_SECONDS },
      }
    );
    return await responseToResult(
      res,
      taxonomyTreeSchema,
      "GET /api/v1/job-ads/taxonomy"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Reverse-lookup: concept-id-lista → visningsnamn. Används för att rendera
 * redan-valda/sparade concept-id som namn (chips + sparade sökningar).
 * Okänt id → backend `"Okänd kod (<id>)"` (graceful, ADR 0043 Beslut B).
 *
 * Tom id-lista → tom lista utan backend-anrop (ingen DoS-yta, ingen
 * onödig rundtur). Cap = backend `SearchCriteria.MaxConceptIds * 4` (=1600)
 * i `ResolveTaxonomyLabelsQueryValidator` (sista barriär; ×4 sedan ADR
 * 0043-notatet 2026-06-09 — kommentaren här släpade på ×2).
 *
 * Cache: backend skickar `private, no-store` (varierar per ids, auth) →
 * `authedFetch` tvingar `cache: "no-store"` så per-användar-svaret ej cachas.
 */
export async function resolveTaxonomyLabels(
  ids: ReadonlyArray<string>
): Promise<ApiResult<TaxonomyLabelsResult>> {
  if (ids.length === 0) return { kind: "ok", data: [] };

  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams();
  // Upprepad nyckel per element (`?ids=a&ids=b`) — backend binder string[].
  for (const id of ids) params.append("ids", id);

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/job-ads/taxonomy/labels?${params.toString()}`
    );
    return await responseToResult(
      res,
      taxonomyLabelsResultSchema,
      "GET /api/v1/job-ads/taxonomy/labels"
    );
  } catch {
    return { kind: "error" };
  }
}
