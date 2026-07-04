import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  listRecentSearchesResultSchema,
  type ListRecentSearchesResult,
} from "@/lib/dto/recent-searches";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

/**
 * Klient-side timeout för list-anropet. ADR 0060 Beslut 4 accepterar N+1
 * COUNT-projektion under cap=20. Default-timeout är pragmatic band tills
 * TD-94 löser rotorsaken (ListJobAds COUNT-perf p50 1.2s/max 6.7s).
 *
 * <p>Konsumenter som anropar med <code>includeCount=false</code> behöver ingen
 * slow COUNT-loop → kortare default-timeout. <b>2026-06-13: INGEN konsument
 * anropar med <code>true</code> idag</b> (/oversikt, /sokningar, /jobb hero-chip
 * hämtar alla med <code>false</code>). <code>LIST_TIMEOUT_WITH_COUNT_MS</code>
 * är reserverad för den framtida lat-count-vägen (B) — den långa timeouten
 * skulle annars åter-exponera worst-case cap=20×1.5s.</p>
 *
 * <p>F6 P5 P4 svans-PR5 (2026-05-24, Klas-feedback /sokningar + /jobb-hero-chip
 * "Inga senaste sökningar än"): tidigare statiskt 8s blockerade /sokningar +
 * hero-chip när Klas hade flera RecentSearches.</p>
 */
const LIST_TIMEOUT_COMPACT_MS = 8_000;
// Reserverad för framtida lat-count-väg (B) — ingen konsument når true-grenen idag.
const LIST_TIMEOUT_WITH_COUNT_MS = 25_000;

/**
 * ADR 0060 — hämtar användarens auto-fångade RecentJobSearches.
 * Konsumerar `GET /api/v1/me/recent-searches` (auth-gated, JobSeeker-scopad,
 * cap=20 rader).
 *
 * <p><b>includeCount</b> (default <code>false</code> per svans-PR6 2026-05-24):
 * styr om backend beräknar per-row `currentCount`/`newCount` (slow N+1 över
 * JobAds-COUNT, TD-94 rot). Default flyttat från <code>true</code> till
 * <code>false</code> eftersom CloudWatch (2026-05-24) visar p50 15-22s + max
 * &gt;25s timeout för cap=3+ rader med low-selectivity-Q ("AI", "lärare").
 * Min PR5-fix 25s räcker inte; /sokningar + hero-chip 500-failade i Klas-
 * session, cascade-fel drog även hero-chip till tom-state.</p>
 *
 * <p><b>Förlust:</b> hero-chip + /sokningar visar nu bara namn utan per-sökning-
 * träffräknare. Den (tidigare felaktigt renderade "(0)") siffran togs bort i UI:t
 * 2026-06-13 (CTO-beslut: hellre ingen siffra än falsk "(0)") och återinförs via
 * lat klient-hämtning (B, useFacetCounts-mönstret) — oberoende av TD-94:s rotfix.
 * Civic-utility acceptabelt: namn + klickbar rad ger fortfarande funktionell
 * "kör om sökning"-yta.</p>
 *
 * <p>Konsumenter som explicit behöver counts kan sätta <code>true</code> — de
 * tar då 15-25s slow load + risk för timeout. Inte rekommenderat för någon
 * konsument idag.</p>
 */
export async function getRecentSearches(
  includeCount: boolean = false,
): Promise<ApiResult<ListRecentSearchesResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const path = `/api/v1/me/recent-searches?includeCount=${includeCount}`;
  const timeoutMs = includeCount
    ? LIST_TIMEOUT_WITH_COUNT_MS
    : LIST_TIMEOUT_COMPACT_MS;

  try {
    const res = await authedFetch(sessionId, path, {
      signal: AbortSignal.timeout(timeoutMs),
    });
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
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd). Malformat id kan ändå inte existera → 404.
  if (!isValidId(id)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/me/recent-searches/${encodeURIComponent(id)}`,
      { method: "DELETE" }
    );
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 404) return { kind: "notFound" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}
