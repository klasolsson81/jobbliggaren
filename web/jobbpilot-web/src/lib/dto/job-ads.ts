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
// känsligt) per Minimal API enum-binding-konvention. `Relevance` tillagd
// ADR 0042 Beslut D — kräver q non-null (backend 400-skydd via
// ListJobAdsQueryValidator; UI får ej erbjuda Relevance utan söktext).
export const jobAdSortBySchema = z.enum([
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
  "Relevance",
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
// PublishedAt, ExpiresAt, CreatedAt, IsNew. Datum-fält är ISO 8601-strängar
// på wire per ADR 0020 §6. `isNew` (ADR 0042 Beslut E) är runtime-
// presentationskontext: true om PublishedAt >= ListJobAdsQuery.Since,
// false när Since ej angivet.
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
  isNew: z.boolean(),
});
export type JobAdDto = z.infer<typeof jobAdDtoSchema>;

export const listJobAdsResultSchema = pagedResult(jobAdDtoSchema);
export type ListJobAdsResult = z.infer<typeof listJobAdsResultSchema>;

// ADR 0042 Beslut C — typeahead. `GET /api/v1/job-ads/suggest` returnerar
// en ren `string[]` (distinkta aktiva titel-prefix-träffar, capade backend).
export const suggestJobAdTermsResultSchema = z.array(z.string());
export type SuggestJobAdTermsResult = z.infer<
  typeof suggestJobAdTermsResultSchema
>;

// Min prefix-längd för typeahead (speglar backend SuggestJobAdTermsQuery-
// validator MinimumLength(2) — UI skickar ej request under detta, backend
// 400 är sista barriär). Max speglar q-fältets 100-tak.
export const SUGGEST_MIN_PREFIX = 2;
export const SUGGEST_DEBOUNCE_MS = 300;

// ADR 0042 Beslut B — maxantal-cap per taxonomilista. Speglar backend
// SearchCriteria.MaxConceptIds (=10). UI visar gränsen i copy och blockerar
// tillägg över taket; backend-cap är sista barriär.
export const MAX_CONCEPT_IDS = 10;

// Filter-form-schema för JobAdFilters Client Component. Speglar backend
// validator-regler (`ListJobAdsQueryValidator`) i FE för defense-in-depth +
// snabbare feedback. Concept-id-pattern måste matcha backend exakt.
const conceptIdPattern = /^[A-Za-z0-9_-]{1,32}$/;

// ADR 0042 Beslut B — per-element concept-id-validering + maxantal-cap.
// Speglar `ListJobAdsQueryValidator` (RuleForEach + MaxConceptIds-cap).
// Domain (`SearchCriteria.Create`) är sanningskällan; detta är defense-in-
// depth + snabb feedback. Tom lista = "inget filter" (Beslut B.3 tom-invariant).
const conceptIdListSchema = z
  .array(z.string())
  .max(MAX_CONCEPT_IDS, `Max ${MAX_CONCEPT_IDS} val per lista.`)
  .refine((list) => list.every((v) => conceptIdPattern.test(v)), {
    message:
      "Varje kod måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
  });

export const jobAdFiltersSchema = z
  .object({
    ssyk: conceptIdListSchema,
    region: conceptIdListSchema,
    // Tom sträng tillåts — okänt fält. Filtrering bort sker innan URL-build.
    q: z
      .string()
      .refine((v) => v === "" || (v.length >= 2 && v.length <= 100), {
        message: "Söktexten måste vara 2–100 tecken.",
      }),
    sortBy: jobAdSortBySchema,
  })
  // ADR 0042 Beslut D — Relevance kräver söktext. Fail-fast i UI så backend
  // 400 aldrig triggas (backend-validatorn är sista barriär).
  .refine((v) => v.sortBy !== "Relevance" || v.q.trim().length >= 2, {
    message: "Relevans-sortering kräver en söktext på minst 2 tecken.",
    path: ["sortBy"],
  });
export type JobAdFiltersValues = z.infer<typeof jobAdFiltersSchema>;
