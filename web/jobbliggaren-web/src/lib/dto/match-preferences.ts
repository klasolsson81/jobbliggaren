import { z } from "zod";

/**
 * F4-12 PR-B (ADR 0076) — derive-seed-svaret för "Föreslå utifrån en
 * yrkestitel"-bekvämligheten i matchnings-kortet. Speglar backend
 * `OccupationDerivationResult` / `OccupationCandidate`
 * (`Jobbliggaren.Application.JobAds.Abstractions`). Backend serialiserar
 * camelCase per ADR 0020 §6 (samma konvention som `TaxonomyTreeDto`).
 *
 * Seedet är ENBART ett förslag (propose-and-approve, ADR 0040 Beslut 4) —
 * inget skrivs förrän användaren sparar. Kortet behöver bara `conceptId` +
 * `label` för att kunna toggla rätt yrkesgrupp-kryssruta. `matchKind` +
 * `matchedOn` modelleras därför INTE: Zod är icke-strikt (ADR 0020 §4) och
 * strippar dem. Det är dessutom robust mot enum-serialiseringen — backend
 * har INGEN global `JsonStringEnumConverter`, så `matchKind` skickas som
 * heltal (0/1) på wire; genom att utelämna fältet slipper FE-DTO:n bero på
 * den fragila int-vs-string-detaljen.
 */

// Concept-id-format speglar backend `SearchCriteria`/validator-mönstret
// (1–32 tecken, [A-Za-z0-9_-]). Defense-in-depth mot ett korrupt svar —
// backend är sanningskälla.
const conceptIdSchema = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

// En härledd yrkesgrupp-kandidat. Endast conceptId + label behövs (se
// fil-doc) — övriga fält strippas av icke-strikt Zod.
//
// exp-per-occ (ADR 0079-amendment PR-4): den unified-kandidaten bär nu ett
// FRIVILLIGT CV-härlett `approximateYears`. Titel-derive-vägen
// (`deriveOccupations`) lämnar det `undefined` (en yrkestitel bär ingen
// erfarenhet); parsed-resume-vägen fyller det ur CV:t. `0` och `null` är
// semantiskt SKILDA (en parsad delårsroll vs ej angivet) — kollapsa dem aldrig.
// `.int().nullable().optional()` (icke-strikt Zod) speglar backend `int?` och
// tolererar att fältet saknas helt på titel-vägen.
export const occupationCandidateSchema = z.object({
  occupationGroupConceptId: conceptIdSchema,
  occupationGroupLabel: z.string().min(1),
  approximateYears: z.number().int().nullable().optional(),
});
export type OccupationCandidate = z.infer<typeof occupationCandidateSchema>;

// `title` echo:as verbatim av backend; `candidates` är REQUIRED (ej
// .default([])) — backend garanterar arrayen, tolerant default skulle maskera
// kontraktsdrift (samma dom som taxonomins `municipalities`).
export const occupationDerivationResultSchema = z.object({
  title: z.string(),
  candidates: z.array(occupationCandidateSchema),
});
export type OccupationDerivationResult = z.infer<
  typeof occupationDerivationResultSchema
>;

/**
 * Fas 4 onboarding (CTO Variant B 2026-06-21) — the non-PII SSYK occupation proposals returned
 * by `GET /api/v1/resumes/parsed/{id}/occupations` (backend `OccupationProposalDto[]`, wire
 * camelCase `{conceptId, label, matchedOn}`). Lets the match-setup wizard suggest occupations
 * from a freshly-uploaded-but-not-yet-promoted CV (the `latestRole` path only covers promoted
 * CVs). `matchedOn` is stripped by non-strict Zod (same as the title-derive candidates — the
 * card only needs conceptId + label to toggle the right group). Mapped to `OccupationCandidate`
 * at the api boundary so every consumer shares one candidate shape.
 *
 * exp-per-occ (ADR 0079-amendment PR-4): the proposal now also carries the
 * CV-derived `approximateYears` (`int | null` on the wire — `null` = not stated,
 * `0` = a parsed sub-year role; the two are semantically distinct and never
 * collapsed). `.int().nullable().optional()` mirrors backend `int?` and stays
 * tolerant of an older backend that omits the field (non-strict Zod, ADR 0020 §4).
 */
export const parsedResumeOccupationProposalSchema = z.object({
  conceptId: conceptIdSchema,
  label: z.string().min(1),
  approximateYears: z.number().int().nullable().optional(),
});
export const parsedResumeOccupationsSchema = z.array(
  parsedResumeOccupationProposalSchema
);
