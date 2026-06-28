import { useTranslations } from "next-intl";

/**
 * Tagg-rad för jobbannons-rad (`.jp-job`). Generisk subkomponent — renderar
 * NY/färskhet/Sparad/Ansökt-taggar högerjusterat inom `.jp-job__title` h3 via
 * `margin-left:auto` (CTO-dom 2026-05-20, Variant D — utnyttjar h3:s befintliga
 * flex-wrap-kontrakt utan ny grid-topologi).
 *
 * Civic-utility-stil: rektangulära taggar (2px radie), 11px versaler, dovt
 * färg-spektrum från befintliga tokens. INGA pills, INGA pastellchips, INGA
 * ikoner i taggar, INGA emoji (CLAUDE.md §5.2 + HANDOVER §0 + Klas verbatim).
 *
 * NY-modell (#293/#306, ADR 0042 Beslut E-amendment 2026-06-28): NY = OLÄST
 * (per-användar watermark), INTE tidsbaserat. Den föräldern beräknar
 * `createdAt > lastSeenJobsAt` mot den hämtade watermarken (`JobbResults`) och
 * skickar resultatet hit som `isNew`. Den tidigare tidsbaserade
 * (`publishedAt`-7d-fönster) NY-modellen + dess localStorage-high-water-mark är
 * BORTTAGNA — "X DAGAR"-färskhetstaggen (`freshnessLabel`) är recency-signalen
 * nu (NY ≠ recency, dubbleringen Klas flaggade är borta). Ren presentations-
 * komponent (ingen client-state) ⇒ Server Component.
 *
 * PR5 (Klas-feedback 2026-05-23 + CTO Val 4 Variant A): Sparad + Ansökt-taggar
 * (per-user-overlay via ADR 0063 batch-port). `isSaved`/`isApplied` är opt-in
 * flaggor — utelämnas för anonyma/list-yta utan auth → taggar visas inte.
 * Civic-utility: ingen "Inte sparad"-tagg.
 *
 * F4-13 (ADR 0076, 2026-06-19): den pre-pivot numeriska `matchScore` +
 * `MATCH_THRESHOLD`-tröskel-taggen är BORTTAGEN (Goodhart, ADR 0076). Den
 * graderade match-taggen renderas av `MatchChip` (`.jp-matchchip`) direkt i
 * `JobAdCard`, inte här.
 */

export interface JobTagsProps {
  /**
   * NY = oläst (per-användar watermark). Beräknas i föräldern
   * (`createdAt > lastSeenJobsAt`); `false` vid kall start (ingen watermark) /
   * anon, eller när annonsen kommit in före senaste besöket (#293/#306).
   */
  isNew: boolean;
  /**
   * Färskhets-etikett, server-beräknad från `publishedAt`. `null` när äldre än
   * 7 dygn (renderas inte). T.ex. "Idag", "2 dagar", "5 dagar".
   */
  freshnessLabel: string | null;
  /**
   * F6 P5 Punkt 2 PR5 — per-user-overlay-status (ADR 0063 batch-port).
   * Server-fetchad via `getJobAdStatusBatch` i list-page. Default false.
   */
  isSaved?: boolean;
  isApplied?: boolean;
}

export function JobTags({
  isNew,
  freshnessLabel,
  isSaved = false,
  isApplied = false,
}: JobTagsProps) {
  const t = useTranslations("jobads.ui");

  if (!isNew && !freshnessLabel && !isSaved && !isApplied) {
    return null;
  }

  return (
    <span className="jp-job-tags">
      {isNew && (
        <span
          className="jp-tag jp-tag--accent"
          data-tag="new"
          aria-label={t("tags.newAriaLabel")}
        >
          {t("tags.new")}
        </span>
      )}
      {freshnessLabel && (
        <span className="jp-tag jp-tag--neutral" data-tag="freshness">
          {freshnessLabel}
        </span>
      )}
      {isSaved && (
        <span className="jp-tag jp-tag--neutral" data-tag="saved">
          {t("tags.saved")}
        </span>
      )}
      {isApplied && (
        <span className="jp-tag jp-tag--neutral" data-tag="applied">
          {t("tags.applied")}
        </span>
      )}
    </span>
  );
}
