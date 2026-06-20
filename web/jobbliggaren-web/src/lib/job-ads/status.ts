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

// F4-16 (design-reviewer F4-14 Minor, fold-now) — KANONISK match-sort-label.
// Sort-väljarens live-`<option>` (JobbResultsToolbar) konsumerar SAMMA konstant,
// så strängarna ALDRIG kan drifta isär (jobbpilot-design-copy: ett koncept = en
// sträng). Tidigare divergens ("Bästa matchning" här vs "Sortera efter
// matchning" i väljaren) var en latent fälla — nu en SPOT.
export const MATCH_SORT_LABEL = "Sortera efter matchning";

export const JOB_AD_SORT_LABELS: Record<JobAdSortBy, string> = {
  PublishedAtDesc: "Nyast först",
  PublishedAtAsc: "Äldst först",
  // Civic-utility-copy (Klas 2026-05-17): dubbelt "sista" var otydligt.
  // Användar-centrerad, parallell med Nyast/Äldst — visar avsikten
  // (hinna söka) istället för datumriktningen. Enum oförändrad.
  ExpiresAtDesc: "Stänger senare",
  ExpiresAtAsc: "Stänger snart",
  // ADR 0042 Beslut D — endast valbar med söktext (se JobAdFilters).
  Relevance: "Mest relevant",
  // F4-14/F4-16 (ADR 0076) — match-sort. I recent-search-/SavedSearch-ytan
  // visas denna label aldrig (backend mappar MatchDesc → PublishedAtDesc för
  // hash/capture). Posten finns för Record<JobAdSortBy>-uttömmande täckning;
  // F4-16 folder den till den kanoniska strängen så väljaren och labeln aldrig
  // kan drifta isär.
  MatchDesc: MATCH_SORT_LABEL,
};

export function getJobAdSortLabel(sortBy: JobAdSortBy): string {
  return JOB_AD_SORT_LABELS[sortBy] ?? sortBy;
}
