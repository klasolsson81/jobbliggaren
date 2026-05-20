"use client";

import { useReadJobAds } from "./use-read-job-ads";

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
 * Klient-komponent p.g.a. NY-taggens localStorage-driven "läst"-state. NY
 * server-snapshot = `showNew` (= `isNew`-flaggan från server, ADR 0042 Beslut E).
 * Vid hydration läses localStorage och NY försvinner om annonsen är markerad
 * läst. Färskhet och match-tröskel beräknas server-side i parent (JobAdCard)
 * och skickas som strängar/numer — undviker hydration-mismatch på datum.
 */

export interface JobTagsProps {
  /** Stabilt id för läst-state-uppslag i localStorage. */
  jobAdId: string;
  /** Server-flagga `isNew` (publishedAt inom 7-dygnsfönstret per ADR 0042 E). */
  showNew: boolean;
  /**
   * Färskhets-etikett, server-beräknad från `publishedAt`. `null` när äldre än
   * 7 dygn (renderas inte). T.ex. "Idag", "2 dagar", "5 dagar".
   */
  freshnessLabel: string | null;
  /**
   * Match-score från CV-domän. Renderar "Bra match" om >= MATCH_THRESHOLD.
   * TODO: Fas 4 — koppla mot riktig match-domän + tröskel-beslut Klas
   * (ADR 0053 amendment — match-score är Fas 4-gated, domän saknas idag).
   * I Prompt 1 alltid `undefined` → taggen renderas aldrig live.
   */
  matchScore?: number;
}

const MATCH_THRESHOLD = 75;

export function JobTags({
  jobAdId,
  showNew,
  freshnessLabel,
  matchScore,
}: JobTagsProps) {
  const { isRead } = useReadJobAds();

  const renderNew = showNew && !isRead(jobAdId);
  const renderMatch =
    matchScore !== undefined && matchScore >= MATCH_THRESHOLD;

  if (!renderNew && !freshnessLabel && !renderMatch) {
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
      {renderMatch && (
        <span className="jp-tag jp-tag--brand" data-tag="match">
          Bra match
        </span>
      )}
    </span>
  );
}

/**
 * Server-side helper — beräknar färskhets-etikett från `publishedAt` (ISO).
 * Returnerar `null` om annonsen är äldre än 7 dygn. Anropas i RSC-parent
 * (JobAdCard) så strängvärdet är stabilt mellan server-render och hydration.
 */
export function computeFreshnessLabel(
  publishedAtIso: string,
  nowMs: number = Date.now(),
): string | null {
  const publishedMs = Date.parse(publishedAtIso);
  if (!Number.isFinite(publishedMs)) return null;
  const ageDays = Math.floor((nowMs - publishedMs) / (24 * 60 * 60 * 1000));
  if (ageDays < 0 || ageDays > 7) return null;
  if (ageDays === 0) return "Idag";
  if (ageDays === 1) return "1 dag";
  return `${ageDays} dagar`;
}
