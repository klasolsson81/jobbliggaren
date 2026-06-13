import { NextResponse, type NextRequest } from "next/server";
import { getFacetCounts } from "@/lib/api/job-ads";
import { facetDimensionSchema } from "@/lib/dto/job-ads";

/**
 * ADR 0067 Beslut 4 (Fas E2c) — facet-counts-proxy. Popover-klienten kan
 * inte anropa den `server-only` `getFacetCounts`-fetchern direkt
 * (session-cookie + BACKEND_URL är serverkontext). Speglar
 * `/api/jobb/suggest/route.ts`-mönstret: validera dimension billigt här
 * (allowlist — okänd dimension träffar aldrig backend), delegera till
 * server-fetchern, mappa `ApiResult` → HTTP-status.
 *
 * Degraderings-kontrakt (E2c-architect §5, skärpt av code-reviewer Major 1):
 * fel-utfall → NON-2xx så hookens !res.ok-gren ger counts=null → INGA tal
 * renderas. Ett 200 + tomt objekt vore desinformation — "(0)" på varje rad
 * påstår noll annonser när backend är nere; tom dict är tvetydig (legitim
 * tom korpus går inte att skilja från fel). Counts är en hint, aldrig en
 * förutsättning — popovern förblir fullt användbar utan dem.
 */
export async function GET(request: NextRequest) {
  const params = request.nextUrl.searchParams;

  const dimension = facetDimensionSchema.safeParse(params.get("dimension"));
  if (!dimension.success) {
    return NextResponse.json(
      { error: "Ogiltig dimension." },
      { status: 400 }
    );
  }

  const result = await getFacetCounts(dimension.data, {
    occupationGroup: params.getAll("occupationGroup"),
    municipality: params.getAll("municipality"),
    region: params.getAll("region"),
    employmentType: params.getAll("employmentType"),
    worktimeExtent: params.getAll("worktimeExtent"),
    q: params.get("q") ?? undefined,
  });

  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data);
    case "unauthorized":
      return NextResponse.json({}, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        {},
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        }
      );
    // forbidden/notFound/error → 502: hooken nollar counts (null), inga
    // tal renderas. ALDRIG 200 + tomt objekt (code-reviewer Major 1 —
    // "(0)" vid backend-fel vore aktiv desinformation).
    default:
      return NextResponse.json({}, { status: 502 });
  }
}
