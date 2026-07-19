import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ListSavedJobAdsResult } from "@/lib/dto/saved-job-ads";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
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
import { SavedSearchNoticeText } from "./saved-search-notice-text";
import { SetupCallout } from "./setup-callout";
import { NoticeToolbar } from "./notice-toolbar";
import {
  NOTICE_TYPES,
  NoticeSection,
  type NoticePrefType,
  type NoticeSource,
  type NoticeType,
  type SectionNoticeData,
} from "./notice-section";

interface OversiktPageProps {
  readonly email: string;
  readonly displayName: string | null;
  readonly profile: ApiResult<JobSeekerProfileDto>;
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
  readonly savedJobAds: ApiResult<ListSavedJobAdsResult>;
  readonly recentSearches: ApiResult<ListRecentSearchesResult>;
  /**
   * ADR 0079 STEG 6 — live match-count (Bra + Stark) för matchnings-notisen.
   * `number` = backend-svar (`count`, kan vara 0 = honest nollstate). `null` =
   * fetch:en degraderade (nätverk/auth/rate-limit) ⇒ notisen utelämnas helt
   * (aldrig en mock-fallback). Notisen renderas bara när profilen har angett ett
   * yrke (`hasStatedDesiredOccupation`); annars äger setup-kortet slotten.
   */
  readonly matchCount: number | null;
  /**
   * Bevakning F2 (#801, RF-6=6B) — antalet nya annonser från bevakade företag
   * NYA sedan senaste /foretag-besök (live `GET /me/followed-company-ads/new-count`,
   * per-watch grad-filtrerat read-time). Driver Företagsbevaknings-notisen (#726);
   * `0` ⇒ notisen utelämnas (honest tomt-läge). Degraderar till `0` vid fetch-fel.
   */
  readonly newFollowedCompanyAdCount: number;
}

/**
 * F6 P5 Punkt 4 — Översikt-sidan, omgjord till NOTISCENTER (#726). Server
 * Component (orkestratorn, non-async — synkron next-intl-translator).
 *
 * Bygger notiser grupperade per KÄLLA (Mina ansökningar / Jobbannonser /
 * Företagsbevakning) i stället för de globala "Kräver åtgärd"/"Information"-
 * grupperna, plus ett "Kräver åtgärd"-kort för matchnings-setup. Sammanfattningen
 * och I dag-kortet är borttagna (guest-only-ytorna behåller sina kopior).
 *
 * Degraderad fallback: ApiResult-fel på en enskild källa ger en tom sektion
 * (tomt-läge), aldrig en blank sida.
 */
export function OversiktPage({
  email,
  displayName,
  profile,
  pipeline,
  savedJobAds,
  recentSearches,
  matchCount,
  newFollowedCompanyAdCount,
}: OversiktPageProps) {
  const t = useTranslations("oversikt");
  // Scoped translator for the relative-time helper (`formatDaysAgo`).
  const tRelativeTime = useTranslations("oversikt.relativeTime");
  const bold = (chunks: ReactNode) => <b>{chunks}</b>;
  const today = new Date();
  // Datum-suffix på notice-IDs så en dismissad notis återkommer när data ändras
  // (nästa dag = ny render). När unified notification-port finns: byt slug+datum
  // mot riktigt notificationId per backend-instans.
  const dateSlug = today.toISOString().slice(0, 10);
  const kickerName =
    displayName && displayName.trim().length > 0
      ? displayName
      : (email.split("@")[0] ?? email);

  const pipelineData = pipeline.kind === "ok" ? pipeline.data : [];
  const allApps = flattenPipeline(pipelineData);

  const followUps = findFollowUpCandidates(allApps, today);
  const recentInterviews = findRecentInterviews(allApps, today);
  const latestOffer = findLatestOffer(allApps);

  // F4-12 PR-B (ADR 0076): setup-kort ↔ match-notis är ÖMSESIDIGT uteslutande,
  // styrt av `hasStatedDesiredOccupation`. Yrke angett → match-notis (live count).
  // Ej angett → setup-kortet. Aldrig båda.
  const hasStatedOccupation =
    profile.kind === "ok" && profile.data.hasStatedDesiredOccupation;

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

  if (hasStatedOccupation && matchCount !== null && profile.kind === "ok") {
    // Trust-invariant (harmoniserad 2026-07-03, CTO H2): länken bär EXAKT samma
    // facetter som backend-counten hård-filtrerar på och INGA matchGrades —
    // /jobb-landningens TotalCount == notis-talet == setup-räknaren per konstruktion.
    const matchHref = buildJobbHref({
      q: "",
      occupationGroup: [...profile.data.preferredOccupationGroups],
      region: [...profile.data.preferredRegions],
      municipality: [...profile.data.preferredMunicipalities],
      employmentType: [...profile.data.preferredEmploymentTypes],
      worktimeExtent: [],
      matchGrades: [],
      sortBy: DEFAULT_SORT_BY,
    });
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
        <SavedSearchNoticeText searchId={lastSearch.id} name={lastSearch.label} />
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
      }),
      cta: t("notices.companiesCta"),
      href: "/foretag",
      time: t("notices.timeToday"),
    });
  }

  const allNotices: SectionNoticeData[] = [
    ...applicationNotices,
    ...jobAdNotices,
    ...companyNotices,
  ];

  // Kugghjuls-typer per sektion, byggda ur NOTICE_TYPES-SSOT:en så popover-
  // raderna aldrig kan drifta från notisernas `type`-slugs (code-reviewer
  // Minor 1). `Record<NoticeType, string>` tvingar en label för VARJE typ —
  // en ny typ utan label blir ett kompileringsfel. Inkluderar förberedda typer
  // utan notiser ännu ("Statusändringar", "Företagshändelser"). A′: sök-typen
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

  return (
    <>
      {/* Page-hero utan aside (I dag-kortet borttaget, #726) — edge-to-edge
          navy/grön band per ADR 0068. */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">
              {t("hero.kicker", { name: kickerName })}
            </div>
            <h1 className="jp-pagehero__title">{t("hero.title")}</h1>
            <p className="jp-pagehero__lede">{t("hero.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* #384 — notiserna beräknas LIVE per request (force-dynamic), så
            "senast uppdaterad" är render-tiden, inte en stale mock-stämpel. */}
        <NoticeToolbar
          lastUpdated={formatNoticesStamp(today)}
          notices={allNotices}
        />

        {!hasStatedOccupation && <SetupCallout />}

        <NoticeSection
          source="applications"
          titleId="oversikt-applications"
          title={t("notices.sectionApplications")}
          notices={applicationNotices}
          emptyBody={t("notices.emptyApplications")}
          prefTypes={prefTypesFor("applications")}
        />
        <NoticeSection
          source="jobads"
          titleId="oversikt-jobads"
          title={t("notices.sectionJobAds")}
          notices={jobAdNotices}
          emptyBody={t("notices.emptyJobAds")}
          prefTypes={prefTypesFor("jobads")}
        />
        <NoticeSection
          source="companies"
          titleId="oversikt-companies"
          title={t("notices.sectionCompanies")}
          notices={companyNotices}
          emptyBody={t("notices.emptyCompanies")}
          prefTypes={prefTypesFor("companies")}
        />
      </div>
    </>
  );
}
