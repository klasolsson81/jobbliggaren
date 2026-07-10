import type {
  ApplicationAttentionSignal,
  ApplicationStatus,
  FollowUpOutcome,
} from "@/lib/dto/applications";

export type BadgeVariant = "Info" | "Brand" | "Success" | "Warning" | "Danger" | "Neutral";

// Aligned with the Mina ansökningar 2a handoff §11 status-tag mapping (#683). Four
// corrections from the earlier drift:
//  - Submitted: Brand → Info  (SKICKAD in interaction-green collided with "green = interaction
//    only", DESIGN principles §5; two green tags blurred the signal).
//  - Ghosted:   Danger → Neutral (red over-signalled as a rejection, same red as Nekad).
//  - Acknowledged: Success → Brand.
//  - Draft:     Info → Neutral.
// The .jp-tag chassis renders these tags as visual siblings of /jobb's job-ad tags (which use a
// separate map, JOB_AD_STATUS_BADGE_VARIANT); this map + its data-status-variant attribute are
// applications-internal. Every status keeps colour + a text label (WCAG 1.4.1 — never colour alone).
export const STATUS_BADGE_VARIANT: Record<ApplicationStatus, BadgeVariant> = {
  Draft: "Neutral",
  Submitted: "Info",
  Acknowledged: "Brand",
  InterviewScheduled: "Warning",
  Interviewing: "Warning",
  OfferReceived: "Success",
  Accepted: "Success",
  Rejected: "Danger",
  Withdrawn: "Neutral",
  Ghosted: "Neutral",
};

export const ALLOWED_TRANSITIONS: Record<ApplicationStatus, ApplicationStatus[]> = {
  Draft: ["Submitted"],
  Submitted: ["Acknowledged", "Rejected", "Withdrawn"],
  Acknowledged: ["InterviewScheduled", "Rejected", "Withdrawn"],
  InterviewScheduled: ["Interviewing", "Withdrawn"],
  Interviewing: ["OfferReceived", "Rejected", "Withdrawn"],
  OfferReceived: ["Accepted", "Rejected", "Withdrawn"],
  Accepted: [],
  Rejected: [],
  Withdrawn: [],
  Ghosted: ["Submitted"],
};

export const DESTRUCTIVE_STATUSES: ApplicationStatus[] = ["Rejected", "Withdrawn"];

/**
 * Fast pipeline-ordning. Single source of truth — speglar backend-
 * pipelinens grupp-ordning. Tidigare duplicerad som PIPELINE_ORDER i
 * applications-pipeline.tsx; centraliserad här så app-row v3, modal,
 * statusbar och sektioner inte kan drifta isär (CLAUDE.md §9.1 DRY).
 * Detta är den REALA domän-ordningen — ersätter v3-prototypens
 * STATUS_ORDER-mock (no-mock-direktiv).
 */
export const PIPELINE_ORDER: ApplicationStatus[] = [
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
];

/**
 * De aktiva stegen på pipelinevägen (Utkast → Erbjudande). Resten
 * (Accepterad/Nekad/Återtagen/Ghosted) är terminala/vilande och grupperas under
 * "AVSLUT & VILANDE"-kickern i Lista-vyn (design 2a §5) och får `--jp-surface-2`
 * i stegrailen (§7). SSOT — delas av containern, stegrailen och Lista-sektionerna
 * så partitionen aldrig kan drifta isär (CLAUDE.md §9.1 DRY).
 */
export const ACTIVE_PIPELINE_STATUSES: ApplicationStatus[] = [
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
];

const ACTIVE_PIPELINE_STATUS_SET: ReadonlySet<ApplicationStatus> = new Set(
  ACTIVE_PIPELINE_STATUSES,
);

export function isActivePipelineStatus(status: ApplicationStatus): boolean {
  return ACTIVE_PIPELINE_STATUS_SET.has(status);
}

// ─── 2a action-affordanser (#630 PR 7, design §5/§8.4–8.5) ─────────────────

/**
 * Den aktiva vägens 7 steg (design §8.4): de 6 aktiva pipeline-stegen + målet
 * Accepterad. Detta är drawerns stegväljare — INTE PIPELINE_ORDER (10, railen)
 * och inte ACTIVE_PIPELINE_STATUSES (6, Lista-partitionen). SSOT för "nästa
 * steg"-härledningen nedan.
 */
export const ACTIVE_PATH_STATUSES: ApplicationStatus[] = [
  ...ACTIVE_PIPELINE_STATUSES,
  "Accepted",
];

/**
 * Statusmenyns "AVSLUT & VILANDE"-grupp (design §5): Accepterad/Nekad/Återtagen
 * + Ghosted, i PIPELINE_ORDER-ordning. Menyns övre grupp är
 * ACTIVE_PIPELINE_STATUSES (6). Alla 10 är alltid valbara — fria byten åt båda
 * håll (ADR 0092 D3).
 */
export const STATUS_MENU_CLOSED_GROUP: ApplicationStatus[] = [
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
];

