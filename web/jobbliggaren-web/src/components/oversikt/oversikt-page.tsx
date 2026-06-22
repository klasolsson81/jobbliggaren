import { useTranslations } from "next-intl";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ListSavedJobAdsResult } from "@/lib/dto/saved-job-ads";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type { GetResumesResult } from "@/lib/dto/resumes";
import type { LandingStatsDto } from "@/lib/dto/landing";
import {
  computeApplicationCounts,
  daysSince,
  filterFutureDeadlines,
  findFollowUpCandidates,
  findLatestOffer,
  findRecentInterviews,
  flattenPipeline,
  formatDaysAgo,
  formatSwedishShortDate,
} from "@/lib/oversikt/aggregations";
import { OVERSIKT_MOCK } from "@/lib/oversikt/mock-data";
import { TodayCard } from "./today-card";
import { NoticeList } from "./notice-list";
import { Summary } from "./summary";
import type { NoticeData } from "./notice-row";

interface OversiktPageProps {
  readonly email: string;
  readonly displayName: string | null;
  readonly profile: ApiResult<JobSeekerProfileDto>;
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
  readonly savedJobAds: ApiResult<ListSavedJobAdsResult>;
  readonly recentSearches: ApiResult<ListRecentSearchesResult>;
  readonly resumes: ApiResult<GetResumesResult>;
  /**
   * Landing-stats per CTO svans-PR2-dom (agentId ad37955db80099f19) —
   * ersatte tidigare `jobAds` prop. Worker-precomputed Redis-cache
   * (ADR 0064), 0-1ms read vs ListJobAdsQuery p50 ~1.2s. Samma
   * `activeCount` som HeaderStats renderar → ingen 28 vs 9 mismatch.
   */
  readonly landingStats: LandingStatsDto | null;
}

/**
 * F6 P5 Punkt 4 — Översikt-sidan. Server Component (orkestratorn).
 *
 * Per CTO-dom 2026-05-24 (Variant A): direkt RSC `Promise.all` mot 5-6
 * befintliga endpoints, ingen ny composer-endpoint, ingen Worker-cache
 * (per-user-data, ej publik anonym).
 *
 * Degraderad fallback: ApiResult-fel på enskild källa ger "—" eller mock-
 * default i sin sektion, men låter resten rendra. Aldrig blank sida pga
 * enskild endpoint-failure.
 */
