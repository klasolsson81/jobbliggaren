import { redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";
import { getJobAds } from "@/lib/api/job-ads";
import {
  jobAdSortBySchema,
  type JobAdSortBy,
  type JobAdFiltersValues,
} from "@/lib/dto/job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobAdFilters } from "@/components/job-ads/job-ad-filters";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { SaveSearchButton } from "@/components/saved-searches/save-search-button";

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

  // Disclosure-räknare (Beslut A): antal aktiva taxonomi-/sort-filter.
  // Sökordet räknas inte — det är den alltid-synliga primära ytan.
  const activeFilterCount =
    ssyk.length +
    region.length +
    (sortBy !== "PublishedAtDesc" ? 1 : 0);

  const result = await getJobAds({
    page,
    pageSize,
    sortBy,
    ssyk,
    region,
    q,
    since,
  });

  return (
    <div className="flex flex-col">
      <div>
        <h1 className="jp-h1">Jobb</h1>
        <p className="jp-lede">
          Sök bland aktiva annonser från Platsbanken. Filtrera, jämför och spara.
        </p>
      </div>

      <div className="mt-7">
        <JobAdFilters
          initial={filtersInitial}
          activeFilterCount={activeFilterCount}
        />
      </div>

      <div className="mt-4">
        <SaveSearchButton
          ssyk={ssyk}
          region={region}
          q={q ?? ""}
          sortBy={sortBy}
        />
      </div>

      <div className="mt-6 flex flex-col gap-2.5">
        {renderResult(result, params, pageSize)}
      </div>
    </div>
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
