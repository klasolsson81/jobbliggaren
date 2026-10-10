import Link from "next/link";
import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { useCodedTaxonomyName } from "@/lib/i18n/use-coded-taxonomy-name";
import { swedishDateSlug } from "@/lib/time/swedish-calendar";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ListSavedJobAdsResult } from "@/lib/dto/saved-job-ads";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type { ListCompanyWatchesResult } from "@/lib/dto/company-follows";
import type {
  CriterionReference,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";
import {
  findFollowUpCandidates,
  findLatestOffer,
  findRecentInterviews,
  findUpcomingSavedJobDeadlines,
  flattenPipeline,
  formatDaysAgo,
  formatNoticesStamp,
  formatSwedishShortDate,
  OVERSIKT_DEADLINE_WINDOW_DAYS,
  OVERSIKT_FOLLOW_UP_DAYS,
} from "@/lib/oversikt/aggregations";
import { buildJobbHref, DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";
import { buildRecentSearchHref } from "@/lib/job-ads/recent-search-href";
import {
  buildRecentSearchLabel,
  recentSearchLabelCopy,
} from "@/lib/job-ads/recent-search-label";
import { ApplicationsCard } from "./applications-card";
import { CompaniesCard } from "./companies-card";
import { CriteriaCard, criteriaCardIsWide } from "./criteria-card";
import { MarkAllReadRow } from "./mark-all-read-row";
import { MatchingCard } from "./matching-card";
import {
  NoticePrefsPopover,
  type NoticePrefGroup,
  type NoticePrefType,
} from "./notice-prefs-popover";
import { NoticeToolbar } from "./notice-toolbar";
import {
  NOTICE_TYPES,
  type NoticeSource,
  type NoticeType,
  type SectionNoticeData,
} from "./notice-types";
import { RecentEventsCard } from "./recent-events-card";
import { RequiresYouCard } from "./requires-you-card";
import { SavedSearchNoticeText } from "./saved-search-notice-text";
import { getSetupState } from "@/lib/onboarding/setup-state";

interface OversiktPageProps {
  readonly profile: ApiResult<JobSeekerProfileDto>;
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
  readonly savedJobAds: ApiResult<ListSavedJobAdsResult>;
  readonly recentSearches: ApiResult<ListRecentSearchesResult>;
  /**
   * The live count for the matching card and notice. Zero is a real answer. `null` means
   * the read failed: the card shows an en dash without a link and the notice is left out.
   * Used only once the user has stated an occupation; until then the card shows the setup
   * prompt.
   */
  readonly matchCount: number | null;
  /**
   * New ads from followed companies since the user's last visit to /foretag. Drives the
   * company notice and the card's "N nya" pill; at 0 both are left out. A failed read
   * counts as 0.
   */
  readonly newFollowedCompanyAdCount: number;
  /**
   * The followed companies, as a Result rather than an array: the card must tell "none"
   * apart from "could not be read".
   */
  readonly companyWatches: ApiResult<ListCompanyWatchesResult>;
  /**
   * The industry watches, as a Result for the same reason as `companyWatches`. Each row
   * already carries the two ad counts its detail page shows (ADR 0139); this page counts
   * nothing itself.
   */
  readonly criteria: ApiResult<ListCompanyWatchCriteriaResult>;
  /**
   * The SCB reference tree, for each row's readable name. `null` means the read failed: the
   * heading then falls back to the user's own label and then to a neutral one. The ad counts
   * do not depend on the tree and are always shown.
   */
  readonly criterionReference: CriterionReference | null;
  readonly setupUnavailable?: boolean;
}

/**
 * The overview page as a dashboard of cards (ADR 0140). A synchronous Server Component that
 * receives every read from the route.
 *
 * Notices are split by kind: everything except `info` asks something of the user and goes
 * to "Kräver åtgärd"; `info` goes to "Senaste händelser". Applications, matching, followed
 * companies and industry watches each have their own card with a number and a link.
 *
 * A failed read for one source gives that card an en dash and an "unavailable" line, never a
 * blank cell or a blank page.
 */
export function OversiktPage({
  profile,
  pipeline,
  savedJobAds,
  recentSearches,
  matchCount,
  newFollowedCompanyAdCount,
  companyWatches,
  criteria,
  criterionReference,
  setupUnavailable,
}: OversiktPageProps) {
  const t = useTranslations("oversikt");
  // Scoped translator for the relative-time helper (`formatDaysAgo`).
  const tRelativeTime = useTranslations("oversikt.relativeTime");
  // The recent-search label lives in the jobads catalogue, not in oversikt.
  const tRecentLabel = useTranslations("jobads.recent");
  const format = useFormatter();
  const codedName = useCodedTaxonomyName();
  const bold = (chunks: ReactNode) => <b>{chunks}</b>;
  // The number itself links to the ads it counts. The destination runs the same predicate
  // as the count, so the two cannot disagree.
  const newAdsLink = (chunks: ReactNode) => (
    <Link href="/foretag/bevakade/nya" className="jp-countlink">
      {chunks}
    </Link>
  );
  const today = new Date();
  // Notice ids are a slug plus the date, so a dismissed notice comes back the next day.
  const dateSlug = swedishDateSlug(today);

  const pipelineData = pipeline.kind === "ok" ? pipeline.data : [];
  const allApps = flattenPipeline(pipelineData);

  const followUps = findFollowUpCandidates(allApps, today);
  const recentInterviews = findRecentInterviews(allApps, today);
  const latestOffer = findLatestOffer(allApps);

  // The setup prompt and the match count are mutually exclusive (ADR 0076): with a stated
  // occupation the matching card shows the count and the match notice renders; without one
  // the card shows the setup prompt.
  const setupState = getSetupState(profile);
  const hasStatedOccupation = setupState === "configured";

  // The link carries exactly the facets the backend count filters on, and no match grades,
  // so the hit count on /jobb equals the card's number and the notice's number by
  // construction. It is built once and shared, so the card and the notice cannot point at
  // different lists.
  const matchHref =
    profile.kind === "ok"
      ? buildJobbHref({
          q: "",
          occupationGroup: [...profile.data.preferredOccupationGroups],
          region: [...profile.data.preferredRegions],
          municipality: [...profile.data.preferredMunicipalities],
          // GetMyMatchCountQueryHandler filters on the saved remote preference, so the
          // link must carry it too, or the card and the list would disagree.
          remote: profile.data.preferredRemote,
          employmentType: [...profile.data.preferredEmploymentTypes],
          worktimeExtent: [],
          matchGrades: [],
          sortBy: DEFAULT_SORT_BY,
        })
      : null;

  // ── Applications ─────────────────────────────────────────────────────────
  const applicationNotices: SectionNoticeData[] = [];

  if (followUps.length > 0) {
    applicationNotices.push({
      id: `n-followup-${dateSlug}`,
      source: "applications",
      type: "followup",
      kind: "warning",
      label: t("notices.followUpLabel"),
      text: t.rich("notices.followUpText", {
        count: followUps.length,
        days: OVERSIKT_FOLLOW_UP_DAYS,
        b: bold,
      }),
      cta: t("notices.followUpCta"),
      href: "/ansokningar",
      // The backend gives no time for when this was computed, so it reads "today".
      time: t("notices.timeToday"),
    });
  }

  if (latestOffer) {
    const offerCompany = latestOffer.jobAd?.company ?? t("notices.fallbackCompany");
    const offerTitle = latestOffer.jobAd?.title;
    applicationNotices.push({
      id: `n-offer-${dateSlug}`,
      source: "applications",
      type: "offers",
      kind: "success",
      label: t("notices.offerLabel"),
      text: offerTitle
        ? t.rich("notices.offerTextWithTitle", {
            company: offerCompany,
            title: offerTitle,
            b: bold,
          })
        : t.rich("notices.offerText", { company: offerCompany, b: bold }),
      cta: t("notices.offerCta"),
      href: "/ansokningar",
      time: formatDaysAgo(tRelativeTime, latestOffer.updatedAt, today),
    });
  }

  if (recentInterviews.length > 0) {
    const interview = recentInterviews[0]!;
    const interviewCompany =
      interview.jobAd?.company ?? t("notices.fallbackEmployer");
    applicationNotices.push({
      id: `n-interview-confirmed-${dateSlug}`,
      source: "applications",
      type: "interviews",
      kind: "brand",
      label: t("notices.interviewLabel"),
      text: t.rich("notices.interviewText", {
        company: interviewCompany,
        b: bold,
      }),
      cta: t("notices.interviewCta"),
      href: "/ansokningar",
      time: formatDaysAgo(tRelativeTime, interview.updatedAt, today),
    });
  }

  // ── Job ads ──────────────────────────────────────────────────────────────
  const jobAdNotices: SectionNoticeData[] = [];

  // Upcoming deadlines among saved ads: labelled by company name, timed by the nearest
  // deadline.
  const savedJobAdsData = savedJobAds.kind === "ok" ? savedJobAds.data : [];
  const deadlines = findUpcomingSavedJobDeadlines(savedJobAdsData, today);
  if (deadlines.length > 0) {
    const labels = deadlines.map((d) => d.company).join(", ");
    jobAdNotices.push({
      id: `n-deadline-${dateSlug}`,
      source: "jobads",
      type: "deadlines",
      kind: "warning",
      label: t("notices.deadlineLabel"),
      text: t.rich("notices.deadlineText", {
        count: deadlines.length,
        days: OVERSIKT_DEADLINE_WINDOW_DAYS,
        labels,
        b: bold,
      }),
      cta: t("notices.deadlineCta"),
      href: "/sparade",
      time: formatSwedishShortDate(deadlines[0]!.expiresAt),
    });
  }

  if (hasStatedOccupation && matchCount !== null && matchHref !== null) {
    jobAdNotices.push({
      id: `n-match-${dateSlug}`,
      source: "jobads",
      type: "matches",
      kind: "info",
      label: t("notices.matchLabel"),
      text:
        matchCount > 0
          ? t.rich("notices.matchText", { count: matchCount, b: bold })
          : t("notices.matchTextZero"),
      cta: t("notices.matchCta"),
      href: matchHref,
      // The count is live, but the backend gives no time for it, so it reads "today".
      time: t("notices.timeToday"),
    });
  }

  // The user's most recent search, with a link to run it again. Its "N new hits" count is
  // fetched lazily by SavedSearchNoticeText.
  const recentSearchesData =
    recentSearches.kind === "ok" ? recentSearches.data : [];
  const lastSearch =
    recentSearchesData.length > 0
      ? [...recentSearchesData].sort((a, b) =>
          b.lastViewedAt.localeCompare(a.lastViewedAt),
        )[0]
      : null;
  if (lastSearch) {
    jobAdNotices.push({
      id: `n-saved-search-${dateSlug}`,
      source: "jobads",
      type: "latestsearch",
      kind: "info",
      label: t("notices.savedSearchLabel"),
      text: (
        <SavedSearchNoticeText
          searchId={lastSearch.id}
          name={buildRecentSearchLabel(lastSearch.label, recentSearchLabelCopy(tRecentLabel, codedName))}
        />
      ),
      cta: t("notices.savedSearchCta"),
      href: buildRecentSearchHref(lastSearch),
      time: formatDaysAgo(tRelativeTime, lastSearch.lastViewedAt, today),
    });
  }

  // ── Followed companies ───────────────────────────────────────────────────
  const companyNotices: SectionNoticeData[] = [];
  if (newFollowedCompanyAdCount > 0) {
    companyNotices.push({
      id: `n-followed-ads-${dateSlug}`,
      source: "companies",
      type: "followedads",
      kind: "info",
      label: t("notices.companiesLabel"),
      text: t.rich("notices.companiesText", {
        count: newFollowedCompanyAdCount,
        b: bold,
        lnk: newAdsLink,
      }),
      cta: t("notices.companiesCta"),
      // One destination for the notice: the link and the number go to the same list (ADR 0140).
      href: "/foretag/bevakade/nya",
      time: t("notices.timeToday"),
    });
  }

  const allNotices: SectionNoticeData[] = [
    ...applicationNotices,
    ...jobAdNotices,
    ...companyNotices,
  ];

  // Split by kind (ADR 0140 Beslut 5). Each list keeps the order the notices were built in.
  const actionNotices = allNotices.filter((n) => n.kind !== "info");
  const infoNotices = allNotices.filter((n) => n.kind === "info");

  // The settings popover lists its types from NOTICE_TYPES, so its rows cannot drift from the
  // notices' `type` slugs. `Record<NoticeType, string>` requires a label for every type, so a
  // new type without one fails to compile. Some types have no notices yet.
  const prefLabels: Record<NoticeType, string> = {
    followup: t("notices.prefFollowup"),
    interviews: t("notices.prefInterviews"),
    offers: t("notices.prefOffers"),
    statuschanges: t("notices.prefStatusChanges"),
    deadlines: t("notices.prefDeadlines"),
    matches: t("notices.prefMatches"),
    latestsearch: t("notices.prefLatestSearch"),
    followedads: t("notices.prefFollowedAds"),
    companyevents: t("notices.prefCompanyEvents"),
  };
  const prefTypesFor = (source: NoticeSource): NoticePrefType[] =>
    NOTICE_TYPES[source].map((id) => ({ id, label: prefLabels[id] }));
  // ONE gear for the page (ADR 0140 Beslut 4), the nine types grouped under the source names
  // the three sections used to carry.
  const prefGroups: NoticePrefGroup[] = [
    { source: "applications", title: t("notices.sectionApplications"), types: prefTypesFor("applications") },
    { source: "jobads", title: t("notices.sectionJobAds"), types: prefTypesFor("jobads") },
    { source: "companies", title: t("notices.sectionCompanies"), types: prefTypesFor("companies") },
  ];

  // Two or more industry watches reflow the Branschbevakning card to a full row, and its two
  // siblings widen to keep the row even. One expression, read here for the siblings and inside
  // the card for itself.
  const siblingSpan = criteriaCardIsWide(criteria) ? 6 : 4;

  return (
    <>
      {/* Full-width page hero (ADR 0068). */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("hero.title")}</h1>
            <p className="jp-pagehero__lede">{t("hero.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* Notices are computed on every request, so "last updated" is the render time. */}
        <NoticeToolbar
          lastUpdated={formatNoticesStamp(format, today)}
          lastUpdatedIso={today.toISOString()}
          aside={<NoticePrefsPopover groups={prefGroups} notices={allNotices} />}
        />

        <div className="jp-ov-grid">
          <RequiresYouCard notices={actionNotices} needsSetup={setupState === "incomplete"} setupUnavailable={setupUnavailable} />
          <ApplicationsCard pipeline={pipeline} />
          <MatchingCard
            matchCount={matchCount}
            matchHref={matchHref}
            setupState={setupState}
            span={siblingSpan}
          />
          <CompaniesCard
            watches={companyWatches}
            newAdCount={newFollowedCompanyAdCount}
            span={siblingSpan}
          />
          <CriteriaCard criteria={criteria} reference={criterionReference} />
          <RecentEventsCard notices={infoNotices} />
        </div>

        {/* Last on the page, after the notices it acts on. */}
        <MarkAllReadRow notices={allNotices} />
      </div>
    </>
  );
}
