import { useTranslations } from "next-intl";
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
  /**
   * ADR 0080 Vag 4 PR-5 — antalet bakgrundsmatchningar NYA sedan senaste besök
   * (live `GET /me/new-match-count`). `0` är ett honest svar (inget nytt) och
   * renderas som "0"; aldrig en mock-siffra. Raden länkar till /matchningar.
   */
  readonly newMatchCount: number;
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
  newMatchCount,
  cvCount,
  personalLettersCount,
  lastUpdatedCvDate,
  searchStartDate,
  searchStartDaysSince,
}: SummaryProps) {
  const t = useTranslations("oversikt");
  const dash = t("summary.valueDash");
  return (
    <div className="jp-summary">
      <div className="jp-summary__group">
        <div className="jp-summary__group__title">
          {t("summary.groupApplications")}
        </div>
        <SummaryRow
          label={t("summary.rowActive")}
          value={counts.active}
          href="/ansokningar"
        />
        <SummaryRow
          label={t("summary.rowDrafts")}
          value={counts.drafts}
          href="/ansokningar"
        />
        <SummaryRow
          label={t("summary.rowInterviews")}
          value={counts.interviews}
          highlight
          href="/ansokningar"
        />
        <SummaryRow
          label={t("summary.rowOffers")}
          value={counts.offers}
          highlight
          href="/ansokningar"
        />
        <SummaryRow label={t("summary.rowRejected")} value={counts.rejected} />
        <SummaryRow
          label={t("summary.rowGhosted")}
          value={counts.ghosted}
          hint={t("summary.hintGhosted")}
        />
      </div>

      <div className="jp-summary__group">
        <div className="jp-summary__group__title">
          {t("summary.groupWatch")}
        </div>
        <SummaryRow
          label={t("summary.rowSavedJobs")}
          value={savedJobsCount}
          href="/sparade"
        />
        <SummaryRow
          label={t("summary.rowSavedSearches")}
          value={recentSearchesCount}
          href="/sokningar"
        />
        <SummaryRow
          label={t("summary.rowNewMatches")}
          value={newMatchCount}
          hint={t("summary.hintSinceLastVisit")}
          href="/matchningar"
        />
        <SummaryRow
          label={t("summary.rowActiveJobAdsTotal")}
          value={
            activeJobAdsTotal != null
              ? formatThousands(activeJobAdsTotal)
              : dash
          }
        />
        <SummaryRow
          label={t("summary.rowLastSearch")}
          value={lastSearchName ?? dash}
          href={lastSearchName ? "/sokningar" : undefined}
        />
      </div>

      <div className="jp-summary__group">
        <div className="jp-summary__group__title">
          {t("summary.groupMaterial")}
        </div>
        <SummaryRow
          label={t("summary.rowResumeVariants")}
          value={cvCount}
          href="/cv"
        />
        <SummaryRow
          label={t("summary.rowCoverLetters")}
          value={personalLettersCount}
        />
        <SummaryRow
          label={t("summary.rowLatestResume")}
          value={lastUpdatedCvDate ?? dash}
          href={lastUpdatedCvDate ? "/cv" : undefined}
        />
        <SummaryRow
          label={t("summary.rowActiveSince")}
          value={searchStartDate ?? dash}
          hint={
            searchStartDaysSince != null
              ? t("summary.hintDays", { count: searchStartDaysSince })
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
