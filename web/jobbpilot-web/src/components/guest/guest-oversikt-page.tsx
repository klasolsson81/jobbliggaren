import Link from "next/link";
import { GUEST_MOCK, OVERSIKT_MOCK } from "@/lib/guest/mock-data";
import { SummaryRow } from "@/components/oversikt/summary-row";
import { TodayCard } from "@/components/oversikt/today-card";

// F-Pre Punkt 5 — Gäst-översikt-sida (CTO-dom 2026-05-24 Beslut 1).
// F-Pre Punkt 5b 2026-05-24 (CTO Beslut 5, Variant α): Klas-feedback "för
// liten" adresserad genom återanvändning av `<TodayCard>` (presentational
// RSC), utökad summary (4 rader per grupp), och fler notiser (4 i stället
// för 3).
//
// Ren mockdata-driven utan BE-anrop. Återanvänder presentational
// `<SummaryRow>` + `<TodayCard>`. Muterande notice-CTAs leder till
// /vantelista per Klas-direktiv §F.

const STAMP_DATE = new Date().toISOString().slice(0, 10);
// Stabil "i dag"-referens för demo så TodayCard inte ändrar datum mellan
// renderings i samma session (gäst-mockdata är frozen — datumet ska kännas
// realistiskt men inte drifta). Använder referens-stämpel som matchar
// mock-applikationernas updatedAtLabel-period.
const GUEST_DEMO_TODAY = new Date("2026-05-24T08:00:00Z");

function formatThousands(n: number): string {
  return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}

export function GuestOversiktPage() {
  const { applications, resumes, summary } = GUEST_MOCK;
  const latestOffer = applications.find((a) => a.status === "Offer");
  const latestInterview = applications.find((a) => a.status === "Interview");

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
              today={GUEST_DEMO_TODAY}
              events={OVERSIKT_MOCK.todaysEvents}
              googleSynced={false}
            />
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <section className="jp-notice-list" aria-labelledby="guest-notiser">
          <h2 className="sr-only" id="guest-notiser">
            Notiser (exempel)
          </h2>

          {latestOffer && (
            <div className="jp-notice jp-notice--success">
              <span className="jp-notice__strip" aria-hidden="true" />
              <span className="jp-notice__label">Erbjudande</span>
              <div className="jp-notice__text">
                <b>{latestOffer.company}</b> — {latestOffer.role}. Erbjudande
                väntar svar.
              </div>
              <Link href="/vantelista" className="jp-notice__cta">
                Anmäl till väntelistan
              </Link>
              <span className="jp-notice__time">i dag</span>
            </div>
          )}

          {latestInterview && (
            <div className="jp-notice jp-notice--brand">
              <span className="jp-notice__strip" aria-hidden="true" />
              <span className="jp-notice__label">Intervju</span>
              <div className="jp-notice__text">
                <b>{latestInterview.company}</b> har bekräftat intervjutid.
              </div>
              <Link href="/vantelista" className="jp-notice__cta">
                Anmäl till väntelistan
              </Link>
              <span className="jp-notice__time">i går</span>
            </div>
          )}

          <div className="jp-notice jp-notice--warning">
            <span className="jp-notice__strip" aria-hidden="true" />
            <span className="jp-notice__label">Påminnelse</span>
            <div className="jp-notice__text">
              Du har <b>{GUEST_MOCK.summary.applicationsByStatus.Draft} utkast</b>{" "}
              som inte är inskickade. Färdigställ och skicka för att hålla
              pipeline aktiv.
            </div>
            <Link href="/gast/ansokningar" className="jp-notice__cta">
              Visa ansökningar
            </Link>
            <span className="jp-notice__time">i dag</span>
          </div>

          <div className="jp-notice jp-notice--info">
            <span className="jp-notice__strip" aria-hidden="true" />
            <span className="jp-notice__label">Matchning</span>
            <div className="jp-notice__text">
              Det finns{" "}
              <b>{OVERSIKT_MOCK.matchCountThisWeek} nya annonser</b> som
              matchar profilen — de flesta inom{" "}
              <em>{OVERSIKT_MOCK.matchSegmentLabel}</em>.
            </div>
            <Link href="/gast/jobb" className="jp-notice__cta">
              Visa annonser
            </Link>
            <span className="jp-notice__time">i dag</span>
          </div>
        </section>

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
