import { after } from "next/server";
import { redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { formatNumber } from "@/lib/i18n/format";
import { Announce } from "@/components/common/announcer";
import { getSessionId } from "@/lib/auth/session";
import { getJobAds } from "@/lib/api/job-ads";
import { getJobAdStatusBatch } from "@/lib/api/job-ad-status";
import { getJobAdMatchTags } from "@/lib/api/job-ad-match";
import { getEmployerApplicationCounts } from "@/lib/api/employer-application-counts";
import { getFollowedJobAdIds } from "@/lib/api/company-follows";
import { getMyProfile } from "@/lib/api/me";
import { getJobsWatermark, markJobsSeen } from "@/lib/api/me-jobs";
import { resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { MatchGrade, JobAdMatchBatch } from "@/lib/dto/job-ad-match";
import { assertNever } from "@/lib/dto/_helpers";
import {
  buildJobbHref,
  buildPageHref,
  type JobbRawSearchParams,
} from "@/lib/job-ads/search-params";
import { maxCreatedAt } from "@/lib/job-ads/seen-window";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobbResultsToolbar } from "@/components/job-ads/jobb-results-toolbar";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";

/**
 * The results part of /jobb, and the only part of the page that depends on
 * `getJobAds()`. It is its own async Server Component so that `jobb/page.tsx` can
 * render the hero at once and wrap only this component in
 * `<Suspense fallback={<JobAdListSkeleton />}>`: during a search only the results
 * are replaced by the skeleton, and the search field the user just used stays.
 *
 * The hit count in `JobbResultsToolbar` and its chip labels depend on the data,
 * so the toolbar renders here with the list, and `JobAdListSkeleton` reserves the
 * toolbar's row so the layout does not jump when the data arrives.
 */

interface JobbResultsProps {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  occupationGroup: string[];
  region: string[];
  municipality: string[];
  /** A place facet the backend combines with region and municipality. */
  remote: boolean;
  employmentType: string[];
  worktimeExtent: string[];
  /** Selected grades, validated in page.tsx. Empty means every grade is shown. */
  matchGrades: string[];
  /** From `?matchning=off`; `true` means off. `matchActive` is derived from it below. */
  matchningOff: boolean;
  /**
   * From `?relaterade=on`: also include ads graded as a related occupation, in both
   * the list query and the grade batch. Applies only while matching is active.
   */
  includeRelated: boolean;
  /**
   * From `?doljAnsokta=on`: hide ads the user has applied to. Independent of
   * matching; the control lives in the hero filter row.
   */
  hideApplied: boolean;
  /**
   * From `?baraMatchade=on`: list only ads with a positive grade for the user.
   * Applies only while matching is active (see `effectiveOnlyMatched`).
   */
  onlyMatched: boolean;
  /**
   * Organisation numbers of 10 digits each, validated in page.tsx. The list query
   * filters on them exactly, independently of matching.
   */
  employer: ReadonlyArray<string>;
  q: string;
  /**
   * From `?commit=true`, which a deliberate search sets (Enter, the search button,
   * a picked suggestion, the toolbar). The list query passes it on so the backend
   * saves the search as a recent search; a live preview without it is not saved.
   */
  commit: boolean;
  /**
   * The raw search params. Their shape lives with `buildPageHref` in
   * `lib/job-ads/search-params.ts`.
   */
  rawParams: JobbRawSearchParams;
}

export async function JobbResults({
  page,
  pageSize,
  sortBy,
  occupationGroup,
  region,
  municipality,
  remote,
  employmentType,
  worktimeExtent,
  matchGrades,
  matchningOff,
  includeRelated,
  hideApplied,
  onlyMatched,
  employer,
  q,
  commit,
  rawParams,
}: JobbResultsProps) {
  const t = await getTranslations("jobads.ui");
  // The announced count is formatted by the same helper the toolbar renders with,
  // so the number a screen reader hears and the number on screen cannot diverge.
  const format = await getFormatter();
  // Chip labels belong to the result, so they are resolved alongside the list. A
  // concept id the backend cannot name comes back without a label, and the toolbar
  // names its chip from the catalogue instead (ADR 0043 Beslut B).
  const selectedConceptIds = [
    ...occupationGroup,
    ...region,
    ...municipality,
    ...employmentType,
    ...worktimeExtent,
  ];
  // The profile is read before the list query because matchActive decides the
  // sort and whether grades are fetched. getMyProfile is cache()-wrapped and the
  // page has already started the same read, so this await costs no extra
  // round-trip. An error or a guest counts as no stated occupation.
  const profileResult = await getMyProfile();
  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" &&
    profileResult.data.hasStatedDesiredOccupation;

  // Matching is active exactly when the user has stated an occupation and has not
  // switched matching off. Everything below that depends on matching reads this.
  const matchActive = hasStatedDesiredOccupation && !matchningOff;

  // Both toggles are matching concepts. Their controls render only while matching
  // is on, so these guards stop a stale or edited URL from widening or narrowing
  // the list when it is off.
  const effectiveIncludeRelated = matchActive && includeRelated;
  const effectiveOnlyMatched = matchActive && onlyMatched;

  // With matching inactive, a match sort falls back to newest first. The toolbar
  // applies the same rule to its select from the same matchActive, so the order
  // shown and the order used agree; the URL may keep the inert MatchDesc until
  // the user next picks a sort.
  const effectiveSortBy: JobAdSortBy =
    !matchActive && sortBy === "MatchDesc" ? "PublishedAtDesc" : sortBy;

  // Every row links into the job modal with the current list's query string (see
  // `JobAdCard.listQuery`). With a bare `/jobb/[id]` link the list beneath the
  // modal re-rendered with empty search params, and `router.back()` restores only
  // the `@modal` slot, so closing the modal left the list unfiltered. The link
  // carries the raw URL state, not the effective values above, so the URL after
  // closing equals the URL before opening; `page` is added so deep pages survive.
  const listHref = buildJobbHref({
    q,
    occupationGroup,
    region,
    municipality,
    remote,
    employmentType,
    worktimeExtent,
    matchGrades,
    matchningOff,
    includeRelated,
    hideApplied,
    onlyMatched,
    employer,
    sortBy,
    pageSize: rawParams.pageSize,
  });
  const listBaseQuery = listHref.includes("?")
    ? listHref.slice(listHref.indexOf("?") + 1)
    : "";
  const pageParam =
    rawParams.page && rawParams.page !== "1" ? `page=${rawParams.page}` : "";
  const listQuery = [listBaseQuery, pageParam].filter(Boolean).join("&");

  // The user's "seen up to" watermark is read alongside the list. "New" is judged
  // against the watermark as read, and the watermark moves forward afterwards (as
  // on /matchningar). A read error or a guest gives null, so nothing is new.
  const [result, labelsResult, watermarkResult] = await Promise.all([
    getJobAds({
      page,
      pageSize,
      sortBy: effectiveSortBy,
      occupationGroup,
      region,
      municipality,
      remote,
      employmentType,
      worktimeExtent,
      matchGrades,
      includeRelated: effectiveIncludeRelated,
      hideApplied,
      onlyMatched: effectiveOnlyMatched,
      employer,
      q,
      commit,
    }),
    resolveTaxonomyLabels(selectedConceptIds),
    getJobsWatermark(),
  ]);

  const watermark =
    watermarkResult.kind === "ok" ? watermarkResult.data.lastSeenJobsAt : null;

  // A plain Record rather than a Map, because it crosses the RSC boundary to the
  // toolbar. Ids without a label are left out instead of carrying null: the
  // toolbar already names a missing id from the catalogue.
  const resolvedLabels: Record<string, string> =
    labelsResult.kind === "ok"
      ? Object.fromEntries(
          labelsResult.data.flatMap((l) =>
            l.label === null ? [] : [[l.conceptId, l.label] as const]
          )
        )
      : {};

  switch (result.kind) {
    case "ok": {
      // An ad is new when it was ingested (`createdAt`) after the watermark as read.
      const watermarkMs = watermark != null ? Date.parse(watermark) : Number.NaN;
      const newIdSet = new Set<string>(
        Number.isNaN(watermarkMs)
          ? []
          : result.data.items
              .filter((it) => Date.parse(it.createdAt) > watermarkMs)
              .map((it) => it.id)
      );

      // Per-card overlays for this page (at most 100 ids, the batch validator's cap;
      // page.tsx caps pageSize to match). Grades are fetched only while matching is
      // active; saved/applied status, earlier applications per employer and followed
      // employers do not depend on matching and are always fetched. Each batch
      // degrades to empty for a guest or on error, so the cards simply show no tags.
      const itemIds = result.data.items.map((it) => it.id);
      const [status, matchTags, employerApplicationCounts, followedIds] = await Promise.all([
        getJobAdStatusBatch(itemIds),
        // Only ads with a positive grade are present (ADR 0076).
        matchActive
          ? getJobAdMatchTags(itemIds, effectiveIncludeRelated)
          : Promise.resolve<JobAdMatchBatch>({ entries: {} }),
        getEmployerApplicationCounts(itemIds),
        getFollowedJobAdIds(itemIds),
      ]);
      const savedIdSet = new Set(status.savedIds);
      const appliedIdSet = new Set(status.appliedIds);
      const followedIdSet = new Set(followedIds);
      // Only positive counts are present, so a missing key means no badge.
      const employerApplicationCountById = new Map<string, number>(
        Object.entries(employerApplicationCounts.countsByJobAdId)
      );
      const matchGradeById = new Map<string, MatchGrade>(
        Object.entries(matchTags.entries).map(
          ([id, entry]) => [id, entry.grade] as const
        )
      );

      // Move the watermark forward only now that "new" has been judged against the
      // old one, so the next visit shows only what arrived since this one. It moves
      // to the newest `createdAt` on this page, not to the current time, so an ad
      // ingested in between stays new; the maximum is taken over the whole page
      // because /jobb can be sorted by relevance or match. The original ISO string
      // keeps full precision, and an empty page sends undefined, which the backend
      // reads as now. It moves only after a successful read, so a transient error
      // cannot clear "new". The write runs after the response via `after()`; the
      // session is read during render, because an `after()` callback in a Server
      // Component cannot read cookies. A failed write leaves the watermark as it was.
      if (watermarkResult.kind === "ok") {
        const seenThrough = maxCreatedAt(result.data.items);
        const sessionId = await getSessionId();
        if (sessionId) {
          after(() => markJobsSeen(seenThrough, sessionId));
        }
      }

      // `?employer=` is an exact filter on organisation number, so when exactly one
      // employer is filtered, every row belongs to it and its `companyName` is the
      // employer's name. The chip shows that name without a lookup service and
      // without putting the name in the URL. On an empty page the chip falls back
      // to the organisation number.
      const soleEmployerName =
        employer.length === 1
          ? [...new Set(result.data.items.map((it) => it.companyName))]
              .sort((a, b) => a.localeCompare(b, "sv"))[0]
          : undefined;

      return (
        <>
          {/* #1505 — the sentence that ends the load, announced through the page's persistent
              region. Both arms are already on screen — the count in the toolbar, the empty pair in
              `JobAdList`'s `.jp-empty` — so nothing is invented here and Understanding 4.1.3's
              caveat about not forcing new status messages is respected by construction.
              The empty arm carries title AND body for the same reason the two error branches do:
              the title states the dead end and only the body gives the way out. */}
          <Announce
            message={
              result.data.totalCount === 0
                ? `${t("list.emptyTitle")} ${t("list.emptyBody")}`
                : `${formatNumber(format, result.data.totalCount)} ${t("toolbar.hits", { count: result.data.totalCount })}`
            }
          />
          {/* Hit count, active filter chips and sort on one row (ADR 0055). */}
          <JobbResultsToolbar
            totalCount={result.data.totalCount}
            occupationGroup={occupationGroup}
            region={region}
            municipality={municipality}
            remote={remote}
            employmentType={employmentType}
            worktimeExtent={worktimeExtent}
            matchGrades={matchGrades}
            includeRelated={includeRelated}
            matchningOff={matchningOff}
            hideApplied={hideApplied}
            onlyMatched={onlyMatched}
            employer={employer}
            employerName={soleEmployerName}
            resolvedLabels={resolvedLabels}
            q={q}
            sortBy={sortBy}
            pageSize={rawParams.pageSize}
            hasStatedDesiredOccupation={hasStatedDesiredOccupation}
            matchActive={matchActive}
          />
          <div className="flex flex-col gap-2.5">
            <JobAdList
              jobAds={result.data.items}
              newIdSet={newIdSet}
              savedIdSet={savedIdSet}
              appliedIdSet={appliedIdSet}
              followedIdSet={followedIdSet}
              matchGradeById={matchGradeById}
              employerApplicationCountById={employerApplicationCountById}
              listQuery={listQuery}
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
        // #1395 — `mt-6` matches `.jp-results-toolbar`'s own top margin, so this branch sits
        // where the results would. Without it the card butts against the section heading added
        // above the boundary and reads as that heading's own box. The margin lives HERE and not
        // on the `.jp-h2`: `.jp-*` is unlayered and beats `@layer utilities`, so an `mb-*` on
        // the heading computes to 0 (globals.css names the same trap). Same placement as
        // `foretag-sok-results.tsx`'s ErrorShell, which carries its own margin for this reason.
        <div className="mt-6 rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4">
          {/* #1505 — a start that is never closed leaves a screen reader waiting on a load that
              has in fact finished, which is worse than the silence it replaced. Title AND body,
              because the title alone says an error occurred and only the body says what to do
              about it. */}
          <Announce
            message={`${t("results.rateLimitedTitle")} ${t("results.rateLimitedBody", { seconds: result.retryAfterSeconds })}`}
          />
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
    // The list endpoint cannot return 404 (responseToResult is not asked to map it)
    // and has no 403 today, so all three share the technical-error copy.
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="mt-6 rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          {/* #1505 — same reason as the rate-limit branch: every branch that ends a load closes
              its own announcement. This card carries no role at all, so before this it reached a
              screen reader through nothing. */}
          <Announce
            message={`${t("results.errorTitle")} ${t("results.errorBody")}`}
          />
          <p className="text-body font-medium">{t("results.errorTitle")}</p>
          <p className="mt-1 text-body-sm">{t("results.errorBody")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}
