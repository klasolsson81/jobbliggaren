import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getJobAds } from "@/lib/api/job-ads";
import { getTaxonomyTree, resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import {
  jobAdSortBySchema,
  type JobAdSortBy,
  type JobAdFiltersValues,
} from "@/lib/dto/job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { Search } from "lucide-react";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobAdFilters } from "@/components/job-ads/job-ad-filters";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";

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

  const filtersInitial: JobAdFiltersValues = {
    ssyk,
    region,
    q: q ?? "",
    sortBy,
  };

  // Disclosure-räknare (Beslut A): antal aktiva taxonomi-filter bakom
  // disclosuren. Sökordet räknas inte (alltid-synlig primär yta) och
  // sorteringen räknas inte längre — den flyttades ut till en egen
  // alltid-synlig kontroll (Klas 2026-05-17), så den är inte "gömd" och
  // ska inte driva disclosure-räknaren/auto-expand.
  const activeFilterCount = ssyk.length + region.length;

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
  // degraderar väljarna civilt (tomma listor + informativ rad i
  // JobAdFilters); reverse-lookup-miss → chip faller till "Okänd kod (<id>)"
  // i picker-komponenten (ADR 0043 Beslut B graceful degradation).
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;
  const resolvedLabels = new Map<string, string>(
    labelsResult.kind === "ok"
      ? labelsResult.data.map((l) => [l.conceptId, l.label] as const)
      : []
  );

  return (
    <>
      {/* v3 navy-hero — edge-to-edge i .jp-content (/jobb är v3-native,
          app-shell V3_NATIVE_ROUTES opt-out). GET-form mot /jobb behåller
          befintlig searchParams-mekanik/URL-kontrakt utan client-JS:
          aktiva filter (ssyk[]/region[]/sortBy/pageSize) bärs som hidden
          inputs så en ny sökning inte tappar dem; `page` utelämnas medvetet
          (ny sökterm → sida 1). INGA Ort/Yrke/Filter-pills (= filter-
          popover = F4) och INGA Senaste/Sparade-chips (ingen recent/saved-
          data, no-mock-doktrin). */}
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
                placeholder="t.ex. backend Stockholm"
                aria-label="Sökord"
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
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* JobAdFilters behålls AS-IS (v2-disclosure) — F4 refaktorerar
            till Platsbanken-popovers (un-refaktorerat mellantillstånd,
            branch-by-abstraction). SaveSearchButton borttagen ur /jobb
            (HANDOVER §9-veto: ingen spara-sökning-knapp här). */}
        <JobAdFilters
          initial={filtersInitial}
          activeFilterCount={activeFilterCount}
          taxonomy={taxonomy}
          resolvedLabels={resolvedLabels}
        />

        <div className="mt-6 flex flex-col gap-2.5">
          {renderResult(result, params, pageSize)}
        </div>
      </div>
    </>
  );
}

function renderResult(
  result: Awaited<ReturnType<typeof getJobAds>>,
  params: JobbSearchParams,
  pageSize: number
) {
  switch (result.kind) {
    case "ok":
      return (
        <>
          <p
            className="font-mono text-body-sm text-text-secondary"
            role="status"
            aria-live="polite"
          >
            {result.data.totalCount === 0
              ? "Inga träffar"
              : `${result.data.totalCount.toLocaleString("sv-SE")} träffar`}
          </p>
          <JobAdList jobAds={result.data.items} />
          <JobAdPagination
            page={result.data.page}
            pageSize={result.data.pageSize}
            totalCount={result.data.totalCount}
            buildHref={(targetPage) =>
              buildPageHref(params, targetPage, pageSize)
            }
          />
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
