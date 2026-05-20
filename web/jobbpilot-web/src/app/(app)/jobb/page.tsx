import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getJobAds } from "@/lib/api/job-ads";
import { getTaxonomyTree, resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import { jobAdSortBySchema, type JobAdSortBy } from "@/lib/dto/job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { Search } from "lucide-react";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobbHeroFilters } from "@/components/job-ads/jobb-hero-filters";
import { JobbResultsToolbar } from "@/components/job-ads/jobb-results-toolbar";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { MarkJobbVisited } from "@/components/job-ads/mark-jobb-visited";

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

  // ADR 0043 — picker-träd + reverse-lookup för redan-valda concept-id
  // hämtas server-side (CLAUDE.md §4.3/§5.2 — ingen useEffect-fetch).
  // Parallellt med listan: oberoende requests, inga inbördes beroenden.
  const selectedConceptIds = [...ssyk, ...region];
  const [result, taxonomyResult, labelsResult] = await Promise.all([
    getJobAds({ page, pageSize, sortBy, ssyk, region, q, since }),
    getTaxonomyTree(),
    resolveTaxonomyLabels(selectedConceptIds),
  ]);

  // Träd-/label-hämtning får aldrig blockera sök-ytan. Misslyckas trädet
  // degraderar popovern civilt (tom lista + informativ rad i
  // JobbFilterPopover); reverse-lookup-miss → chip faller till
  // "Okänd kod (<id>)" i toolbaren (ADR 0043 Beslut B graceful degradation).
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;
  // Plain Record (EJ Map) — passas över RSC→client-gränsen till
  // JobbResultsToolbar (Map serialiseras inte i RSC-payloaden; verifierat
  // mot Next-docs server-and-client-components "props passed from a Server
  // Component to a Client Component").
  const resolvedLabels: Record<string, string> =
    labelsResult.kind === "ok"
      ? Object.fromEntries(
          labelsResult.data.map((l) => [l.conceptId, l.label] as const)
        )
      : {};

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
          deferred helt). INGA Senaste/Sparade-chips (no-mock-doktrin). */}
      <section className="jp-hero">
        <div className="jp-hero__inner">
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
        {renderResult(
          result,
          params,
          pageSize,
          ssyk,
          region,
          resolvedLabels,
          q ?? "",
          sortBy
        )}
      </div>
    </>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getJobAds>>,
  params: JobbSearchParams,
  pageSize: number,
  ssyk: string[],
  region: string[],
  resolvedLabels: Record<string, string>,
  q: string,
  sortBy: JobAdSortBy
) {
  switch (result.kind) {
    case "ok":
      return (
        <>
          {/* Result-toolbar (client-island): N träffar + aktiva chips +
              sort-dropdown på samma rad (F4/ADR 0055). totalCount kommer
              från RSC-fetchen; chips/sort live-commit:ar searchParams
              symmetriskt med hero-pills (buildJobbHref). */}
          <JobbResultsToolbar
            totalCount={result.data.totalCount}
            ssyk={ssyk}
            region={region}
            resolvedLabels={resolvedLabels}
            q={q}
            sortBy={sortBy}
            pageSize={params.pageSize}
          />
          <div className="flex flex-col gap-2.5">
            <JobAdList jobAds={result.data.items} />
            <JobAdPagination
              page={result.data.page}
              pageSize={result.data.pageSize}
              totalCount={result.data.totalCount}
              buildHref={(targetPage) =>
                buildPageHref(params, targetPage, pageSize)
              }
            />
          </div>
        </>
      );
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div
          role="alert"
          className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
        >
          <p className="text-body font-medium text-warning-700">
            För många förfrågningar
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            Du har gjort för många förfrågningar på kort tid. Försök igen om{" "}
            {result.retryAfterSeconds} sekunder.
          </p>
        </div>
      );
    // notFound/forbidden/error kollapsas till samma copy: list-endpointen kan
    // aldrig runtime-faktiskt returnera 404 (responseToResult sätter inte
    // includeNotFound) och job-ads endpoint är endast auth-gated (forbidden
    // exponeras inte idag) — alla tre faller till samma "tekniskt fel"-copy.
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">Kunde inte ladda jobbannonser</p>
          <p className="mt-1 text-body-sm">
            Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.
          </p>
        </div>
      );
    default:
      return assertNever(result);
  }
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

function buildPageHref(
  params: JobbSearchParams,
  targetPage: number,
  defaultPageSize: number
): string {
  const url = new URLSearchParams();
  if (targetPage !== 1) url.set("page", String(targetPage));
  if (params.pageSize && Number(params.pageSize) !== defaultPageSize) {
    url.set("pageSize", params.pageSize);
  }
  if (params.sortBy && params.sortBy !== "PublishedAtDesc") {
    url.set("sortBy", params.sortBy);
  }
  for (const v of toStringList(params.ssyk)) url.append("ssyk", v);
  for (const v of toStringList(params.region)) url.append("region", v);
  if (params.q) url.set("q", params.q);
  const qs = url.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}
