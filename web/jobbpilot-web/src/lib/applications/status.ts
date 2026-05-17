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
