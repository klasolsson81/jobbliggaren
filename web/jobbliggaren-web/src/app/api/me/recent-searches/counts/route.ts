import { NextResponse } from "next/server";
import { getRecentSearches } from "@/lib/api/recent-searches";

/**
 * B (CTO-beslut 2026-06-13) — lat-count-proxy för recent-search-ytorna.
 * `useRecentSearchCounts`-klienten kan inte anropa den `server-only`
 * `getRecentSearches`-fetchern direkt (session-cookie + BACKEND_URL är
 * serverkontext). Speglar `/api/jobb/facet-counts/route.ts`-mönstret:
 * delegera till server-fetchern med `includeCount=true` (återanvänder den
 * befintliga slow N+1-COUNT-grenen off-critical-path), projicera till en smal
 * `{id, currentCount, newCount}`-lista, mappa `ApiResult` → HTTP-status.
 *
 * Degraderings-kontrakt (= facet-counts): fel-utfall → NON-2xx så hookens
 * !res.ok-gren ger `null` → INGA tal renderas. ALDRIG 200 + tom/falsk data —
 * "(0)" vid fel vore desinformation (counten är en hint, aldrig en
 * förutsättning). Ingen användarinput vidarebefordras (sökningarna är
 * session-scopade) → ingen SSRF/path-injektion-yta.
 */
export async function GET() {
  const result = await getRecentSearches(true);

  switch (result.kind) {
    case "ok":
      return NextResponse.json(
        result.data.map((r) => ({
          id: r.id,
          currentCount: r.currentCount,
          newCount: r.newCount,
        })),
      );
    case "unauthorized":
      return NextResponse.json({}, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        {},
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        },
      );
    // forbidden/notFound/error → 502: hooken nollar counts (null), inga tal
    // renderas. ALDRIG 200 + tomt/falskt (desinformation vid backend-fel).
    default:
      return NextResponse.json({}, { status: 502 });
  }
}
