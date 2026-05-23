import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { jobAdSortBySchema, type JobAdSortBy } from "@/lib/dto/job-ads";
import { Search } from "lucide-react";
import { JobbHeroFilters } from "@/components/job-ads/jobb-hero-filters";
import { JobbResults } from "@/components/job-ads/jobb-results";
import { JobAdListSkeleton } from "@/components/job-ads/job-ad-list-skeleton";
import { MarkJobbVisited } from "@/components/job-ads/mark-jobb-visited";
import { RecentSearchesHeroChip } from "@/components/recent-searches/recent-searches-hero-chip";
import { SavedJobAdsHeroChip } from "@/components/saved-job-ads/saved-job-ads-hero-chip";

// searchParams-värden kan vara string | string[] | undefined. ssyk/region
// är upprepade query-params (ADR 0042 Beslut B) → string[] vid flera värden.
type JobbSearchParams = {
  page?: string;
  pageSize?: string;
  sortBy?: string;
  ssyk?: string | string[];
  region?: string | string[];
  q?: string;
};

interface PageProps {
  // Next.js 16 App Router: searchParams är Promise (verifierat mot
  // node_modules/next/dist/docs/.../page#searchparams-optional).
  searchParams: Promise<JobbSearchParams>;
}

const DEFAULT_PAGE_SIZE = 20;

// ADR 0042 Beslut E — "ny sedan"-fönster. Designval: ett fast, rullande
// 7-dygnsfönster (server-styrt, ingen extra UI-kontroll). Civic-utility:
// håll enkelt — "Ny" betyder "publicerad senaste 7 dygnen", inget
// användaren behöver konfigurera. (Användarstyrt fönster är en medveten
// icke-leverans här; kan adderas senare utan kontraktsändring.)
const NEW_WINDOW_DAYS = 7;

// Beräknas i en helper (ej direkt i Server Component-kroppen) — React
// Compiler-lintregeln flaggar `Date.now()` i render-kroppen som oren.
// Server Component körs per request så fönstret är färskt vid varje load.
function newWindowSince(): string {
  return new Date(
    Date.now() - NEW_WINDOW_DAYS * 24 * 60 * 60 * 1000
  ).toISOString();
}

