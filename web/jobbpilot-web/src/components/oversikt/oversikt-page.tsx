import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobSeekerProfileDto } from "@/lib/dto/me";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ListSavedJobAdsResult } from "@/lib/dto/saved-job-ads";
import type { ListRecentSearchesResult } from "@/lib/dto/recent-searches";
import type { GetResumesResult } from "@/lib/dto/resumes";
import type { ListJobAdsResult } from "@/lib/dto/job-ads";
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
  readonly jobAds: ApiResult<ListJobAdsResult>;
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
  jobAds,
}: OversiktPageProps) {
  const today = new Date();
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
    const offerCompany = latestOffer.jobAd?.company ?? "ett företag";
    const offerTitle = latestOffer.jobAd?.title;
    actionNotices.push({
      id: "n-offer",
      kind: "success",
      label: "Erbjudande",
      text: (
        <>
          <b>{offerCompany}</b>
          {offerTitle ? ` — ${offerTitle}` : ""}. Erbjudande väntar svar.
        </>
      ),
      cta: "Granska erbjudande",
      href: "/ansokningar",
      time: formatDaysAgo(latestOffer.updatedAt, today),
    });
  }

  if (followUps.length > 0) {
    actionNotices.push({
      id: "n-followup",
      kind: "warning",
      label: "Uppföljning",
      text: (
        <>
          Du har <b>{followUps.length} ansökningar</b> som inte fått svar på
          över 14 dagar. Överväg att höra av dig.
        </>
      ),
      cta: "Visa ansökningar",
      href: "/ansokningar",
      // MOCK: BE-port saknas för "när-noteringen-räknades-ut"-tidsstämpel
      time: "i dag",
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
      id: "n-deadline",
      kind: "warning",
      label: "Deadline",
      text: (
        <>
          <b>{futureDeadlines.length} sparade annonser</b> har sista
          ansökningsdag denna vecka ({labels}).
        </>
      ),
      cta: "Visa sparade",
      href: "/sparade",
      // MOCK: BE-port saknas för faktisk deadline-stämpel
      time: "denna vecka",
    });
  }

  const infoNotices: NoticeData[] = [
    {
      id: "n-match",
      kind: "info",
      label: "Matchning",
      text: (
        <>
          Det finns <b>{OVERSIKT_MOCK.matchCountThisWeek} nya annonser</b> som
          matchar din profil sedan i tisdags — de flesta inom{" "}
          <em>{OVERSIKT_MOCK.matchSegmentLabel}</em>.
        </>
      ),
      cta: "Visa annonser",
      href: "/jobb",
      // MOCK: BE-port saknas för matchning-uppdaterings-stämpel
      time: "i dag",
    },
  ];

  if (recentInterviews.length > 0) {
    const interview = recentInterviews[0]!;
    const interviewCompany =
      interview.jobAd?.company ?? "en arbetsgivare";
    infoNotices.push({
      id: "n-interview-confirmed",
      kind: "brand",
      label: "Intervju",
      text: (
        <>
          <b>{interviewCompany}</b> har bekräftat intervjutid.
        </>
      ),
      cta: "Öppna ärende",
      href: "/ansokningar",
      time: formatDaysAgo(interview.updatedAt, today),
    });
  }

  // Sparad-sökning-notis: ny-träff-count finns ej i recent-searches-DTO ännu
  // (newCount finns men gäller bara körda sökningar). Visa mock om vi har
  // minst en sökning över huvud taget.
  const recentSearchesData =
    recentSearches.kind === "ok" ? recentSearches.data : [];
  if (recentSearchesData.length > 0) {
    infoNotices.push({
      id: "n-saved-search",
      kind: "info",
      label: "Sparad sökning",
      text: (
        <>
          <b>{OVERSIKT_MOCK.savedSearchHitsLast.name}</b> har{" "}
          <b>{OVERSIKT_MOCK.savedSearchHitsLast.newHits} nya träffar</b> sedan
          din senaste körning.
        </>
      ),
      cta: "Kör sökning",
      href: "/sokningar",
      // MOCK: BE-port saknas för sökning-körnings-stämpel
      time: "i går",
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
  const activeJobAdsTotal =
    jobAds.kind === "ok" ? jobAds.data.totalCount : null;

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
    <div className="jp-container jp-page">
      {/* Title block + I dag-kort */}
      <div className="jp-page__title-block">
        <div className="jp-oversikt__head">
          <div>
            <div className="jp-oversikt__kicker">
              Inloggad som {kickerName}
            </div>
            <h1 className="jp-page__title">Översikt</h1>
            <p className="jp-page__lede">
              Senaste händelser och status för dina ansökningar.
            </p>
          </div>
          <TodayCard
            today={today}
            events={OVERSIKT_MOCK.todaysEvents}
            googleSynced={OVERSIKT_MOCK.googleSynced}
          />
        </div>
      </div>

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
            Sammanfattning
          </h2>
          <span className="jp-section__count">
            registrerat per <span className="jp-mono">{stampDate}</span>
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
  );
}
