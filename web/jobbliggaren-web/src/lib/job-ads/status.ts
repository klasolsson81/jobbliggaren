import type { JobAdStatus, JobSource, JobAdSortBy } from "@/lib/dto/job-ads";

export type BadgeVariant =
  | "Info"
  | "Brand"
  | "Success"
  | "Warning"
  | "Danger"
  | "Neutral";

// Active/Expired/Archived speglar backend SmartEnum exakt — synk krävs vid
// status-tillägg (memory `project_crossref_badge_status`).
export const JOB_AD_STATUS_BADGE_VARIANT: Record<JobAdStatus, BadgeVariant> = {
  Active: "Success",
  Expired: "Warning",
  Archived: "Neutral",
};

// User-facing enum labels resolve through next-intl. The same `t`-call
// signature works for both client (`useTranslations("jobads.enums")`) and
// server (`useTranslations` in an RSC) — callers acquire `t` scoped to the
// `"jobads.enums"` namespace and pass it in. The Swedish values live in
// `messages/sv/jobads.json` (source of truth, typed via AppConfig).

// JobAdStatus / JobSource / JobAdSortBy are literal unions -> direct lookup,
// exhaustive (no fallback needed).
export function jobAdStatusLabel(
  t: (key: `status.${JobAdStatus}`) => string,
  status: JobAdStatus,
): string {
  return t(`status.${status}`);
}

export function jobSourceLabel(
  t: (key: `source.${JobSource}`) => string,
  source: JobSource,
): string {
  return t(`source.${source}`);
}

// Ordered key array for the sort selector — preserves the previous
// JOB_AD_SORT_LABELS declaration order so the dropdown order is unchanged.
export const JOB_AD_SORT_KEYS: readonly JobAdSortBy[] = [
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
  "Relevance",
  "MatchDesc",
];

export function jobAdSortLabel(
  t: (key: `sort.${JobAdSortBy}`) => string,
  sortBy: JobAdSortBy,
): string {
  return t(`sort.${sortBy}`);
}
