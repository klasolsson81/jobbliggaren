import { z } from "zod";
import { pagedResultWithTotalPages } from "@/lib/dto/_helpers";

/**
 * #560 PR-3 (CTO Fork G5/G6) — criteria-based company watches ("smarta bevakningar"). Zod mirrors of
 * the backend DTOs served under `/api/v1/me/company-watch-criteria` (ADR 0020 single-source; backend
 * serialises camelCase). A criterion is a saved predicate over two RAW code axes — SNI branches and
 * kommun codes — LEAVES ONLY on the wire (the picker expands a section/division/whole-län selection to
 * its leaf codes FE-side; the write path never accepts a group code).
 *
 * The codes are the user's own criterion-PII, returned only to their owner over an auth-gated /me
 * route. They are validated by the backend against the SAME SCB reference catalog the picker renders,
 * so the FE schemas keep the code shape deliberately loose (`z.string()`): the reference tree is the
 * authority on what is a valid code, not a regex here, and an over-strict pattern would only mask a
 * legitimate catalog value the backend accepts.
 */

// ── The saved criterion (GET /) ─────────────────────────────────────────────

/**
 * One criterion as the owner sees it — RAW codes + the user's optional label (mirrors backend
 * `CompanyWatchCriterionDto`). The human display-label ("Dataprogrammering m.fl. · Stockholm m.fl.")
 * is deliberately NOT resolved server-side: the FE already holds the reference tree and derives it
 * there (`lib/company-criteria/display-label.ts`) — a second label authority could only drift.
 * `label` is null when the user gave the criterion no name.
 */
export const companyWatchCriterionSchema = z.object({
  id: z.string(),
  sniCodes: z.array(z.string()),
  municipalityCodes: z.array(z.string()),
  label: z.string().nullable(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type CompanyWatchCriterion = z.infer<typeof companyWatchCriterionSchema>;

/** `GET /company-watch-criteria` returns a bare array (unpaginated — hard-capped at 20 per user). */
export const listCompanyWatchCriteriaResultSchema = z.array(companyWatchCriterionSchema);
export type ListCompanyWatchCriteriaResult = z.infer<
  typeof listCompanyWatchCriteriaResultSchema
>;

// ── The SCB reference tree (GET /reference) ─────────────────────────────────

// A single reference leaf/node: SCB code + Swedish name. Both required (the picker renders the name;
// a missing name would render a blank checkbox row).
const sniLeafSchema = z.object({ code: z.string(), name: z.string() });

const sniDivisionSchema = z.object({
  code: z.string(),
  name: z.string(),
  leaves: z.array(sniLeafSchema),
});

const sniSectionSchema = z.object({
  code: z.string(),
  name: z.string(),
  divisions: z.array(sniDivisionSchema),
});

const kommunSchema = z.object({ code: z.string(), name: z.string() });

const lanSchema = z.object({
  code: z.string(),
  name: z.string(),
  kommuner: z.array(kommunSchema),
});

/**
 * The full picker tree (mirrors backend `CriterionReferenceDto`): SNI 2025 sections → divisions →
 * leaves, and län → kommuner. Version stamps are surfaced so a stale FE cache is diagnosable. Served
 * with ETag + `Cache-Control: private` (auth-gated; the taxonomy-endpoint mold), ~100 kB, fetched
 * server-side per render.
 */
export const criterionReferenceSchema = z.object({
  sniVersion: z.string(),
  kommunVersion: z.string(),
  sni: z.array(sniSectionSchema),
  lan: z.array(lanSchema),
});
export type CriterionReference = z.infer<typeof criterionReferenceSchema>;
export type SniSection = z.infer<typeof sniSectionSchema>;
export type SniDivision = z.infer<typeof sniDivisionSchema>;
export type SniLeaf = z.infer<typeof sniLeafSchema>;
export type Lan = z.infer<typeof lanSchema>;
export type Kommun = z.infer<typeof kommunSchema>;

// ── The magnitude (headline count + live preview) ───────────────────────────

/**
 * The honest magnitude of a criterion (mirrors backend `CriterionMatchMagnitudeDto`): `magnitude` is
 * exact when `saturated` is false; when true the truth is "10 000 or more" and the copy MUST render
 * "10 000+", never the bare number (#859: a rendered magnitude must be true). This is the ONLY honest
 * headline number — never the browse page's `totalCount` (a pagination quantity capped at 2000).
 */
export const criterionMagnitudeSchema = z.object({
  magnitude: z.number().int().nonnegative(),
  saturated: z.boolean(),
});
export type CriterionMagnitude = z.infer<typeof criterionMagnitudeSchema>;

// ── The register browse (GET /{id}/companies) ───────────────────────────────

/**
 * One ACTIVE register company matching the criterion (mirrors backend `CompanyBrowseDto`). The
 * personnummer guard is applied backend-side (ADR 0087 D8(c)): a personnummer-shaped sole-prop org.nr
 * arrives as `organizationNumber: null` + `isProtectedIdentity: true` — the raw value never crosses
 * the wire. `seatMunicipalityCode` is the company's REGISTERED SEAT (säteskommun), a 4-digit SCB code
 * with a load-bearing leading zero ("0180" = Stockholm) — a string, never parsed to int.
 */
export const companyBrowseSchema = z.object({
  organizationNumber: z.string().nullable(),
  isProtectedIdentity: z.boolean(),
  name: z.string(),
  seatMunicipalityCode: z.string(),
  seatMunicipalityName: z.string().nullable(),
  sniCodes: z.array(z.string()),
});
export type CompanyBrowse = z.infer<typeof companyBrowseSchema>;

/**
 * The composed browse response (mirrors the Api's `CompanyBrowseResponse`): the paginated page and the
 * honest magnitude, side by side — so the FE can never mistake the pagination `totalCount` for the
 * magnitude. `companies.totalCount` SATURATES at 2000 (max 100 pages × 20) and is a pagination
 * quantity ONLY; the honest headline number is `magnitude`.
 */
export const companyBrowseResponseSchema = z.object({
  companies: pagedResultWithTotalPages(companyBrowseSchema),
  magnitude: criterionMagnitudeSchema,
});
export type CompanyBrowseResponse = z.infer<typeof companyBrowseResponseSchema>;

/** `POST /` returns the created criterion's id. */
export const createCriterionResultSchema = z.object({ id: z.string() });

// ── The wire predicate (create / update / preview input) ────────────────────

/**
 * The criterion predicate as it travels on the wire: two raw code lists, LEAVES ONLY. Shared by
 * create, PATCH-update and the live magnitude-preview so all three carry the same shape.
 */
export interface CriterionPredicateInput {
  readonly sniCodes: ReadonlyArray<string>;
  readonly municipalityCodes: ReadonlyArray<string>;
}
