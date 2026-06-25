import type { ApplicationStatus, FollowUpOutcome } from "@/lib/types/applications";

export type BadgeVariant = "Info" | "Brand" | "Success" | "Warning" | "Danger" | "Neutral";

export const STATUS_BADGE_VARIANT: Record<ApplicationStatus, BadgeVariant> = {
  Draft: "Info",
  Submitted: "Brand",
  Acknowledged: "Success",
  InterviewScheduled: "Warning",
  Interviewing: "Warning",
  OfferReceived: "Success",
  Accepted: "Success",
  Rejected: "Danger",
  Withdrawn: "Neutral",
  Ghosted: "Danger",
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
