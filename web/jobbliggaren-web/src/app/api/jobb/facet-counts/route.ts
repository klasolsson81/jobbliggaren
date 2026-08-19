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
 *
 * URL dialect, written down because it is NOT the page's (2026-08-01). `/jobb`'s
 * PAGE url now writes ONE param per axis with the conceptIds joined
 * (`?municipality=a.b`). This route reads with `getAll` and does NOT split — it
 * speaks the REPEATED form, and that is correct, because its only caller is
 * `lib/hooks/use-facet-counts.ts`, which builds fresh `URLSearchParams` with
 * `append` from already-parsed arrays. The loop is closed, verified mechanically
 * (code-reviewer + security-auditor, #1144).
 *
 * The obvious future refactor — forward the page's `searchParams` here — would
 * therefore send `a.b` as ONE facet code. It fails closed but SILENTLY: the
 * backend's conceptId grammar rejects the dot, the response is 400, this route
 * maps that to 502, and the hook nulls the counts with no trace in the UI. Split
 * here FIRST if that refactor is ever done.
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
    // #551 punkt 4 — endast "true" räknas som på (klienten skriver bara den
    // formen); allt annat är av. Drop-unknown, paritet med dimensionslistorna.
    remote: params.get("remote") === "true",
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