/**
 * Drawerns "AVSLUTA ELLER PARKERA"-knappar (design §8.5): Nekad (dangertext),
 * Återtagen, Ghosted. Accepterad nås via stegväljarens steg 7, inte här.
 */
export const PARK_STATUSES: ApplicationStatus[] = [
  "Rejected",
  "Withdrawn",
  "Ghosted",
];

/**
 * "Flytta till {nästa steg}"-källan (design §5/§8.3, prototypens nextOf —
 * facit): nästa steg på den aktiva vägen; Ghosted → Skickad (återaktivering).
 * Terminala (Accepterad/Nekad/Återtagen) har inget nästa steg → ingen primär
 * CTA. Detta är en REN presentations-mappning (vilken knapp visas) — ALDRIG en
 * transitions-grind; backend tillåter alla byten (ADR 0092 D3) och statusmenyn
 * erbjuder alltid alla 10.
 */
const NEXT_STEP: Partial<Record<ApplicationStatus, ApplicationStatus>> = {
  Draft: "Submitted",
  Submitted: "Acknowledged",
  Acknowledged: "InterviewScheduled",
  InterviewScheduled: "Interviewing",
  Interviewing: "OfferReceived",
  OfferReceived: "Accepted",
  Ghosted: "Submitted",
};

export function nextStepOf(status: ApplicationStatus): ApplicationStatus | null {
  return NEXT_STEP[status] ?? null;
}

/**
 * Status → statusvariant-nyckel ("info"/"brand"/"success"/"warning"/"danger"/
 * "neutral") för `data-status-variant`-attributet. Återbrukar
 * STATUS_BADGE_VARIANT + PILL_VARIANT_CLASS (samma källa som status-taggen och
 * modalens status-block) så stegrailens 3px-toppkant färgkodas mot EXAKT samma
 * status-tokens — ingen ny token, ingen drift (CLAUDE.md §9.1 DRY, DESIGN.md).
 */
export function getStatusVariantKey(status: ApplicationStatus): string {
  return PILL_VARIANT_CLASS[STATUS_BADGE_VARIANT[status]];
}

/**
 * BadgeVariant → v3 `.jp-pill--{variant}`-suffix. Speglar
 * STATUS_BADGE_VARIANT-semantiken mot v3 .jp-pill-systemet
 * (HANDOVER §5.7). Delas av app-row v3 och ansökan-modalen så
 * status-pill aldrig drifter mellan list och detalj.
 */
export const PILL_VARIANT_CLASS: Record<BadgeVariant, string> = {
  Info: "info",
  Brand: "brand",
  Success: "success",
  Warning: "warning",
  Danger: "danger",
  Neutral: "neutral",
};

export function getStatusPillClass(status: ApplicationStatus): string {
  const variant = STATUS_BADGE_VARIANT[status];
  return `jp-pill jp-pill--${PILL_VARIANT_CLASS[variant]}`;
}

/**
 * Status → `[data-tag="status-{variant}"]`-attribut för den fyrkantiga
 * status-taggen i ansökningsraden (#336, slice 1). Återbrukar
 * STATUS_BADGE_VARIANT + PILL_VARIANT_CLASS så list-radens status-tagg,
 * .jp-pill-systemet och modalen aldrig kan drifta isär (CLAUDE.md §9.1 DRY).
 * Konsumeras på en `.jp-tag`-bas (kvadratisk, 2px radie, 11px versal) — ej
 * den rundade .jp-pill — så status läses som en `.jp-tag`-syskon till
 * /jobb-taggarna men förblir färgkodad för skannbarhet (design-reviewer-bind:
 * färg + textetikett, WCAG 1.4.1 inte färg-enbart).
 */
export function getStatusTagDataAttr(status: ApplicationStatus): string {
  const variant = STATUS_BADGE_VARIANT[status];
  return `status-${PILL_VARIANT_CLASS[variant]}`;
}

// User-facing enum labels resolve through next-intl. The same `t`-call
// signature works for both client (`useTranslations("applications.enums")`)
// and server (`useTranslations` in an RSC) — callers acquire `t` scoped to the
// `"applications.enums"` namespace and pass it in. The Swedish values live in
// `messages/sv/applications.json` (source of truth, typed via AppConfig).

// ApplicationStatus is a literal union -> direct lookup, exhaustive (no fallback
// needed). PIPELINE_ORDER above is the ordered key array for status dropdowns.
export function applicationStatusLabel(
  t: (key: `status.${ApplicationStatus}`) => string,
  status: ApplicationStatus,
): string {
  return t(`status.${status}`);
}

export function followUpOutcomeLabel(
  t: (key: `followUpOutcome.${FollowUpOutcome}`) => string,
  outcome: FollowUpOutcome,
): string {
  return t(`followUpOutcome.${outcome}`);
}

// Free-string-keyed enums (channel/source arrive as `string` from the DTO) ->
// validate against a known key tuple, fall back to the raw value.
export const CHANNEL_KEYS = ["Email", "LinkedIn", "Phone", "Other"] as const;
export type ChannelKey = (typeof CHANNEL_KEYS)[number];
export function channelLabel(
  t: (key: `channel.${ChannelKey}`) => string,
  channel: string,
): string {
  return (CHANNEL_KEYS as readonly string[]).includes(channel)
    ? t(`channel.${channel as ChannelKey}`)
    : channel;
}

