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

interface OversiktPageProps {
  readonly profile: ApiResult<JobSeekerProfileDto>;
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
  readonly savedJobAds: ApiResult<ListSavedJobAdsResult>;
  readonly recentSearches: ApiResult<ListRecentSearchesResult>;
  /**
   * ADR 0079 STEG 6 — live match-count (Bra + Stark) för Matchning-kortet och matchnings-notisen.
   * `number` = backend-svar (`count`, kan vara 0 = honest nollstate). `null` = fetch:en
   * degraderade (nätverk/auth/rate-limit) ⇒ kortet visar en en-dash utan CTA och notisen
   * utelämnas (aldrig en mock-fallback). Renderas bara när profilen har angett ett yrke
   * (`hasStatedDesiredOccupation`); annars äger setup-läget kortet.
   */
  readonly matchCount: number | null;
  /**
   * Bevakning F2 (#801, RF-6=6B) — antalet nya annonser från bevakade företag
   * NYA sedan senaste /foretag-besök (live `GET /me/followed-company-ads/new-count`,
   * per-watch grad-filtrerat read-time). Driver Företagsbevaknings-notisen (#726) och kortets
   * "N nya"-pill; `0` ⇒ båda utelämnas (honest tomt-läge). Degraderar till `0` vid fetch-fel.
   */
  readonly newFollowedCompanyAdCount: number;
  /**
   * #1558 — de bevakade företagen, som Result. Driver Bevakade företag-kortet. Ett Result och
   * inte en array: kortet måste kunna skilja noll bevakningar från en hämtning som föll, och
   * bara ett Result bär den skillnaden.
   */
  readonly companyWatches: ApiResult<ListCompanyWatchesResult>;
  /**
   * #1681 del 3 — branschbevakningarna, som Result och av samma skäl som `companyWatches`:
   * bara ett Result skiljer "du har inga" från "listan kunde inte läsas". Varje rad bär de TVÅ
   * annonstal detaljsidan visar, i samma former (ADR 0139) — talen är alltså redan komponerade
   * när de kommer hit, och den här sidan räknar ingenting själv.
   */
  readonly criteria: ApiResult<ListCompanyWatchCriteriaResult>;
  /**
   * SCB-referensträdet, för radernas människoetikett. `null` = läsningen degraderade; rubriken
   * faller då till användarens egen etikett och därefter till den neutrala. En degraderad tabell
   * får ALDRIG blanka annonstalen — de är kortets poäng och beror inte på trädet.
   */
  readonly criterionReference: CriterionReference | null;
}

