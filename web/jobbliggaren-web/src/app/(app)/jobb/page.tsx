import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { jobAdSortBySchema, type JobAdSortBy } from "@/lib/dto/job-ads";
import { isListMatchGrade } from "@/lib/dto/job-ad-match";
import {
  parseQParam,
  MATCHNING_OFF_VALUE,
  RELATERADE_ON_VALUE,
  DISTANS_ON_VALUE,
  STATUS_ON_VALUE,
  parseEmployerParam,
  toStringList,
} from "@/lib/job-ads/search-params";
import { Announcer } from "@/components/common/announcer";
import { JobbHeroFilters } from "@/components/job-ads/jobb-hero-filters";
import { JobbHeroSearch } from "@/components/job-ads/jobb-hero-search";
import { JobbResults } from "@/components/job-ads/jobb-results";
import { JobAdListSkeleton } from "@/components/job-ads/job-ad-list-skeleton";
import { StripCommitParam } from "@/components/job-ads/strip-commit-param";
import { RecentSearchesHeroChip } from "@/components/recent-searches/recent-searches-hero-chip";
import { SavedJobAdsHeroChip } from "@/components/saved-job-ads/saved-job-ads-hero-chip";

import type { Metadata } from "next";
import { PageFeedback } from "@/components/feedback/page-feedback";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages");
  return { title: t("jobb.meta.title") };
}

// Next.js delivers a repeated query parameter as an array. The multi-value filters
// are written as one joined parameter (ADR 0042 Beslut B); `toStringList` still
// reads the older repeated form, so bookmarked links keep working.
// occupationGroup is the SSYK level-4 occupation group. The backend combines
// region and municipality into one place filter.
type JobbSearchParams = {
  page?: string;
  pageSize?: string;
  sortBy?: string;
  occupationGroup?: string | string[];
  region?: string | string[];
  municipality?: string | string[];
  // Each toggle below reacts only to its exact value; any other value is treated
  // as absent.
  distans?: string;
  employmentType?: string | string[];
  worktimeExtent?: string | string[];
  matchGrades?: string | string[];
  // `?matchning=off` turns matching off: no grades and no match sorting. Matching
  // is otherwise on whenever the user has stated an occupation.
  matchning?: string;
  // `?relaterade=on` also lists ads graded as a related occupation.
  relaterade?: string;
  // `?doljAnsokta=on` hides ads the user has already applied to.
  doljAnsokta?: string;
  // `?baraMatchade=on` lists only ads with a positive match grade.
  baraMatchade?: string;
  // One or more 10-digit organisation numbers. A malformed value is dropped on
  // its own, so it cannot void a valid filter next to it: the backend validator
  // would reject the whole query.
  employer?: string | string[];
  q?: string | string[];
  // `?commit=true` marks a deliberate search, which the backend saves as a recent
  // search; a live preview while typing does not carry it.
  commit?: string;
};

interface PageProps {
  searchParams: Promise<JobbSearchParams>;
}

const DEFAULT_PAGE_SIZE = 20;

