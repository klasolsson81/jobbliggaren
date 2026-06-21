import { z } from "zod";

/**
 * F4-12 PR-B (ADR 0076) — input-schema för `updateMatchPreferencesAction`.
 * Speglar backend `SetMatchPreferencesCommand` + dess validator.
 *
 * Full-replace-semantik: åtgärden skickar HELA den aktuella valda mängden per
 * dimension (PUT, idempotent). Alla tre arrayer är frivilliga och får vara
 * tomma — tom mängd = "inte angett" (ärlig not-assessed-state, aldrig ett fel,
 * ADR 0076). `.default([])` så en saknad nyckel binder till tom lista.
 *
 * conceptId-mönstret (1–32 tecken, [A-Za-z0-9_-]) + `.max(400)` speglar
 * backend `SearchCriteria.MaxConceptIds`-taket (defense-in-depth + DoS-skydd;
 * backend är sista barriären).
 */

const MAX_CONCEPT_IDS = 400;

const conceptIdString = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

const conceptIdList = z.array(conceptIdString).max(MAX_CONCEPT_IDS).default([]);

export const setMatchPreferencesSchema = z.object({
  preferredOccupationGroups: conceptIdList,
  preferredRegions: conceptIdList,
  // Spår 3 PR-D (ADR 0076-amendment 2026-06-21): kommun-axeln. Ort är EN
  // dimension i två granulariteter (region ∪ municipality, backend unionerar) —
  // den lyfts atomiskt med `preferredRegions` i ETT full-replace-PUT så ett
  // spar av regioner aldrig nollar angivna kommuner (CTO/architect NOTE-1).
  preferredMunicipalities: conceptIdList,
  preferredEmploymentTypes: conceptIdList,
});

export type SetMatchPreferencesInput = z.infer<
  typeof setMatchPreferencesSchema
>;
