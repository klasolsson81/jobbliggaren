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
  formatNoticesStamp,
  formatSwedishShortDate,
  OVERSIKT_FOLLOW_UP_DAYS,
} from "@/lib/oversikt/aggregations";
import { OVERSIKT_MOCK } from "@/lib/oversikt/mock-data";
import { OVERSIKT_MATCH_GRADES } from "@/lib/dto/match-count";
import { buildJobbHref, DEFAULT_SORT_BY } from "@/lib/job-ads/search-params";
import { buildRecentSearchHref } from "@/lib/job-ads/recent-search-href";
import { TodayCard } from "./today-card";
import { NoticeList } from "./notice-list";
import { Summary } from "./summary";
import { SavedSearchNoticeText } from "./saved-search-notice-text";
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
  /**
   * ADR 0079 STEG 6 — live match-count (Bra + Stark) för matchnings-notisen.
   * `number` = backend-svar (`count`, kan vara 0 = honest nollstate). `null` =
   * fetch:en degraderade (nätverk/auth/rate-limit) ⇒ notisen utelämnas helt
   * (aldrig en mock-fallback). Notisen renderas bara när profilen har angett ett
   * yrke (`hasStatedDesiredOccupation`); annars äger setup-nudgen slotten.
   */
  readonly matchCount: number | null;
  /**
   * ADR 0080 Vag 4 PR-5 — antalet bakgrundsmatchningar NYA sedan senaste besök
   * (live `GET /me/new-match-count`), för Sammanfattningens "Nya matchningar"-
   * rad (ersätter den dagliga `matchCountToday`-mocken 28, nu guest-only; SKILD
   * från STEG 6:s vecko-`matchCount` som ersatte mock-143). Degraderar till `0`
   * vid fetch-fel (paritet STEG 6:s
   * `matchCount`-degradering, men `0` här i stället för `null` — raden visar
   * alltid en siffra och länkar till /matchningar; ett honest 0 är korrekt
   * fallback när bakgrundsmatchning ej kunde läsas).
   */
  readonly newMatchCount: number;
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
  matchCount,
  newMatchCount,
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
        // #384 — talet kommer från samma SSOT som filter-tröskeln (ingen
        // hårdkodad "14" i copyn) så de aldrig kan drifta isär.
        days: OVERSIKT_FOLLOW_UP_DAYS,
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

  // F4-12 PR-B (ADR 0076): setup-nudge ↔ match-notis är ÖMSESIDIGT uteslutande
  // och styrs av `hasStatedDesiredOccupation`. Du kan inte ha "annonser som
  // matchar din profil" innan en profil finns — det vore ohederligt. Yrke
  // angett → match-notis (LIVE count, STEG 6). Ej angett → setup-nudge. Aldrig
  // båda (undviker två motsägande "Matchning"-rader).
  const hasStatedOccupation =
    profile.kind === "ok" && profile.data.hasStatedDesiredOccupation;

  // Recent searches — consumed by both the saved-search notice (below) and the
  // Summary. `lastSearch` = the most recently run/viewed search ("din senaste
  // körning"), which is the one the notice features (#294).
  const recentSearchesData =
    recentSearches.kind === "ok" ? recentSearches.data : [];
  const lastSearch =
    recentSearchesData.length > 0
      ? [...recentSearchesData].sort((a, b) =>
          b.lastViewedAt.localeCompare(a.lastViewedAt),
        )[0]
      : null;

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
  } else if (matchCount !== null) {
    // ADR 0079 STEG 6 — LIVE per-användar count (ersätter mock-143). `null` =
    // fetch:en degraderade ⇒ notisen utelämnas helt (denna gren körs inte),
    // aldrig mock-fallback. Annars renderas notisen för bägge talen:
    //   count > 0  → "Det finns N jobb som matchar din profil."
    //   count == 0 → honest nollstate ("inga jobb ... just nu") — länken kvar.
    //
    // Trust-invariant (load-bearing): länkens grad-set MÅSTE vara counten:s
    // grad-set, annars ser användaren N i notisen men ett annat tal på /jobb.
    // Länken byggs från OVERSIKT_MATCH_GRADES (= backend
    // GetMyMatchCountQueryHandler.HeadlineGrades, [Good, Strong]) via det
    // delade buildJobbHref-URL-kontraktet (upprepad ?matchGrades= enum-namn).
    // Grad-NEUTRAL copy ("matchar din profil") — aldrig "Toppmatchningar"
    // (counten är Fast-bandet, honest by design, ADR 0076 G3-OPT-A).
    const matchHref = buildJobbHref({
      q: "",
      occupationGroup: [],
      region: [],
      municipality: [],
      employmentType: [],
      worktimeExtent: [],
      matchGrades: OVERSIKT_MATCH_GRADES,
      sortBy: DEFAULT_SORT_BY,
    });
    infoNotices.push({
      id: `n-match-${dateSlug}`,
      kind: "info",
      label: t("notices.matchLabel"),
      text:
        matchCount > 0
          ? t.rich("notices.matchText", {
              // Raw number — the catalog formats it locale-aware via ICU
              // {count, number} (sv: non-breaking-space grouping per CLAUDE §10;
              // en: comma). The corpus routinely exceeds 1000 for broad
              // occupation groups.
              count: matchCount,
              b: (chunks) => <b>{chunks}</b>,
            })
          : t("notices.matchTextZero"),
      cta: t("notices.matchCta"),
      href: matchHref,
      // MOCK: BE-port saknas för matchning-uppdaterings-stämpel (STEG 6
      // follow-up). Counten är live; tidsstämpeln förblir mock så länge.
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

  // Sparad-sökning-notis (#294): featurar DIN SENASTE sökning (lastSearch) med
  // riktigt namn + CTA som KÖR sökningen (replay-href via buildRecentSearchHref),
  // i stället för det tidigare hårdkodade `/sokningar`-målet (fel destination +
  // dubbelsteg) och mock-namnet. "Har N nya träffar"-counten hämtas lazy
  // klient-side i SavedSearchNoticeText (TD-94: server-side per-sök-COUNT hoppas
  // över, includeCount=false). Klas 2026-06-28: nya-annonser-signalen rankas
  // UNDER match-/topp-match-notisen → notisen ligger sist i infoNotices (efter
  // match-notisen + intervju-notisen). Tids-stämpeln är nu riktig (lastViewedAt).
  if (lastSearch) {
    infoNotices.push({
      id: `n-saved-search-${dateSlug}`,
      kind: "info",
      label: t("notices.savedSearchLabel"),
      text: (
        <SavedSearchNoticeText
          searchId={lastSearch.id}
          name={lastSearch.label}
        />
      ),
      cta: t("notices.savedSearchCta"),
      href: buildRecentSearchHref(lastSearch),
      time: formatDaysAgo(tRelativeTime, lastSearch.lastViewedAt, today),
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
          // #384 — notiserna beräknas LIVE per request (force-dynamic), så
          // "senast uppdaterad" är render-tiden, inte en stale mock-stämpel.
          lastUpdated={formatNoticesStamp(today)}
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
            newMatchCount={newMatchCount}
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
