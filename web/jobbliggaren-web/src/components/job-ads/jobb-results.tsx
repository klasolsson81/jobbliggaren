import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getJobAds } from "@/lib/api/job-ads";
import { getJobAdStatusBatch } from "@/lib/api/job-ad-status";
import { getJobAdMatchTags } from "@/lib/api/job-ad-match";
import { getMyProfile } from "@/lib/api/me";
import { resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";
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
  occupationGroup: string[];
  region: string[];
  municipality: string[];
  // Klass 2 (2026-06-13) — anställningsform + omfattning.
  employmentType: string[];
  worktimeExtent: string[];
  // STEG 5 (grade-filter, 2026-06-23) — valda matchningsgrader (enum-namn,
  // delmängd av Basic/Good/Strong; validerad + Top-strippad i page.tsx). Tom =
  // inget grad-filter ("Matchning av"). Skickas vidare till list-queryn.
  matchGrades: string[];
  q: string;
  since: string;
  /**
   * E2j (ADR 0060 amend 2026-06-12) — commit-intent: när URL:en bär
   * ?commit=1 (avsiktlig sökning via Enter/Sök/förslags-val/toolbar) skickas
   * det vidare till list-queryn så backend auto-capturerar sökningen.
   * Live-förhandsvisning (utan flaggan) fångas EJ. Transient — strippas ur
   * URL:en efter mount av `StripCommitParam`.
   */
  commit: boolean;
  /** Råa searchParams — endast för att bygga paginerings-href. */
  rawParams: {
    page?: string;
    pageSize?: string;
    sortBy?: string;
    occupationGroup?: string | string[];
    region?: string | string[];
    municipality?: string | string[];
    employmentType?: string | string[];
    worktimeExtent?: string | string[];
    matchGrades?: string | string[];
    q?: string;
  };
}

export async function JobbResults({
  page,
  pageSize,
  sortBy,
  occupationGroup,
  region,
  municipality,
  employmentType,
  worktimeExtent,
  matchGrades,
  q,
  since,
  commit,
  rawParams,
}: JobbResultsProps) {
  // Async Server Component → awaitable next-intl translator (jobads.ui).
  const t = await getTranslations("jobads.ui");
  // Chip-labels hör ihop med resultatet — hämtas parallellt med listan.
  // Reverse-lookup-miss → chip faller till "Okänd kod (<id>)" i toolbaren
  // (ADR 0043 Beslut B graceful degradation).
  // Cap-aritmetik (E2b-architect fråga 5): backend-resolve-capet är
  // MaxConceptIds × 4 = 1600; teoretiskt max här = 400 yrkesgrupper +
  // 21 län + 290 kommuner = 711 — täcker, men marginalen krymper om en
  // fjärde dimension (employmentType, B2) någonsin chip-resolvas.
  // Klass 2 — anställningsform/omfattning chip-resolvas via samma server-
  // reverse-lookup (kind-agnostisk sedan PR-1). Cap-aritmetik (E2b fråga 5):
  // backend-resolve-capet MaxConceptIds×4 = 1600 täcker även de ~8+2 nya.
  const selectedConceptIds = [
    ...occupationGroup,
    ...region,
    ...municipality,
    ...employmentType,
    ...worktimeExtent,
  ];
  // F4-16 (CTO D8) — `hasStatedDesiredOccupation` hämtas här (samma parallella
  // fetch, ingen waterfall, blockerar ej hero) och tråds in i toolbaren för
  // in-/jobb-disclosuren. getMyProfile är `cache()`:ad (dedupar mot andra
  // läsare i samma request). Fel/anonym → false (ingen falsk disclosure).
  const [result, labelsResult, profileResult] = await Promise.all([
    getJobAds({
      page,
      pageSize,
      sortBy,
      occupationGroup,
      region,
      municipality,
      employmentType,
      worktimeExtent,
      matchGrades,
      q,
      since,
      commit,
    }),
    resolveTaxonomyLabels(selectedConceptIds),
    getMyProfile(),
  ]);

  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" &&
    profileResult.data.hasStatedDesiredOccupation;

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
      const [status, matchTags] = await Promise.all([
        getJobAdStatusBatch(itemIds),
        // F4-13 (ADR 0076) — graderad match-tagg-overlay. Anonym/utan-auth →
        // tom batch (degraderar civilt, inga taggar). POSITIVE-ONLY: bara
        // annonser med positiv grad finns i `entries`.
        getJobAdMatchTags(itemIds),
      ]);
      const savedIdSet = new Set(status.savedIds);
      const appliedIdSet = new Set(status.appliedIds);
      // Map<JobAdId, MatchGrade> — O(1)-lookup per kort (paritet med
      // savedIdSet/appliedIdSet). `entries` är ett plain Record; bygg Map här.
      const matchGradeById = new Map<string, MatchGrade>(
        Object.entries(matchTags.entries).map(
          ([id, entry]) => [id, entry.grade] as const
        )
      );

      return (
        <>
          {/* Result-toolbar (client-island): N träffar + aktiva chips +
              sort-dropdown på samma rad (F4/ADR 0055). totalCount kommer
              från RSC-fetchen; chips/sort live-commit:ar searchParams
              symmetriskt med hero-pills (buildJobbHref). */}
          <JobbResultsToolbar
            totalCount={result.data.totalCount}
            occupationGroup={occupationGroup}
            region={region}
            municipality={municipality}
            employmentType={employmentType}
            worktimeExtent={worktimeExtent}
            matchGrades={matchGrades}
            resolvedLabels={resolvedLabels}
            q={q}
            sortBy={sortBy}
            pageSize={rawParams.pageSize}
            hasStatedDesiredOccupation={hasStatedDesiredOccupation}
          />
          <div className="flex flex-col gap-2.5">
            <JobAdList
              jobAds={result.data.items}
              savedIdSet={savedIdSet}
              appliedIdSet={appliedIdSet}
              matchGradeById={matchGradeById}
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
            {t("results.rateLimitedTitle")}
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            {t("results.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
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
          <p className="text-body font-medium">{t("results.errorTitle")}</p>
          <p className="mt-1 text-body-sm">{t("results.errorBody")}</p>
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
  for (const v of toStringList(params.occupationGroup))
    url.append("occupationGroup", v);
  for (const v of toStringList(params.region)) url.append("region", v);
  // E2b — utan denna rad tappar sida-2-klicket kommun-filtret (samma
  // felklass som F3 B-FIX; buildPageHref är en ANDRA URL-builder vid
  // sidan av buildJobbHref — architect-dom fråga 4.1).
  for (const v of toStringList(params.municipality))
    url.append("municipality", v);
  // Klass 2 — utan dessa tappar sida-2-klicket anställningsform/omfattning
  // (samma felklass som municipality ovan; buildPageHref är en andra URL-
  // builder vid sidan av buildJobbHref).
  for (const v of toStringList(params.employmentType))
    url.append("employmentType", v);
  for (const v of toStringList(params.worktimeExtent))
    url.append("worktimeExtent", v);
  // STEG 5 — utan denna rad tappar sida-2-klicket grad-filtret (samma felklass
  // som municipality/Klass-2 ovan; buildPageHref är en andra URL-builder vid
  // sidan av buildJobbHref). Page-validatorn droppar Top/okänt redan.
  for (const v of toStringList(params.matchGrades))
    url.append("matchGrades", v);
  if (params.q) url.set("q", params.q);
  const qs = url.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}