export default async function JobbPage({ searchParams }: PageProps) {
  // Start the hero's reads before awaiting the session, so they overlap the /me
  // round-trip instead of following it. None of them needs the session object:
  // each fetcher reads the session id itself and returns an error Result for a
  // guest, which the redirect below discards. The promise cannot reject, since
  // every fetcher returns a Result. getMyProfile is cache()-wrapped, so
  // JobbResults reuses this read within the request.
  const heroDataPromise = Promise.all([
    getTaxonomyTree(),
    getRecentSearches(),
    getSavedJobAds(),
    getMyProfile(),
  ]);

  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const params = await searchParams;
  const page = parsePositiveInt(params.page, 1);
  const pageSize = Math.min(
    parsePositiveInt(params.pageSize, DEFAULT_PAGE_SIZE),
    100
  );
  const sortBy = parseSortBy(params.sortBy);
  const occupationGroup = toStringList(params.occupationGroup);
  const region = toStringList(params.region);
  const municipality = toStringList(params.municipality);
  const employmentType = toStringList(params.employmentType);
  const worktimeExtent = toStringList(params.worktimeExtent);
  // Only the grades the list can filter on are kept: unknown values and Top are
  // dropped silently (the backend would answer 400), and duplicates are removed.
  // An empty list means no grade filter.
  const matchGrades = [
    ...new Set(toStringList(params.matchGrades).filter(isListMatchGrade)),
  ];
  // Only the flags are parsed here. JobbResults derives whether matching is
  // active, and gates the matching-only flags on it.
  const matchningOff = params.matchning === MATCHNING_OFF_VALUE;
  const includeRelated = params.relaterade === RELATERADE_ON_VALUE;
  const hideApplied = params.doljAnsokta === STATUS_ON_VALUE;
  const onlyMatched = params.baraMatchade === STATUS_ON_VALUE;
  // A place facet: the Swedish `?distans=on` becomes the API's `remote: true`.
  const remote = params.distans === DISTANS_ON_VALUE;
  // buildPageHref uses the same parser, so the parsed page and its pagination
  // links cannot diverge.
  const employer = parseEmployerParam(params.employer);
  // parseQParam takes the first value of a repeated q, trims it, and treats text
  // shorter than the backend's minimum as no text, as the backend's own
  // SearchQueryParser does. Otherwise `?q=a` from a bookmark or a no-JS submit
  // would fail validation, and the hero would carry the invalid q into every
  // later search. buildPageHref shares the parser.
  const q = parseQParam(params.q);
  // StripCommitParam removes the flag after mount, so a shared link does not save
  // the search again.
  const commit = params.commit === "true";

  // These reads feed the hero, which renders outside the Suspense boundary, so
  // only the results area is replaced by a skeleton during a search. A successful
  // profile read means the user has a job-seeker profile:
  // hasStatedDesiredOccupation gates the matching control and hasSeeker gates
  // "Dölj ansökta", as the backend does. An error hides both controls.
  const [taxonomyResult, recentSearchesResult, savedJobAdsResult, profileResult] =
    await heroDataPromise;
  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" && profileResult.data.hasStatedDesiredOccupation;
  const hasSeeker = profileResult.kind === "ok";

  // On a read error the recent-searches and saved-ads chips fall back to their
  // empty state rather than showing an error (ADR 0060).
  const recentSearches =
    recentSearchesResult.kind === "ok" ? recentSearchesResult.data : [];

  const savedJobAds =
    savedJobAdsResult.kind === "ok" ? savedJobAdsResult.data : [];

  // A failed taxonomy read must not block search: the filter popover then shows
  // an empty list with an explanatory line (ADR 0043 Beslut B).
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;

  // The Suspense key identifies the search. It changes whenever any input to the
  // result list changes, so the skeleton also shows when navigating from one
  // /jobb URL to another; with an unchanged key React would keep the previous
  // results on screen while the next search loads.
  const resultsKey = new URLSearchParams(
    Object.entries({
      page: params.page ?? "",
      pageSize: params.pageSize ?? "",
      sortBy: params.sortBy ?? "",
      q: q ?? "",
    })
  ).toString();
  const occupationGroupKey = occupationGroup.join(",");
  const regionKey = region.join(",");
  const municipalityKey = municipality.join(",");
  const employmentTypeKey = employmentType.join(",");
  const worktimeExtentKey = worktimeExtent.join(",");
  const matchGradesKey = matchGrades.join(",");
  const matchningKey = matchningOff ? "off" : "";
  const relateradeKey = includeRelated ? "on" : "";
  const statusKey = hideApplied ? "h" : "";
  const onlyMatchedKey = onlyMatched ? "m" : "";
  const remoteKey = remote ? "d" : "";
  const employerKey = employer.join(".");

  return (
    <>
      {/* Removes `?commit=true` after mount; renders nothing. */}
      <StripCommitParam active={commit} />
      {/* The hero banner (ADR 0068) renders outside the Suspense boundary and
          stays visible while results load. */}
      <section className="jp-hero">
        <div className="jp-hero__inner">
          <div className="jp-hero__plate">
            <div>
              <h1 className="jp-hero__title">{t("jobb.title")}</h1>
              <p className="jp-hero__lede">{t("jobb.lede")}</p>
            </div>

            <div className="jp-hero__panel">
              <div className="jp-hero__actions">
                <RecentSearchesHeroChip items={recentSearches} />
                <SavedJobAdsHeroChip items={savedJobAds} />
              </div>

              {/* Typeahead search (ADR 0067): a taxonomy suggestion becomes a
                  filter chip and free text becomes q. Without JavaScript it is a
                  plain GET form to /jobb whose hidden inputs carry the active
                  filters. The label above the field carries the instruction, so
                  there is no placeholder. */}
              <JobbHeroSearch
                taxonomy={taxonomy}
                q={q ?? ""}
                occupationGroup={occupationGroup}
                region={region}
                municipality={municipality}
                remote={remote}
                employmentType={employmentType}
                worktimeExtent={worktimeExtent}
                matchGrades={matchGrades}
                employer={employer}
                sortBy={sortBy}
                pageSize={params.pageSize}
                initialCommitted={commit}
              />

              {/* Filter pills with Platsbanken-style popovers (ADR 0055). Every
                  change navigates at once with router.push in a transition. */}
              <JobbHeroFilters
                taxonomy={taxonomy}
                initialOccupationGroup={occupationGroup}
                initialRegion={region}
                initialMunicipality={municipality}
                initialRemote={remote}
                initialEmploymentType={employmentType}
                initialWorktimeExtent={worktimeExtent}
                initialMatchGrades={matchGrades}
                initialMatchningOff={matchningOff}
                initialIncludeRelated={includeRelated}
                initialHideApplied={hideApplied}
                initialOnlyMatched={onlyMatched}
                hasStatedDesiredOccupation={hasStatedDesiredOccupation}
                hasSeeker={hasSeeker}
                q={q ?? ""}
                employer={employer}
                sortBy={sortBy}
                pageSize={params.pageSize}
              />
            </div>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* #1395 — the results region's label. Three things bind it HERE, outside the
            Suspense boundary, and each breaks silently if it moves inside:
              - `loading.tsx` reserves a band the height of this heading. Inside the boundary
                the in-page fallback would need one too.
              - the section wraps JobbResults' error and rateLimited branches, so those stay
                labelled. `foretag-sok-results.tsx` suppresses its own h2 in the empty state
                and loses the region's name exactly where structure helps most; this cannot.
              - the heading takes no data, which is what lets it render with the hero at all.
                A count is data-bound — hence the invariance rule this inherits from
                `foretag-sok-results.tsx`, which here is structural and not only editorial.
            `jp-h2` and not `text-h2` for the reason written at that same file. */}
        <section aria-labelledby="jobb-results-title">
          <h2 id="jobb-results-title" className="jp-h2">
            {t("jobb.resultsHeading")}
          </h2>

          {/* #1505 — the load-cycle region, HERE: inside the section so it is part of the
              labelled results area, and outside the boundary below so it is not swapped with
              the subtree that writes to it. A region re-created with each search is one an
              assistive technology has not registered yet, which is the whole defect. The
              skeleton and every JobbResults branch push their sentence into it.
              Deliberately NOT the hero search's own region (`jobb-hero-search.tsx`): that one
              announces filter and tag commits. Two regions with two jobs, never one region
              with two writers. */}
          <Announcer>
          <Suspense
            key={`${resultsKey}|${occupationGroupKey}|${regionKey}|${municipalityKey}|${employmentTypeKey}|${worktimeExtentKey}|${matchGradesKey}|${matchningKey}|${relateradeKey}|${statusKey}|${onlyMatchedKey}|${remoteKey}|${employerKey}`}
            fallback={<JobAdListSkeleton />}
          >
            <JobbResults
              page={page}
              pageSize={pageSize}
              sortBy={sortBy}
              occupationGroup={occupationGroup}
              region={region}
              municipality={municipality}
              remote={remote}
              employmentType={employmentType}
              worktimeExtent={worktimeExtent}
              matchGrades={matchGrades}
              matchningOff={matchningOff}
              includeRelated={includeRelated}
              hideApplied={hideApplied}
              onlyMatched={onlyMatched}
              employer={employer}
              q={q ?? ""}
              commit={commit}
              rawParams={params}
            />
          </Suspense>
          </Announcer>
        </section>
      </div>
      <PageFeedback pageKey="jobs" />
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

