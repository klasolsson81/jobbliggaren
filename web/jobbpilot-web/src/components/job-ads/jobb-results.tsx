import { redirect } from "next/navigation";
import { getJobAds } from "@/lib/api/job-ads";
import { getJobAdStatusBatch } from "@/lib/api/job-ad-status";
import { resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import { assertNever } from "@/lib/dto/_helpers";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobbResultsToolbar } from "@/components/job-ads/jobb-results-toolbar";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";

/**
 * Resultatdelen av /jobb (F6 P4).
 *
 * Detta är den enda delen av /jobb som hänger på `getJobAds()`. Den är
 * extraherad till en egen `async` Server Component så att `jobb/page.tsx`
 * kan rendera hero (sökfält, filter-pills) synkront och wrappa ENBART
 * denna komponent i `<Suspense fallback={<JobAdListSkeleton />}>`.
 *
 * Effekten: under en sökning byts bara resultat-ytan mot skeleton —
 * sökfältet användaren just använde, hero:n och sidans chrome förblir
 * renderade. Detta är den idiomatiska Next.js streaming-patternen och
 * ersätter det tidigare `loading.tsx`, som var en route-segment-fallback
 * och därför raderade HELA /jobb-segmentet (inklusive hero + träffräknare)
 * vid varje sökning (design-reviewer F6 P4 B1).
 *
 * Träffräknaren bor i `JobbResultsToolbar` och är data-beroende
 * (`totalCount` + filter-chip-labels). Den kan därför inte ligga utanför
 * Suspense-gränsen — den skulle inte kunna visa rätt antal innan
 * `getJobAds()` landat. Toolbaren renderas alltså tillsammans med listan
 * här inne, och `JobAdListSkeleton` speglar toolbar-raden så layouten
 * inte hoppar när data landar.
 *
 * `resolveTaxonomyLabels` hämtas också här: chip-labels i toolbaren beror
 * på de valda concept-id:na och hör ihop med resultat-renderingen. Träd-
 * och senaste-sökningar-hämtning ligger kvar i `page.tsx` (hero-beroenden
 * som måste renderas synkront).
 */

interface JobbResultsProps {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  ssyk: string[];
  region: string[];
  q: string;
  since: string;
  /** Råa searchParams — endast för att bygga paginerings-href. */
  rawParams: {
    page?: string;
    pageSize?: string;
    sortBy?: string;
    ssyk?: string | string[];
    region?: string | string[];
    q?: string;
  };
}

export async function JobbResults({
  page,
  pageSize,
  sortBy,
  ssyk,
  region,
  q,
  since,
  rawParams,
}: JobbResultsProps) {
  // Chip-labels hör ihop med resultatet — hämtas parallellt med listan.
  // Reverse-lookup-miss → chip faller till "Okänd kod (<id>)" i toolbaren
  // (ADR 0043 Beslut B graceful degradation).
  const selectedConceptIds = [...ssyk, ...region];
  const [result, labelsResult] = await Promise.all([
    getJobAds({ page, pageSize, sortBy, ssyk, region, q, since }),
    resolveTaxonomyLabels(selectedConceptIds),
  ]);

  // Plain Record (EJ Map) — passas över RSC→client-gränsen till
  // JobbResultsToolbar (Map serialiseras inte i RSC-payloaden).
  const resolvedLabels: Record<string, string> =
    labelsResult.kind === "ok"
      ? Object.fromEntries(
          labelsResult.data.map((l) => [l.conceptId, l.label] as const)
        )
      : {};

  switch (result.kind) {
    case "ok": {
      // PR5 / ADR 0063 — per-user-overlay-status batch (Sparad/Ansökt-taggar
      // på list-kort). Anonym/utan-auth → tomma set:n (degraderar civilt,
      // inga taggar visas). Max 100 IDs per anrop = validator-cap.
      const itemIds = result.data.items.map((it) => it.id);
      const status = await getJobAdStatusBatch(itemIds);
      const savedIdSet = new Set(status.savedIds);
      const appliedIdSet = new Set(status.appliedIds);

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
            pageSize={rawParams.pageSize}
          />
          <div className="flex flex-col gap-2.5">
            <JobAdList
              jobAds={result.data.items}
              savedIdSet={savedIdSet}
              appliedIdSet={appliedIdSet}
            />
            <JobAdPagination
              page={result.data.page}
              pageSize={result.data.pageSize}
              totalCount={result.data.totalCount}
              buildHref={(targetPage) =>
                buildPageHref(rawParams, targetPage, pageSize)
              }
            />
          </div>
        </>
      );
    }
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

// Normaliserar string | string[] | undefined → string[] (tomma värden bort).
function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  const arr = Array.isArray(raw) ? raw : [raw];
  return arr.map((v) => v.trim()).filter((v) => v.length > 0);
}

function buildPageHref(
  params: JobbResultsProps["rawParams"],
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
