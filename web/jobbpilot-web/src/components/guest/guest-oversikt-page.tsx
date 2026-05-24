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
  const { applications, resumes, summary } = GUEST_MOCK;
  const latestOffer = applications.find((a) => a.status === "Offer");
  const latestInterview = applications.find((a) => a.status === "Interview");

  const actionNotices: NoticeData[] = [];
  if (latestOffer) {
    actionNotices.push({
      id: "guest-n-offer",
      kind: "success",
      label: "Erbjudande",
      text: (
        <>
          <b>{latestOffer.company}</b> — {latestOffer.role}. Erbjudande
          väntar svar.
        </>
      ),
      cta: "Anmäl till väntelistan",
      href: "/vantelista",
      time: "i dag",
    });
  }
  actionNotices.push({
    id: "guest-n-drafts",
    kind: "warning",
    label: "Påminnelse",
    text: (
      <>
        Du har <b>{summary.applicationsByStatus.Draft} utkast</b> som inte
        är inskickade. Färdigställ och skicka för att hålla pipeline aktiv.
      </>
    ),
    cta: "Visa ansökningar",
    href: "/gast/ansokningar",
    time: "i dag",
  });

  const infoNotices: NoticeData[] = [];
  if (latestInterview) {
    infoNotices.push({
      id: "guest-n-interview",
      kind: "brand",
      label: "Intervju",
      text: (
        <>
          <b>{latestInterview.company}</b> har bekräftat intervjutid.
        </>
      ),
      cta: "Anmäl till väntelistan",
      href: "/vantelista",
      time: "i går",
    });
  }
  infoNotices.push({
    id: "guest-n-match",
    kind: "info",
    label: "Matchning",
    text: (
      <>
        Det finns <b>{OVERSIKT_MOCK.matchCountThisWeek} nya annonser</b>{" "}
        som matchar profilen — de flesta inom{" "}
        <em>{OVERSIKT_MOCK.matchSegmentLabel}</em>.
      </>
    ),
    cta: "Visa annonser",
    href: "/gast/jobb",
    time: "i dag",
  });

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">Demoöversikt</div>
            <h1 className="jp-pagehero__title">Översikt</h1>
            <p className="jp-pagehero__lede">
              Så här ser det ut när du följer dina ansökningar. Allt här är
              exempeldata.
            </p>
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
          lastUpdated={`exempeldata · ${STAMP_DATE}`}
        />

        <section className="jp-section" aria-labelledby="guest-sammanfattning">
          <div className="jp-section__head">
            <h2 className="jp-section__title" id="guest-sammanfattning">
              Sammanfattning
            </h2>
            <span className="jp-section__count">
              exempeldata per <span className="jp-mono">{STAMP_DATE}</span>
            </span>
          </div>

          <div className="jp-summary">
            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Ansökningar</div>
              <SummaryRow
                label="Ansökningar totalt"
                value={summary.applicationsTotal}
              />
              <SummaryRow
                label="Utkast"
                value={summary.applicationsByStatus.Draft}
              />
              <SummaryRow
                label="Inskickade"
                value={summary.applicationsByStatus.Submitted}
              />
              <SummaryRow
                label="Intervjuer"
                value={summary.applicationsByStatus.Interview}
                highlight
              />
              <SummaryRow
                label="Erbjudanden"
                value={summary.applicationsByStatus.Offer}
                highlight
              />
              <SummaryRow
                label="Avslag"
                value={summary.applicationsByStatus.Rejected}
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Bevakning</div>
              <SummaryRow
                label="Sparade sökningar"
                value={OVERSIKT_MOCK.savedSearchHitsLast.newHits}
                hint="nya träffar"
              />
              <SummaryRow
                label="Nya matchningar i dag"
                value={OVERSIKT_MOCK.matchCountToday}
                hint="profil"
              />
              <SummaryRow
                label="Aktiva annonser totalt"
                value={formatThousands(GUEST_MOCK.activeJobAdsTotal)}
              />
              <SummaryRow
                label="Exempelannonser i demo"
                value={GUEST_MOCK.summary.jobAdsTotal}
                href="/gast/jobb"
              />
            </div>

            <div className="jp-summary__group">
              <div className="jp-summary__group__title">Underlag</div>
              <SummaryRow
                label="CV-varianter"
                value={summary.resumesTotal}
                href="/gast/cv"
              />
              <SummaryRow
                label="Personliga brev"
                value={OVERSIKT_MOCK.personalLettersCount}
              />
              <SummaryRow
                label="Senast uppdaterat CV"
                value={resumes[0]?.updatedAtLabel ?? "—"}
              />
              <SummaryRow
                label="Demo aktiv sedan"
                value="i dag"
                hint="ej sparad"
              />
            </div>
          </div>
        </section>
      </div>
    </>
  );
}