export default async function JobbPage({ searchParams }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const params = await searchParams;
  const page = parsePositiveInt(params.page, 1);
  const pageSize = Math.min(
    parsePositiveInt(params.pageSize, DEFAULT_PAGE_SIZE),
    100
  );
  const sortBy = parseSortBy(params.sortBy);
  const ssyk = toStringList(params.ssyk);
  const region = toStringList(params.region);
  const q = emptyToUndefined(params.q);

  const since = newWindowSince();

  // ADR 0043 — picker-träd hämtas server-side för hero-filter-popovern
  // (CLAUDE.md §4.3/§5.2 — ingen useEffect-fetch). Träd + senaste
  // sökningar är HERO-beroenden: de måste vara klara innan hero renderas
  // och blockerar därför INTE resultat-streamingen. getJobAds() +
  // chip-label-resolvern flyttades till `JobbResults` (F6 P4 B1) så att
  // bara resultat-ytan — inte hero:n — byts mot skeleton under en sökning.
  const [taxonomyResult, recentSearchesResult, savedJobAdsResult] =
    await Promise.all([
      getTaxonomyTree(),
      getRecentSearches(),
      getSavedJobAds(),
    ]);

  // ADR 0060: Senaste-sökningar-hero-chip degraderar civilt — vid fel
  // (network/parse/auth-edge) faller chipen till tom-tillstånd och inget
  // visas i hero-topbaren (no-mock-doktrin). Capturen är best-effort på BE.
  const recentSearches =
    recentSearchesResult.kind === "ok" ? recentSearchesResult.data : [];

  // PR5 (Klas-feedback 2026-05-23 + Platsbanken-paritet): Sparade-chip
  // paritet med Senaste-sökningar. Civil degradering vid fel.
  const savedJobAds =
    savedJobAdsResult.kind === "ok" ? savedJobAdsResult.data : [];

  // Träd-hämtning får aldrig blockera sök-ytan. Misslyckas trädet
  // degraderar popovern civilt (tom lista + informativ rad i
  // JobbFilterPopover) (ADR 0043 Beslut B graceful degradation).
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;

  // Suspense-key: byts vid varje ny sökning så fallbacken (skeleton)
  // visas om även navigeringen sker mellan två /jobb-URL:er. Utan en
  // ny key skulle React behålla föregående resultat-träd medan nästa
  // sökning hämtas. searchParams-strängen är en stabil, kollisionsfri
  // identitet för "den här sökningen".
  const resultsKey = new URLSearchParams(
    Object.entries({
      page: params.page ?? "",
      pageSize: params.pageSize ?? "",
      sortBy: params.sortBy ?? "",
      q: params.q ?? "",
    })
  ).toString();
  const ssykKey = ssyk.join(",");
  const regionKey = region.join(",");

  return (
    <>
      {/* MarkJobbVisited — high-water-mark-island som vid mount skriver
          lastSeen=now till localStorage så NY-taggen visas på allt med
          publishedAt > lastSeen vid NÄSTA sid-besök (Klas-direktiv
          2026-05-20). Render-null, ingen visuell yta. */}
      <MarkJobbVisited />
      {/* v3 navy-hero — edge-to-edge i .jp-content (/jobb är v3-native,
          app-shell V3_NATIVE_ROUTES opt-out). GET-form mot /jobb behåller
          befintlig searchParams-mekanik/URL-kontrakt utan client-JS:
          aktiva filter (ssyk[]/region[]/sortBy/pageSize) bärs som hidden
          inputs så en ny sökning inte tappar dem; `page` utelämnas medvetet
          (ny sökterm → sida 1). Ort/Yrke-pills + popovers = client-island
          JobbHeroFilters (F4, ADR 0055 + amendment — INGEN Filter-pill,
          deferred helt). INGA Senaste/Sparade-chips (no-mock-doktrin).
          Hero renderas SYNKRONT — den ligger utanför Suspense-gränsen och
          förblir synlig medan resultatet hämtas (F6 P4 B1). */}
      <section className="jp-hero">
        <div className="jp-hero__inner">
          {/* PR5 (Klas-feedback 2026-05-23 + Platsbanken-paritet): båda
              chips till HÖGER sida av hero-topbaren. Sparade-chip
              (F6 P5 Punkt 2 PR5) + Senaste-sökningar (ADR 0060). */}
          <div
            className="jp-hero__topbar"
            style={{ justifyContent: "flex-end", gap: 8 }}
          >
            <RecentSearchesHeroChip items={recentSearches} />
            <SavedJobAdsHeroChip items={savedJobAds} />
          </div>

          <h1 className="jp-hero__title">Sök bland aktiva annonser</h1>
          <p className="jp-hero__lede">
            Sök bland aktiva annonser från Platsbanken. Filtrera och jämför i
            lugn och ro.
          </p>

          <form action="/jobb" method="get" className="jp-hero__searchblock">
            <label htmlFor="jobb-q" className="jp-hero__searchlabels">
              Sök på ett eller flera ord
            </label>
            <div className="jp-hero__searchrow">
              <input
                id="jobb-q"
                name="q"
                type="search"
                defaultValue={q ?? ""}
                className="jp-hero__input"
              />
              <button type="submit" className="jp-hero__searchbtn">
                <Search size={18} aria-hidden="true" /> Sök
              </button>
            </div>
            {ssyk.map((v) => (
              <input key={`ssyk-${v}`} type="hidden" name="ssyk" value={v} />
            ))}
            {region.map((v) => (
              <input
                key={`region-${v}`}
                type="hidden"
                name="region"
                value={v}
              />
            ))}
            {sortBy !== "PublishedAtDesc" && (
              <input type="hidden" name="sortBy" value={sortBy} />
            )}
            {params.pageSize && (
              <input type="hidden" name="pageSize" value={params.pageSize} />
            )}
          </form>

          {/* Hero-filter-pills + Platsbanken-popovers (client-island,
              F4/ADR 0055). Serialiserbara props: taxonomy-träd, valda
              conceptId string[], q/sortBy/pageSize. Live-commit per klick
              via router.push (transition) — searchParams ADR 0042
              Beslut B (upprepade ssyk/region) OFÖRÄNDRAT. */}
          <JobbHeroFilters
            taxonomy={taxonomy}
            initialSsyk={ssyk}
            initialRegion={region}
            q={q ?? ""}
            sortBy={sortBy}
            pageSize={params.pageSize}
          />
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* Resultat-ytan streamas: <Suspense> visar JobAdListSkeleton
            medan JobbResults await:ar getJobAds(). Hero ovan är redan
            renderad och förblir synlig. `key` byts per sökning så
            skeleton:en visas även vid /jobb→/jobb-navigering (F6 P4 B1). */}
        <Suspense
          key={`${resultsKey}|${ssykKey}|${regionKey}`}
          fallback={<JobAdListSkeleton />}
        >
          <JobbResults
            page={page}
            pageSize={pageSize}
            sortBy={sortBy}
            ssyk={ssyk}
            region={region}
            q={q ?? ""}
            since={since}
            rawParams={params}
          />
        </Suspense>
      </div>
    </>
  );
}

function parsePositiveInt(raw: string | undefined, fallback: number): number {
  if (!raw) return fallback;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

function parseSortBy(raw: string | undefined): JobAdSortBy {
  if (!raw) return "PublishedAtDesc";
  const parsed = jobAdSortBySchema.safeParse(raw);
  return parsed.success ? parsed.data : "PublishedAtDesc";
}

function emptyToUndefined(s: string | undefined): string | undefined {
  return s && s.trim().length > 0 ? s.trim() : undefined;
}

// Normaliserar string | string[] | undefined → string[] (tomma värden bort).
function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  const arr = Array.isArray(raw) ? raw : [raw];
  return arr.map((v) => v.trim()).filter((v) => v.length > 0);
}
