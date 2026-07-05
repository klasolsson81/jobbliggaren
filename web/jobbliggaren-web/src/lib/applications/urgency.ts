import { daysSince } from "@/lib/i18n/relative-time";
import type { ApplicationDto } from "@/lib/dto/applications";

/**
 * Presentation-only urgency derivations for the 2a row (#630 PR 7, design §5/§11).
 *
 * The firing RULE stays on the backend (ApplicationAttentionEvaluator, SSOT —
 * CLAUDE.md §5 / ADR 0071): these helpers never decide WHETHER something needs
 * attention. They only turn the already-decided `attentionSignal` plus the raw
 * DTO scalars (`lastStatusChangeAt`, `lastFollowUpAt`, `jobAd.expiresAt`) into
 * the display numbers the design shows ("N dagar i steget", "N DGR UTAN SVAR",
 * "DEADLINE 6 JULI"). Data-grounded or omitted — a missing scalar (deploy-skew)
 * yields null, never a fabricated value (§5).
 */

/** Hela dagar i nuvarande steg (design §5 "N dagar i steget"). */
export function daysInStatus(
  lastStatusChangeAt: string | undefined,
  now: Date,
): number | null {
  if (lastStatusChangeAt == null) return null;
  return Math.max(0, daysSince(lastStatusChangeAt, now));
}

/**
 * Effektiv väntetid (ADR 0092 D5): dagar sedan senaste händelse ELLER senaste
 * uppföljning — det minsta av dem. Speglar backend-evaluatorns klocka
 * (`now − max(LastStatusChangeAt, LastFollowUpAt)`) för DISPLAY av
 * "N DGR UTAN SVAR"-taggen; tröskeln som avgör OM taggen visas är signalen.
 */
export function effectiveWaitDays(
  application: Pick<ApplicationDto, "lastStatusChangeAt" | "lastFollowUpAt">,
  now: Date,
): number | null {
  const sinceChange = daysInStatus(application.lastStatusChangeAt, now);
  if (sinceChange == null) return null;
  const sinceFollowUp =
    application.lastFollowUpAt != null
      ? Math.max(0, daysSince(application.lastFollowUpAt, now))
      : null;
  return sinceFollowUp == null ? sinceChange : Math.min(sinceChange, sinceFollowUp);
}

/**
 * Bråttom-taggens innehåll (design §11): strukturerad data — konsumenten
 * renderar med next-intl + useFormatter (ren, testbar utan i18n-plumbing).
 * `variant` matchar ATTENTION_SIGNAL_BUCKET-axeln (aldrig grönt för
 * interaktion; offer-taggen "SVAR SENAST {datum}" är DEFERRAD per ADR 0092 D5,
 * svarsfristfältet finns inte).
 */
export type UrgencyTag =
  | { kind: "deadline"; variant: "warning"; dateIso: string }
  | { kind: "waitDays"; variant: "info"; days: number }
  | { kind: "sinceInterview"; variant: "info"; days: number };

export function urgencyTagFor(
  application: ApplicationDto,
  now: Date,
): UrgencyTag | null {
  switch (application.attentionSignal) {
    case "DraftDeadlineApproaching": {
      // Prototyp: "DEADLINE 6 JULI" (warning). Datakälla = annonsens sista
      // ansökningsdag; saknas den (manuell ansökan utan datum) → ingen tagg.
      const dateIso = application.jobAd?.expiresAt ?? null;
      return dateIso != null
        ? { kind: "deadline", variant: "warning", dateIso }
        : null;
    }
    case "GhostSuggested":
    case "NoResponseNudge": {
      // Prototyp: "{N} DGR UTAN SVAR" (info) — N = effektiv väntetid (D5).
      const days = effectiveWaitDays(application, now);
      return days != null ? { kind: "waitDays", variant: "info", days } : null;
    }
    case "SilentAfterInterview": {
      // Prototyp: "{N} DGR SEDAN INTERVJUN" (info).
      const days = effectiveWaitDays(application, now);
      return days != null
        ? { kind: "sinceInterview", variant: "info", days }
        : null;
    }
    // OfferAwaitingReply: daterad "SVAR SENAST"-tagg kräver svarsfristfältet
    // (deferrat, ADR 0092 D5) — kortets orsaksrad bär signalen. OverdueFollowUp:
    // ingen daterad tagg i designens taggfamilj.
    default:
      return null;
  }
}