/**
 * Översikt-sidan som bento-dashboard (ADR 0140, #1723). Server Component (orkestratorn,
 * non-async — synkron next-intl-translator).
 *
 * Bygger notiserna som förut (#726) och delar dem på KIND: allt utom `info` är åtgärder och
 * går till Kräver dig, `info` går till Senaste händelser. De fyra stående tillstånden (ansökningar,
 * matchning, bevakade företag, branschbevakningar) är egna kort med ett tal och en CTA var.
 *
 * Degraderad fallback: ApiResult-fel på en enskild källa ger ett kort med en en-dash och
 * `unavailable`-text — aldrig en blank cell, aldrig en blank sida.
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
}: OversiktPageProps) {
  const t = useTranslations("oversikt");
  // Scoped translator for the relative-time helper (`formatDaysAgo`).
  const tRelativeTime = useTranslations("oversikt.relativeTime");
  // Recent-sökningens label bor i jobads-katalogen, inte i oversikt (#1430).
  const tRecentLabel = useTranslations("jobads.recent");
  const format = useFormatter();
  const codedName = useCodedTaxonomyName();
  const bold = (chunks: ReactNode) => <b>{chunks}</b>;
  // #1576 - the number itself is the way to the ads it counts. The destination runs the SAME
  // predicate as this count, so the two cannot disagree.
  const newAdsLink = (chunks: ReactNode) => (
    <Link href="/foretag/bevakade/nya" className="jp-countlink">
      {chunks}
    </Link>
  );
  const today = new Date();
  // Datum-suffix på notice-IDs så en dismissad notis återkommer. När unified
  // notification-port finns: byt slug+datum mot riktigt notificationId per
  // backend-instans.
  const dateSlug = swedishDateSlug(today);

  const pipelineData = pipeline.kind === "ok" ? pipeline.data : [];
  const allApps = flattenPipeline(pipelineData);

  const followUps = findFollowUpCandidates(allApps, today);
  const recentInterviews = findRecentInterviews(allApps, today);
  const latestOffer = findLatestOffer(allApps);

  // F4-12 PR-B (ADR 0076): setup-läge ↔ matchtal är ÖMSESIDIGT uteslutande, styrt av
  // `hasStatedDesiredOccupation`. Yrke angett → Matchning-kortet bär talet och match-notisen
  // renderas. Ej angett → kortet bär setup-callouten. Aldrig båda.
  const hasStatedOccupation =
    profile.kind === "ok" && profile.data.hasStatedDesiredOccupation;

  // Trust-invariant (harmoniserad 2026-07-03, CTO H2): länken bär EXAKT samma facetter som
  // backend-counten hård-filtrerar på och INGA matchGrades — /jobb-landningens TotalCount ==
  // kortets tal == notis-talet == setup-räknaren per konstruktion. Byggd EN gång och delad av
  // kortet och notisen, så de två inte kan peka på olika listor.
  const matchHref =
    profile.kind === "ok"
      ? buildJobbHref({
          q: "",
          occupationGroup: [...profile.data.preferredOccupationGroups],
          region: [...profile.data.preferredRegions],
          municipality: [...profile.data.preferredMunicipalities],
          // #551 punkt 4 — H2-invarianten ovan: GetMyMatchCountQueryHandler
          // hård-filtrerar på den persisterade PreferredRemote, så länken måste bära
          // samma axel. Utan den säger kortet N medan listan visar ett annat tal så
          // snart användaren sparat Distans.
          remote: profile.data.preferredRemote,
          employmentType: [...profile.data.preferredEmploymentTypes],
          worktimeExtent: [],
          matchGrades: [],
          sortBy: DEFAULT_SORT_BY,
        })
      : null;

  // ── Mina ansökningar ──────────────────────────────────────────────────────
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
        // #384 — talet läser samma SSOT som filter-tröskeln (ingen hårdkodad "14").
        days: OVERSIKT_FOLLOW_UP_DAYS,
        b: bold,
      }),
      cta: t("notices.followUpCta"),
      href: "/ansokningar",
      // MOCK: BE-port saknas för "när-noteringen-räknades-ut"-tidsstämpel.
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

  // ── Jobbannonser ──────────────────────────────────────────────────────────
  const jobAdNotices: SectionNoticeData[] = [];

  // Deadline-notis: nu RIKTIG `expiresAt` ur de sparade annonserna (#726),
  // ersätter den gamla mock-drivna "denna vecka"-notisen. Etiketterna är
  // FÖRETAGSNAMN (per skisserna), tidskolumnen den NÄRMASTE deadlinens datum.
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
        // SSOT: samma konstant som filtrets fönster (ingen hårdkodad "7").
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
      // MOCK: BE-port saknas för matchning-uppdaterings-stämpel; counten är live.
      time: t("notices.timeToday"),
    });
  }

  // Senaste-sökning-notis (#294, A′-relabel #726): featurar DIN SENASTE sökning
  // med replay-CTA. "Har N nya träffar"-counten hämtas lazy i SavedSearchNoticeText.
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

  // ── Företagsbevakning ─────────────────────────────────────────────────────
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
      // ADR 0140: the CTA now names the new ads and goes where the number in the text already
      // went — one destination for one notice.
      href: "/foretag/bevakade/nya",
      time: t("notices.timeToday"),
    });
  }

  const allNotices: SectionNoticeData[] = [
    ...applicationNotices,
    ...jobAdNotices,
    ...companyNotices,
  ];

  // Kind-splitten (ADR 0140 Beslut 5): allt utom `info` kräver något av läsaren — uppföljning,
  // deadline, erbjudande, intervju — och går till Kräver dig; `info` är händelser. Varje lista
  // behåller konstruktionsordningen.
  const actionNotices = allNotices.filter((n) => n.kind !== "info");
  const infoNotices = allNotices.filter((n) => n.kind === "info");

  // Kugghjuls-typer per källa, byggda ur NOTICE_TYPES-SSOT:en så popover-raderna aldrig kan
  // drifta från notisernas `type`-slugs (code-reviewer Minor 1). `Record<NoticeType, string>`
  // tvingar en label för VARJE typ — en ny typ utan label blir ett kompileringsfel. Inkluderar
  // förberedda typer utan notiser ännu ("Statusändringar", "Företagshändelser"). A′: sök-typen
  // heter "Senaste sökningen", inte "Sparade sökningar".
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
      {/* Page-hero utan aside (I dag-kortet borttaget, #726) — edge-to-edge
          navy/grön band per ADR 0068. */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("hero.title")}</h1>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* #384 — notiserna beräknas LIVE per request (force-dynamic), så
            "senast uppdaterad" är render-tiden, inte en stale mock-stämpel. */}
        <NoticeToolbar
          lastUpdated={formatNoticesStamp(format, today)}
          lastUpdatedIso={today.toISOString()}
          aside={<NoticePrefsPopover groups={prefGroups} notices={allNotices} />}
        />

        <div className="jp-ov-grid">
          <RequiresYouCard notices={actionNotices} />
          <ApplicationsCard pipeline={pipeline} />
          <MatchingCard
            matchCount={matchCount}
            matchHref={matchHref}
            hasStatedOccupation={hasStatedOccupation}
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

        {/* Sist på sidan, efter det den verkar på (#1557). */}
        <MarkAllReadRow notices={allNotices} />
      </div>
    </>
  );
}
