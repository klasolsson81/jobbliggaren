import type { ApplicationStatus, FollowUpOutcome } from "@/lib/types/applications";

export const STATUS_LABELS: Record<ApplicationStatus, string> = {
  Draft: "Utkast",
  Submitted: "Skickad",
  Acknowledged: "Bekräftad",
  InterviewScheduled: "Intervju bokad",
  Interviewing: "Pågående intervju",
  OfferReceived: "Erbjudande",
  Accepted: "Accepterad",
  Rejected: "Nekad",
  Withdrawn: "Återtagen",
  Ghosted: "Inget svar",
};

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

export function getStatusLabel(status: ApplicationStatus): string {
  return STATUS_LABELS[status] ?? status;
}

export function getAllowedTransitions(status: ApplicationStatus): ApplicationStatus[] {
  return ALLOWED_TRANSITIONS[status] ?? [];
}

export function isDestructiveTransition(target: ApplicationStatus): boolean {
  return DESTRUCTIVE_STATUSES.includes(target);
}

export const CHANNEL_LABELS: Record<string, string> = {
  Email: "E-post",
  LinkedIn: "LinkedIn",
  Phone: "Telefon",
  Other: "Övrigt",
};

export const FOLLOW_UP_OUTCOME_LABELS: Record<FollowUpOutcome, string> = {
  Pending: "Inväntar svar",
  Responded: "Svar mottaget",
  NoResponse: "Inget svar",
};

/**
 * Svensk etikett för jobbannonsens källa. Backend skickar "Platsbanken" |
 * "LinkedIn" | "Manual" (literal "Manual" projiceras för manuellt skapade
 * ansökningar — Source-fältet är struket per Klas STOPP 3a-villkor).
 */
const SOURCE_LABELS: Record<string, string> = {
  Platsbanken: "Platsbanken",
  LinkedIn: "LinkedIn",
  Manual: "Manuellt",
};

export function getSourceLabel(source: string): string {
  return SOURCE_LABELS[source] ?? source;
}

/**
 * Kort svenskt datum (sv-SE), t.ex. "18 maj 2026". Codebase-konvention
 * (Intl/`toLocaleDateString`) — ingen ny date-fns-dependency. Returnerar
 * null vid saknat/ogiltigt värde så anropare kan utelämna raden.
 */
export function formatSvDate(value: string | null | undefined): string | null {
  if (!value) return null;
  const d = new Date(value);
  if (isNaN(d.getTime())) return null;
  return d.toLocaleDateString("sv-SE", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}
