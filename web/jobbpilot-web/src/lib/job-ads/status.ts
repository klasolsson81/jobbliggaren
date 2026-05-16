import type { JobAdStatus, JobSource, JobAdSortBy } from "@/lib/dto/job-ads";

// Civic-utility-tonad svensk copy. Active/Expired/Archived speglar backend
// SmartEnum exakt — synk krävs vid status-tillägg (memory
// `project_crossref_badge_status`).
export const JOB_AD_STATUS_LABELS: Record<JobAdStatus, string> = {
  Active: "Aktiv",
  Expired: "Utgången",
  Archived: "Arkiverad",
};

export type BadgeVariant =
  | "Info"
  | "Brand"
  | "Success"
  | "Warning"
  | "Danger"
  | "Neutral";

export const JOB_AD_STATUS_BADGE_VARIANT: Record<JobAdStatus, BadgeVariant> = {
  Active: "Success",
  Expired: "Warning",
  Archived: "Neutral",
};

export function getJobAdStatusLabel(status: JobAdStatus): string {
  return JOB_AD_STATUS_LABELS[status] ?? status;
}

export const JOB_SOURCE_LABELS: Record<JobSource, string> = {
  Manual: "Egen",
  Platsbanken: "Platsbanken",
  LinkedIn: "LinkedIn",
  Eures: "EURES",
};

export function getJobSourceLabel(source: JobSource): string {
  return JOB_SOURCE_LABELS[source] ?? source;
}

export const JOB_AD_SORT_LABELS: Record<JobAdSortBy, string> = {
  PublishedAtDesc: "Nyast först",
  PublishedAtAsc: "Äldst först",
  ExpiresAtDesc: "Sist sista ansökningsdag",
  ExpiresAtAsc: "Tidigast sista ansökningsdag",
  // ADR 0042 Beslut D — endast valbar med söktext (se JobAdFilters).
  Relevance: "Mest relevant",
};

export function getJobAdSortLabel(sortBy: JobAdSortBy): string {
  return JOB_AD_SORT_LABELS[sortBy] ?? sortBy;
}
