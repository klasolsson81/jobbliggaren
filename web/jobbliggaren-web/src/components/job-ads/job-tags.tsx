"use client";

import { useLastSeenJobs } from "./use-last-seen-jobs";

/**
 * Tagg-rad för jobbannons-rad (`.jp-job`). Generisk subkomponent (Klas pre-F6
 * Prompt 1) — designad för återanvändning i Prompt 2–4 (sannolikt jp-app +
 * toolbar/detalj). Placeras inom `.jp-job__title` h3 och högerjusteras via
 * `margin-left:auto` (CTO-dom 2026-05-20, Variant D — utnyttjar h3:s
 * befintliga flex-wrap-kontrakt utan ny grid-topologi).
 *
 * Civic-utility-stil: rektangulära taggar (2px radie), 11px versaler, dovt
 * färg-spektrum från befintliga tokens. INGA pills, INGA pastellchips, INGA
 * ikoner i taggar, INGA emoji (CLAUDE.md §5.2 + HANDOVER §0 + Klas verbatim).
 *
 * NY-modell (Klas-direktiv 2026-05-20): high-water mark — NY visas på allt
 * med `publishedAtMs > lastSeen` (tidsstämpel för senaste sid-besök på /jobb).
 * Server-flagga `showNew` (= backend `isNew`, ≤7d-cap) består som defensivt
 * golv: även om lastSeen är gammalt visas aldrig NY på annonser äldre än 7d.
 * Färskhet beräknas server-side i parent (JobAdCard).
 *
 * PR5 (Klas-feedback 2026-05-23 + CTO Val 4 Variant A): utvidgad med
 * Sparad + Ansökt-taggar (per-user-overlay via ADR 0063 batch-port).
 * `isSaved`/`isApplied` är opt-in flaggor — utelämnas för anonyma/list-yta
 * utan auth → taggar visas inte. Civic-utility: ingen "Inte sparad"-tagg.
 *
 * F4-13 (ADR 0076, 2026-06-19): den pre-pivot numeriska `matchScore` +
 * `MATCH_THRESHOLD`-tröskel-taggen ("Bra match" vid ≥75) är BORTTAGEN — en
 * opak poäng/tröskel är den förbjudna Goodhart-modellen (ADR 0076). Den
 * graderade match-taggen renderas nu av `MatchChip` (`.jp-matchchip`) direkt
 * i `JobAdCard`, inte här.
 */

export interface JobTagsProps {
  /** Server-cap `isNew` (publishedAt inom 7-dygnsfönstret per ADR 0042 E). */
  showNew: boolean;
  /**
   * `publishedAt` som ms sedan epoch (parsed server-side i parent för
   * hydration-stabilitet). Jämförs med `lastSeen` för att avgöra NY-render.
   */
  publishedAtMs: number;
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
  showNew,
  publishedAtMs,
  freshnessLabel,
  isSaved = false,
  isApplied = false,
}: JobTagsProps) {
  const lastSeen = useLastSeenJobs();

  const renderNew = showNew && publishedAtMs > lastSeen;

  if (!renderNew && !freshnessLabel && !isSaved && !isApplied) {
    return null;
  }

  return (
    <span className="jp-job-tags">
      {renderNew && (
        <span className="jp-tag jp-tag--accent" data-tag="new">
          Ny
        </span>
      )}
      {freshnessLabel && (
        <span className="jp-tag jp-tag--neutral" data-tag="freshness">
          {freshnessLabel}
        </span>
      )}
      {isSaved && (
        <span className="jp-tag jp-tag--neutral" data-tag="saved">
          Sparad
        </span>
      )}
      {isApplied && (
        <span className="jp-tag jp-tag--neutral" data-tag="applied">
          Ansökt
        </span>
      )}
    </span>
  );
}
