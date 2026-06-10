import { z } from "zod";

/**
 * ADR 0043 — Taxonomi-ACL för sök-ytan. Speglar backend
 * `TaxonomyTreeDto` / `TaxonomyLabelDto`
 * (`JobbPilot.Application.JobAds.Queries.GetTaxonomyTree`). Backend
 * serialiserar camelCase per ADR 0020 §6 (samma konvention som
 * `JobAdDto`).
 *
 * Sök-ytan visar svenska namn i hierarkiska väljare; concept-id försvinner
 * ur UI:t (Anticorruption Layer, Evans 2003 kap. 14). `onChange` emitterar
 * fortfarande concept-id `string[]` till URL/VO — ADR 0042 Beslut B-
 * domänkontraktet är OFÖRÄNDRAT. Variant A-scope (ADR 0043 Beslut E): Län
 * (enkelnivå, ingen kommun) + Yrkesområde→Yrke (tvånivå).
 */

// Concept-id-format speglar backend `SearchCriteria`/validator-mönstret
// (1–32 tecken, [A-Za-z0-9_-]). Defense-in-depth mot en korrupt snapshot —
// backend är sanningskälla.
const conceptIdSchema = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

// Län (JobTech `region`, ~21). Enkelnivå — ingen kommun (ADR 0043
// Beslut E payload-verifierings-trigger).
export const taxonomyRegionSchema = z.object({
  conceptId: conceptIdSchema,
  label: z.string().min(1),
});
export type TaxonomyRegion = z.infer<typeof taxonomyRegionSchema>;

// Yrkesgrupp (JobTech `ssyk-level-4`, ~400). conceptId matchar
// `job_ads.occupation_group_concept_id` → PRIMÄRT yrke-filter (ADR 0067
// Beslut 1, Platsbanken-paritet). occupation-name (yrke) konsumeras EJ av
// FE — det är recall-substrat backend-side (FTS-synonym-grenen) och
// exkluderas ur FE-DTO:n: en ACL modellerar vad konsumenten (pickern)
// behöver, inte källans interna modell (Evans 2003 kap. 14).
export const taxonomyOccupationGroupSchema = z.object({
  conceptId: conceptIdSchema,
  label: z.string().min(1),
});
export type TaxonomyOccupationGroup = z.infer<
  typeof taxonomyOccupationGroupSchema
>;

// Yrkesområde (JobTech `occupation-field`, ~21) med underordnade yrkesgrupper.
export const taxonomyOccupationFieldSchema = z.object({
  conceptId: conceptIdSchema,
  label: z.string().min(1),
  occupationGroups: z.array(taxonomyOccupationGroupSchema),
});
export type TaxonomyOccupationField = z.infer<
  typeof taxonomyOccupationFieldSchema
>;

export const taxonomyTreeSchema = z.object({
  regions: z.array(taxonomyRegionSchema),
  occupationFields: z.array(taxonomyOccupationFieldSchema),
});
export type TaxonomyTree = z.infer<typeof taxonomyTreeSchema>;

// Reverse-lookup-rad: concept-id → visningsnamn. Okänt id → backend
// returnerar `"Okänd kod (<id>)"` (graceful degradation, ADR 0043
// Beslut B; aldrig null/throw). conceptId valideras INTE mot
// concept-id-pattern här — vid stale snapshot kan ett sparat id ha annat
// format än det nuvarande mönstret; label-strängen renderas som ren text
// (security-auditor FE-flagga 2026-05-17).
export const taxonomyLabelSchema = z.object({
  conceptId: z.string(),
  label: z.string(),
});
export type TaxonomyLabel = z.infer<typeof taxonomyLabelSchema>;

export const taxonomyLabelsResultSchema = z.array(taxonomyLabelSchema);
export type TaxonomyLabelsResult = z.infer<typeof taxonomyLabelsResultSchema>;
