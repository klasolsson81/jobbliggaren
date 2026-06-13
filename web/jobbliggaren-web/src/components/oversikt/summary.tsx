import { SummaryRow } from "./summary-row";
import type { ApplicationCounts } from "@/lib/oversikt/aggregations";

interface SummaryProps {
  readonly counts: ApplicationCounts;
  readonly savedJobsCount: number;
  readonly recentSearchesCount: number;
  readonly lastSearchName: string | null;
  /**
   * `null` när `getJobAds`-endpointen failade. Render som "—" istället
   * för 0 — design-reviewer M2 (2026-05-24): "0" maskerar endpoint-fel
   * och kan inte skiljas från äkta tom korpus i UI. Korpus är ~46k i prod
   * så genuint 0 är osannolikt; vid fel ska användaren se saknad-state.
   */
  readonly activeJobAdsTotal: number | null;
  readonly matchCountToday: number;
  readonly cvCount: number;
  readonly personalLettersCount: number;
  readonly lastUpdatedCvDate: string | null;
  readonly searchStartDate: string | null;
  readonly searchStartDaysSince: number | null;
}

/**
 * Sammanfattning — civic-utility-ledger med tre kolumner.
 * Server Component (ren render från props).
 *
 * Klickbara rader navigerar via `<Link>` per HANDOVER §6. Inerta rader
 * är `<div>`. Chevron-slot reserveras alltid för att aligna värdekolumnen.
 */
export function Summary({
  counts,
  savedJobsCount,
  recentSearchesCount,
  lastSearchName,
  activeJobAdsTotal,
  matchCountToday,
  cvCount,
  personalLettersCount,
  lastUpdatedCvDate,
  searchStartDate,
  searchStartDaysSince,
}: SummaryProps) {
  return (
    <div className="jp-summary">
      <div className="jp-summary__group">
        <div className="jp-summary__group__title">Ansökningar</div>
        <SummaryRow
          label="Aktiva ansökningar"
          value={counts.active}
          href="/ansokningar"
        />
        <SummaryRow label="Utkast" value={counts.drafts} href="/ansokningar" />
        <SummaryRow
          label="Intervjuer bokade"
          value={counts.interviews}
          highlight
          href="/ansokningar"
        />
        <SummaryRow
          label="Erbjudanden"
          value={counts.offers}
          highlight
          href="/ansokningar"
        />
        <SummaryRow label="Avslag" value={counts.rejected} />
        <SummaryRow
          label="Inget svar"
          value={counts.ghosted}
          hint="över 30 dagar"
        />
      </div>

      <div className="jp-summary__group">
        <div className="jp-summary__group__title">Bevakning</div>
        <SummaryRow
          label="Sparade annonser"
          value={savedJobsCount}
          href="/sparade"
        />
        <SummaryRow
          label="Sparade sökningar"
          value={recentSearchesCount}
          href="/sokningar"
        />
        <SummaryRow
          label="Nya matchningar i dag"
          value={matchCountToday}
          hint="profil"
          href="/jobb"
        />
        <SummaryRow
          label="Aktiva annonser totalt"
          value={
            activeJobAdsTotal != null ? formatThousands(activeJobAdsTotal) : "—"
          }
        />
        <SummaryRow
          label="Senaste sökning"
          value={lastSearchName ?? "—"}
          href={lastSearchName ? "/sokningar" : undefined}
        />
      </div>

      <div className="jp-summary__group">
        <div className="jp-summary__group__title">Underlag</div>
        <SummaryRow label="CV-varianter" value={cvCount} href="/cv" />
        <SummaryRow label="Personliga brev" value={personalLettersCount} />
        <SummaryRow
          label="Senast uppdaterat CV"
          value={lastUpdatedCvDate ?? "—"}
          href={lastUpdatedCvDate ? "/cv" : undefined}
        />
        <SummaryRow
          label="Aktiv sedan"
          value={searchStartDate ?? "—"}
          hint={
            searchStartDaysSince != null
              ? `${searchStartDaysSince} dagar`
              : undefined
          }
        />
      </div>
    </div>
  );
}

/** Svensk tusenavgränsning med non-breaking space ("45 580"). */
function formatThousands(n: number): string {
  return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}
