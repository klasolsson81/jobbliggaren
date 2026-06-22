import { useTranslations } from "next-intl";
import {
  GUEST_MOCK,
  GUEST_MOCK_REF_DATE,
  OVERSIKT_MOCK,
} from "@/lib/guest/mock-data";
import { SummaryRow } from "@/components/oversikt/summary-row";
import { TodayCard } from "@/components/oversikt/today-card";
import { NoticeList } from "@/components/oversikt/notice-list";
import type { NoticeData } from "@/components/oversikt/notice-row";

// F-Pre Punkt 5 — Gäst-översikt-sida (CTO-dom 2026-05-24 Beslut 1).
// F-Pre Punkt 5b 2026-05-24 (CTO Beslut 5, Variant α): Klas-feedback "för
// liten" adresserad genom återanvändning av `<TodayCard>` (presentational
// RSC), utökad summary (4 rader per grupp), och fler notiser (4 i stället
// för 3).
//
// F-Pre Punkt 5b in-block-fix 2026-05-24 (design-reviewer M3):
// notiser-strukturen renderas nu via `<NoticeList>` + `<NoticeData[]>` så
// markup, ARIA, grupp-rubriker ("Kräver åtgärd" / "Information") och
// 6-kolumn-grid (inkl. dismiss-knapp) speglar live `(app)/oversikt` exakt.
// `<NoticeList>` dismiss-state är client-only localStorage — ingen BE-
// mutation (gäst-tree-disciplin OK).
//
// design-reviewer m5: STAMP_DATE härleds från frozen `GUEST_MOCK_REF_DATE`
// så hela demoöversikten är konsekvent frozen (mockdata åldras inte mellan
// renderings).

const STAMP_DATE = GUEST_MOCK_REF_DATE.toISOString().slice(0, 10);

function formatThousands(n: number): string {
  return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}