/**
 * Svensk etikett för jobbannonsens källa. Backend skickar "Platsbanken" |
 * "LinkedIn" | "Manual" (literal "Manual" projiceras för manuellt skapade
 * ansökningar — Source-fältet är struket per Klas STOPP 3a-villkor).
 */
export const APPLICATION_SOURCE_KEYS = ["Platsbanken", "LinkedIn", "Manual"] as const;
export type ApplicationSourceKey = (typeof APPLICATION_SOURCE_KEYS)[number];
export function applicationSourceLabel(
  t: (key: `source.${ApplicationSourceKey}`) => string,
  source: string,
): string {
  return (APPLICATION_SOURCE_KEYS as readonly string[]).includes(source)
    ? t(`source.${source as ApplicationSourceKey}`)
    : source;
}

export function getAllowedTransitions(status: ApplicationStatus): ApplicationStatus[] {
  return ALLOWED_TRANSITIONS[status] ?? [];
}

export function isDestructiveTransition(target: ApplicationStatus): boolean {
  return DESTRUCTIVE_STATUSES.includes(target);
}

// ─── Attention feed (#343, ADR 0085) ──────────────────────────────────────
//
// The firing RULE lives on the backend (ApplicationAttentionEvaluator, SSOT —
// CLAUDE.md §5 / ADR 0071); the FE only consumes the already-decided
// `attentionSignal`. The constants below are pure DISPLAY metadata — feed order
// + i18n key + colour bucket — mirroring the existing STATUS_BADGE_VARIANT
// helpers (CLAUDE.md §9.1 DRY). They never decide WHETHER a signal fires.

/**
 * Display order of the pinned "Kräver åtgärd" feed — most urgent first. Mirrors
 * the backend enum order so the feed reads the same priority the evaluator
 * encodes (offer → overdue → draft-deadline → no-response → nudge). This is
 * DISPLAY ordering only, NOT the firing rule. "None" is not listed: a "None"/
 * undefined signal is never lifted into the feed.
 */
export const ATTENTION_SIGNAL_ORDER: Exclude<
  ApplicationAttentionSignal,
  "None"
>[] = [
  "OfferAwaitingReply",
  "OverdueFollowUp",
  "DraftDeadlineApproaching",
  "GhostSuggested",
  "NoResponseNudge",
  "SilentAfterInterview",
];

/**
 * Colour bucket for the feed item's leading indicator, emitted as a
 * `data-signal` attribute the `.jp-attention__dot` CSS resolves to a status
 * token. NEVER green (green = interaction/grade, design-reviewer bind), mirroring
 * design §11 "Urgensregler": offer → success, the overdue/draft-deadline pair →
 * warning, the no-response trio (ghost-suggest, no-response nudge, silent-after-
 * interview) → info. Colour only REINFORCES; the reason text carries the meaning
 * (WCAG 1.4.1 — not colour alone).
 */
export const ATTENTION_SIGNAL_BUCKET: Record<
  Exclude<ApplicationAttentionSignal, "None">,
  "success" | "warning" | "info"
> = {
  OfferAwaitingReply: "success",
  OverdueFollowUp: "warning",
  DraftDeadlineApproaching: "warning",
  GhostSuggested: "info",
  NoResponseNudge: "info",
  SilentAfterInterview: "info",
};

/**
 * Attention signal → `applications.ui.attention.reason.{signal}` i18n key.
 * Typed against the firing signals (no "None"), so the caller cannot ask for a
 * reason line for a non-firing signal.
 */
export function attentionReasonKey(
  signal: Exclude<ApplicationAttentionSignal, "None">,
): `reason.${Exclude<ApplicationAttentionSignal, "None">}` {
  return `reason.${signal}`;
}

/**
 * Narrowing guard: a signal that actually lifts an application into the feed.
 * Treats missing/undefined (deploy-skew) and "None" identically — no attention.
 */
export function isFiringSignal(
  signal: ApplicationAttentionSignal | undefined,
): signal is Exclude<ApplicationAttentionSignal, "None"> {
  return signal != null && signal !== "None";
}

/**
 * Tabell-vyns "I steget"-varningsfärgning (#630 PR 10, CTO-bind 2026-07-10): en
 * FIRANDE VÄNTE-signal, aldrig en dag-tröskel. `OfferAwaitingReply` exkluderas —
 * det är en positiv (success-axeln) signal, inte en väntan som ska rödmarkeras.
 * Alla övriga firande signaler (overdue/draft-deadline/ghost/no-response/silent)
 * betyder att steget stått still för länge → "I steget"-värdet får --jp-warning.
 * Ren display-härledning över den redan-beslutade `attentionSignal` (backend-SSOT,
 * ADR 0071) — den avgör aldrig OM en signal firar.
 */
export function isWaitingSignal(
  signal: ApplicationAttentionSignal | undefined,
): boolean {
  return isFiringSignal(signal) && signal !== "OfferAwaitingReply";
}