export function OversiktPage({
  email,
  displayName,
  profile,
  pipeline,
  savedJobAds,
  recentSearches,
  resumes,
  landingStats,
}: OversiktPageProps) {
  // Synchronous next-intl translator — keeps OversiktPage a non-async RSC.
  const t = useTranslations("oversikt");
  // Scoped translator for the relative-time helper (`formatDaysAgo`), which is
  // a pure helper and receives `t` as a param.
  const tRelativeTime = useTranslations("oversikt.relativeTime");
  const today = new Date();
  // Klas svans-PR2 Variant A: datum-suffix på notice-IDs så dismissad notis
  // återkommer när data ändras (nästa dag = ny render av "143 nya annonser").
  // Permanent-dismiss-defekten i PR1 (Klas-feedback #2 2026-05-24) löst.
  // När unified notification-port finns: ersätt slug+datum med riktigt
  // notificationId per backend-instans.
  const dateSlug = today.toISOString().slice(0, 10);
  const kickerName =
    displayName && displayName.trim().length > 0
      ? displayName
      : (email.split("@")[0] ?? email);

  // Pipeline → counts + apps (för datum-filtrerade notiser)
  const pipelineData = pipeline.kind === "ok" ? pipeline.data : [];
  const counts = computeApplicationCounts(pipelineData);
  const allApps = flattenPipeline(pipelineData);

  // BE-driven notiser
  const followUps = findFollowUpCandidates(allApps, today);
  const recentInterviews = findRecentInterviews(allApps, today);
  const latestOffer = findLatestOffer(allApps);

  // Notice-konstruktion: BE-driven först, mock som copy-template för fält
  // utan BE-port. Tom-state ⇒ notis exkluderas (HANDOVER §3.3).
  //
  // design-reviewer M3 (2026-05-24): notice-text använder ApplicationDto-
  // data direkt (jobAd?.company / .title / .updatedAt) — inte mock-företags-
  // namn — för att inte vilseleda användaren ("Bonnier News" när hen har
  // erbjudande från Skatteverket). Mock används bara för fält där BE-port
  // saknas (deadline-copy, dateCopy).
  const actionNotices: NoticeData[] = [];

  if (latestOffer) {
    const offerCompany =
      latestOffer.jobAd?.company ?? t("notices.fallbackCompany");
    const offerTitle = latestOffer.jobAd?.title;
    actionNotices.push({
      id: `n-offer-${dateSlug}`,
      kind: "success",
      label: t("notices.offerLabel"),
      text: offerTitle
        ? t.rich("notices.offerTextWithTitle", {
            company: offerCompany,
            title: offerTitle,
            b: (chunks) => <b>{chunks}</b>,
          })
        : t.rich("notices.offerText", {
            company: offerCompany,
            b: (chunks) => <b>{chunks}</b>,
          }),
      cta: t("notices.offerCta"),
      href: "/ansokningar",
      time: formatDaysAgo(tRelativeTime, latestOffer.updatedAt, today),
    });
  }

  if (followUps.length > 0) {
    actionNotices.push({
      id: `n-followup-${dateSlug}`,
      kind: "warning",
      label: t("notices.followUpLabel"),
      text: t.rich("notices.followUpText", {
        count: followUps.length,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("notices.followUpCta"),
      href: "/ansokningar",
      // MOCK: BE-port saknas för "när-noteringen-räknades-ut"-tidsstämpel
      time: t("notices.timeToday"),
    });
  }

  // Deadline-notis: BE-port saknas (saved-job-ads har ingen deadline-yta).
  // Visa bara om vi har sparade annonser OCH framtida (ej passerade) mock-
  // deadlines kvar (code-reviewer M3 2026-05-24 — filterFutureDeadlines).
  const savedCount =
    savedJobAds.kind === "ok" ? savedJobAds.data.length : 0;
  const futureDeadlines = filterFutureDeadlines(
    OVERSIKT_MOCK.savedJobsDeadlines,
    today
  );
  if (savedCount > 0 && futureDeadlines.length > 0) {
    const labels = futureDeadlines.map((d) => d.label).join(", ");
    actionNotices.push({
      id: `n-deadline-${dateSlug}`,
      kind: "warning",
      label: t("notices.deadlineLabel"),
      text: t.rich("notices.deadlineText", {
        count: futureDeadlines.length,
        labels,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("notices.deadlineCta"),
      href: "/sparade",
      // MOCK: BE-port saknas för faktisk deadline-stämpel
      time: t("notices.timeThisWeek"),
    });
  }

  // F4-12 PR-B (ADR 0076): setup-nudge ↔ mock-match-notis är ÖMSESIDIGT
  // uteslutande och styrs av `hasStatedDesiredOccupation`. Du kan inte ha
  // "annonser som matchar din profil" innan en profil finns — det vore
  // ohederligt. Yrke angett → mock-match-notis. Ej angett → setup-nudge.
  // Aldrig båda (undviker två motsägande "Matchning"-rader).
  const hasStatedOccupation =
    profile.kind === "ok" && profile.data.hasStatedDesiredOccupation;

  const infoNotices: NoticeData[] = [];

  if (!hasStatedOccupation) {
    // Persistent, icke-avfärdbar nudge — stabilt id UTAN dateSlug (ska bestå
    // tills användaren angett ett yrke, inte återkomma per dag). Tom `time`
    // → NoticeRow renderar ingen tids-span.
    infoNotices.push({
      id: "n-setup-match",
      kind: "info",
      dismissible: false,
      label: t("notices.matchLabel"),
      // Verbatim SPOT med jobads.ui.match.noStatedOccupation/settingsCta —
      // samma copy som /jobb-disclosuren och match-sektionen (ingen drift).
      text: t("notices.setupText"),
      cta: t("notices.setupCta"),
      href: "/installningar#matchning",
      time: "",
    });
  } else {
    infoNotices.push({
      id: `n-match-${dateSlug}`,
      kind: "info",
      label: t("notices.matchLabel"),
      text: t.rich("notices.matchText", {
        count: OVERSIKT_MOCK.matchCountThisWeek,
        segment: OVERSIKT_MOCK.matchSegmentLabel,
        b: (chunks) => <b>{chunks}</b>,
        em: (chunks) => <em>{chunks}</em>,
      }),
      cta: t("notices.matchCta"),
      href: "/jobb",
      // MOCK: BE-port saknas för matchning-uppdaterings-stämpel
      time: t("notices.timeToday"),
    });
  }

  if (recentInterviews.length > 0) {
    const interview = recentInterviews[0]!;
    const interviewCompany =
      interview.jobAd?.company ?? t("notices.fallbackEmployer");
    infoNotices.push({
      id: `n-interview-confirmed-${dateSlug}`,
      kind: "brand",
      label: t("notices.interviewLabel"),
      text: t.rich("notices.interviewText", {
        company: interviewCompany,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("notices.interviewCta"),
      href: "/ansokningar",
      time: formatDaysAgo(tRelativeTime, interview.updatedAt, today),
    });
  }

  // Sparad-sökning-notis: ny-träff-count finns ej i recent-searches-DTO ännu
  // (newCount finns men gäller bara körda sökningar). Visa mock om vi har
  // minst en sökning över huvud taget.
  const recentSearchesData =
    recentSearches.kind === "ok" ? recentSearches.data : [];
  if (recentSearchesData.length > 0) {
    infoNotices.push({
      id: `n-saved-search-${dateSlug}`,
      kind: "info",
      label: t("notices.savedSearchLabel"),
      text: t.rich("notices.savedSearchText", {
        name: OVERSIKT_MOCK.savedSearchHitsLast.name,
        count: OVERSIKT_MOCK.savedSearchHitsLast.newHits,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("notices.savedSearchCta"),
      href: "/sokningar",
      // MOCK: BE-port saknas för sökning-körnings-stämpel
      time: t("notices.timeYesterday"),
    });
  }

  // Summary-data
  const cvCount = resumes.kind === "ok" ? resumes.data.items.length : 0;
  const firstResume =
    resumes.kind === "ok" && resumes.data.items.length > 0
      ? [...resumes.data.items].sort((a, b) =>
          b.updatedAt.localeCompare(a.updatedAt)
        )[0]
      : null;
  const lastUpdatedCvDate = firstResume
    ? formatSwedishShortDate(firstResume.updatedAt)
    : null;

  const lastSearch =
    recentSearchesData.length > 0
      ? [...recentSearchesData].sort((a, b) =>
          b.lastViewedAt.localeCompare(a.lastViewedAt)
        )[0]
      : null;
  const lastSearchName = lastSearch?.label ?? null;

  // design-reviewer M2: vid endpoint-failure ⇒ null (renders som "—"),
  // inte 0 (genuint missvisande för prod-korpus ~46k aktiva annonser).
  // svans-PR2: nu från landing-stats (Worker-precomputed cache, samma siffra
  // som HeaderStats). Floor-fallback (IsStale=true) räknas som "ok" — vi
  // använder floor-värdet hellre än "—" för att undvika svart fält på sidan.
  const activeJobAdsTotal = landingStats?.activeCount ?? null;

  const profileCreatedAt =
    profile.kind === "ok" ? profile.data.createdAt : null;
  const searchStartDate = profileCreatedAt
    ? formatSwedishShortDate(profileCreatedAt)
    : null;
  const searchStartDaysSince = profileCreatedAt
    ? daysSince(profileCreatedAt, today)
    : null;

  const stampDate = today.toISOString().slice(0, 10);

  return (
    <>
      {/* F6 P5 Punkt 6 — page-hero (HANDOVER-v4 §2.2). Edge-to-edge navy
          band; TodayCard ligger som vitt kort i aside mot navy bg. */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">
              {t("hero.kicker", { name: kickerName })}
            </div>
            <h1 className="jp-pagehero__title">{t("hero.title")}</h1>
            <p className="jp-pagehero__lede">{t("hero.lede")}</p>
          </div>
          <div className="jp-pagehero__aside">
            <TodayCard
              today={today}
              events={OVERSIKT_MOCK.todaysEvents}
              googleSynced={OVERSIKT_MOCK.googleSynced}
            />
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* Notiser */}
        <NoticeList
          actionNotices={actionNotices}
          infoNotices={infoNotices}
          lastUpdated={OVERSIKT_MOCK.noticesLastUpdated}
        />

        {/* Sammanfattning */}
        <section
          className="jp-section"
          aria-labelledby="oversikt-sammanfattning"
        >
          <div className="jp-section__head">
            <h2
              className="jp-section__title"
              id="oversikt-sammanfattning"
            >
              {t("summary.title")}
            </h2>
            <span className="jp-section__count">
              {t.rich("summary.stamp", {
                date: stampDate,
                mono: (chunks) => <span className="jp-mono">{chunks}</span>,
              })}
            </span>
          </div>

          <Summary
            counts={counts}
            savedJobsCount={savedCount}
            recentSearchesCount={recentSearchesData.length}
            lastSearchName={lastSearchName}
            activeJobAdsTotal={activeJobAdsTotal}
            matchCountToday={OVERSIKT_MOCK.matchCountToday}
            cvCount={cvCount}
            personalLettersCount={OVERSIKT_MOCK.personalLettersCount}
            lastUpdatedCvDate={lastUpdatedCvDate}
            searchStartDate={searchStartDate}
            searchStartDaysSince={searchStartDaysSince}
          />
        </section>
      </div>
    </>
  );
}
