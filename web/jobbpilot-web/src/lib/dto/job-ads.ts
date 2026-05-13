import { z } from "zod";
import { pagedResult } from "./_helpers";

// JobAdStatus speglar backend `JobAdStatus` SmartEnum
// (`JobbPilot.Domain.JobAds.JobAdStatus`). Synk med backend krävs vid
// status-värdes-tillägg per memory `project_crossref_badge_status`.
export const jobAdStatusSchema = z.enum(["Active", "Expired", "Archived"]);
export type JobAdStatus = z.infer<typeof jobAdStatusSchema>;

// JobSource speglar backend `JobSource` SmartEnum. `Manual`/`Platsbanken`
// är nuvarande Fas 2-värden; `LinkedIn`/`Eures` finns som SmartEnum-värden
// men exponeras inte ännu (ADR 0032 §"Out of scope").
export const jobSourceSchema = z.enum([
  "Manual",
  "Platsbanken",
  "LinkedIn",
  "Eures",
]);
export type JobSource = z.infer<typeof jobSourceSchema>;

// Sort-enum speglar backend `JobAdSortBy`. Värdena är sträng-namn (case-
// känsligt) per Minimal API enum-binding-konvention.
export const jobAdSortBySchema = z.enum([
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
]);
export type JobAdSortBy = z.infer<typeof jobAdSortBySchema>;

// Defense-in-depth-validering av URL-scheme (security-auditor F2-P10
// 2026-05-13 Blocker). Backend `JobAd.ValidateInputs` använder
// `Uri.TryCreate(UriKind.Absolute)` som accepterar `javascript:`,
// `data:`, `vbscript:` — vid render via `<a href={url}>` blir klick
// XSS-exekvering i autentiserad session (cookie-stöld → GDPR Art. 32-yta).
// FE blockerar via Zod refine. BE-tightening lyft som TD-80 (annan fas:
// Domain-invariant, ej F2-P10 FE-scope).
const jobAdUrlSchema = z
  .string()
  .refine((u) => u === "" || /^https?:\/\//i.test(u), {
    message: "URL måste börja med http:// eller https://",
  });

// Backend JobAdDto: Id, Title, CompanyName, Description, Url, Source, Status,
// PublishedAt, ExpiresAt, CreatedAt. Datum-fält är ISO 8601-strängar på wire
// per ADR 0020 §6.
export const jobAdDtoSchema = z.object({
  id: z.string(),
  title: z.string(),
  companyName: z.string(),
  description: z.string(),
  url: jobAdUrlSchema,
  source: jobSourceSchema,
  status: jobAdStatusSchema,
  publishedAt: z.string(),
  expiresAt: z.string().nullable(),
  createdAt: z.string(),
});
export type JobAdDto = z.infer<typeof jobAdDtoSchema>;

export const listJobAdsResultSchema = pagedResult(jobAdDtoSchema);
export type ListJobAdsResult = z.infer<typeof listJobAdsResultSchema>;

// Filter-form-schema för JobAdFilters Client Component. Speglar backend
// validator-regler (`ListJobAdsQueryValidator`) i FE för defense-in-depth +
// snabbare feedback. Concept-id-pattern måste matcha backend exakt.
const conceptIdPattern = /^[A-Za-z0-9_-]{1,32}$/;

export const jobAdFiltersSchema = z.object({
  // Tomma strängar tillåts — RHF skickar "" för icke-fyllda fält. Filtrering
  // bort sker innan URL-build.
  ssyk: z
    .string()
    .refine((v) => v === "" || conceptIdPattern.test(v), {
      message: "SSYK-koden måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
    }),
  region: z
    .string()
    .refine((v) => v === "" || conceptIdPattern.test(v), {
      message: "Regionkoden måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
    }),
  q: z
    .string()
    .refine((v) => v === "" || (v.length >= 2 && v.length <= 100), {
      message: "Söktexten måste vara 2–100 tecken.",
    }),
  sortBy: jobAdSortBySchema,
});
export type JobAdFiltersValues = z.infer<typeof jobAdFiltersSchema>;
