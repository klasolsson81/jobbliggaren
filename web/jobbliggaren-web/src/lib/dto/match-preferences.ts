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
export const occupationCandidateSchema = z.object({
  occupationGroupConceptId: conceptIdSchema,
  occupationGroupLabel: z.string().min(1),
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