export function GuestOversiktPage() {
  // Synchronous next-intl translator — keeps this a non-async RSC.
  const t = useTranslations("guest");
  const { applications, resumes, summary } = GUEST_MOCK;
  const latestOffer = applications.find((a) => a.status === "Offer");
  const latestInterview = applications.find((a) => a.status === "Interview");

  const actionNotices: NoticeData[] = [];
  if (latestOffer) {
    actionNotices.push({
      id: "guest-n-offer",
      kind: "success",
      label: t("oversikt.noticeOfferLabel"),
      text: t.rich("oversikt.noticeOfferText", {
        company: latestOffer.company,
        role: latestOffer.role,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("oversikt.noticeOfferCta"),
      href: "/vantelista",
      time: t("oversikt.timeToday"),
    });
  }
  actionNotices.push({
    id: "guest-n-drafts",
    kind: "warning",
    label: t("oversikt.noticeDraftsLabel"),
    text: t.rich("oversikt.noticeDraftsText", {
      count: summary.applicationsByStatus.Draft,
      b: (chunks) => <b>{chunks}</b>,
    }),
    cta: t("oversikt.noticeDraftsCta"),
    href: "/gast/ansokningar",
    time: t("oversikt.timeToday"),
  });

  const infoNotices: NoticeData[] = [];
  if (latestInterview) {
    infoNotices.push({
      id: "guest-n-interview",
      kind: "brand",
      label: t("oversikt.noticeInterviewLabel"),
      text: t.rich("oversikt.noticeInterviewText", {
        company: latestInterview.company,
        b: (chunks) => <b>{chunks}</b>,
      }),
      cta: t("oversikt.noticeInterviewCta"),
      href: "/vantelista",
      time: t("oversikt.timeYesterday"),
    });
  }
  infoNotices.push({
    id: "guest-n-match",
    kind: "info",
    label: t("oversikt.noticeMatchLabel"),
    text: t.rich("oversikt.noticeMatchText", {
      count: OVERSIKT_MOCK.matchCountThisWeek,
      segment: OVERSIKT_MOCK.matchSegmentLabel,
      b: (chunks) => <b>{chunks}</b>,
      em: (chunks) => <em>{chunks}</em>,
    }),
    cta: t("oversikt.noticeMatchCta"),
    href: "/gast/jobb",
    time: t("oversikt.timeToday"),
  });

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">{t("oversikt.kicker")}</div>
            <h1 className="jp-pagehero__title">{t("oversikt.title")}</h1>
            <p className="jp-pagehero__lede">{t("oversikt.lede")}</p>
          </div>
          <div className="jp-pagehero__aside">
            <TodayCard
              today={GUEST_MOCK_REF_DATE}
              events={OVERSIKT_MOCK.todaysEvents}
              googleSynced={false}
            />
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <NoticeList
          actionNotices={actionNotices}
          infoNotices={infoNotices}
          lastUpdated={t("oversikt.lastUpdated", { date: STAMP_DATE })}
        />

        <section className="jp-section" aria-labelledby="guest-sammanfattning">
          <div className="jp-section__head">
            <h2 className="jp-section__title" id="guest-sammanfattning">
              {t("oversikt.summaryTitle")}
            </h2>
            <span className="jp-section__count">
              {t.rich("oversikt.summaryStamp", {
                date: STAMP_DATE,
                mono: (chunks) => <span className="jp-mono">{chunks}</span>,
              })}
            </span>
          </div>

          <div className="jp-summary">
            <div className="jp-summary__group">
              <div className="jp-summary__group__title">
                {t("oversikt.groupApplications")}
              </div>
              <SummaryRow
                label={t("oversikt.rowApplicationsTotal")}
                value={summary.applicationsTotal}
              />
              <SummaryRow
                label={t("oversikt.rowDrafts")}
                value={summary.applicationsByStatus.Draft}
              />
              <SummaryRow
                label={t("oversikt.rowSubmitted")}
                value={summary.applicationsByStatus.Submitted}
              />
              <SummaryRow
                label={t("oversikt.rowInterviews")}
                value={summary.applicationsByStatus.Interview}
                highlight
              />
              <SummaryRow
                label={t("oversikt.rowOffers")}
                value={summary.applicationsByStatus.Offer}
                highlight
              />
              <SummaryRow
                label={t("oversikt.rowRejected")}
                value={summary.applicationsByStatus.Rejected}
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">
                {t("oversikt.groupWatch")}
              </div>
              <SummaryRow
                label={t("oversikt.rowSavedSearches")}
                value={OVERSIKT_MOCK.savedSearchHitsLast.newHits}
                hint={t("oversikt.hintNewHits")}
              />
              <SummaryRow
                label={t("oversikt.rowNewMatchesToday")}
                value={OVERSIKT_MOCK.matchCountToday}
                hint={t("oversikt.hintProfile")}
              />
              <SummaryRow
                label={t("oversikt.rowActiveJobAdsTotal")}
                value={formatThousands(GUEST_MOCK.activeJobAdsTotal)}
              />
              <SummaryRow
                label={t("oversikt.rowDemoJobAds")}
                value={GUEST_MOCK.summary.jobAdsTotal}
                href="/gast/jobb"
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">
                {t("oversikt.groupMaterial")}
              </div>
              <SummaryRow
                label={t("oversikt.rowResumeVariants")}
                value={summary.resumesTotal}
                href="/gast/cv"
              />
              <SummaryRow
                label={t("oversikt.rowCoverLetters")}
                value={OVERSIKT_MOCK.personalLettersCount}
              />
              <SummaryRow
                label={t("oversikt.rowLatestResume")}
                value={resumes[0]?.updatedAtLabel ?? t("oversikt.valueDash")}
              />
              <SummaryRow
                label={t("oversikt.rowDemoActiveSince")}
                value={t("oversikt.timeToday")}
                hint={t("oversikt.hintNotSaved")}
              />
            </div>
          </div>
        </section>
      </div>
    </>
  );
}
