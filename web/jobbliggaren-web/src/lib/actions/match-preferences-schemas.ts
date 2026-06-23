import { z } from "zod";
import type { useTranslations } from "next-intl";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). This schema carries no
// user-facing validation messages of its own — the conceptId/length guards are
// structural (defense-in-depth; backend is the authoritative barrier) — so the
// translator is accepted for factory-shape consistency but currently unused.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

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

// STEG 3 / ADR 0079 (Beslut 1): a single profile-level "antal års erfarenhet".
// Optional + nullable — `null` is the honest "not stated" state (never 0,
// which would mean "stated zero years"). 0..70 mirrors the backend validator
// (a sane human-career bound + a DoS-/typo-guard; backend is the last barrier).
const EXPERIENCE_YEARS_MIN = 0;
const EXPERIENCE_YEARS_MAX = 70;

export function makeSetMatchPreferencesSchema(_t: ValidationTranslator) {
  return z.object({
    preferredOccupationGroups: conceptIdList,
    preferredRegions: conceptIdList,
    // Spår 3 PR-D (ADR 0076-amendment 2026-06-21): kommun-axeln. Ort är EN
    // dimension i två granulariteter (region ∪ municipality, backend unionerar) —
    // den lyfts atomiskt med `preferredRegions` i ETT full-replace-PUT så ett
    // spar av regioner aldrig nollar angivna kommuner (CTO/architect NOTE-1).
    preferredMunicipalities: conceptIdList,
    preferredEmploymentTypes: conceptIdList,
    // STEG 3 / ADR 0079 (Beslut 1): CV-seeded, editable, trusted skill chips.
    // A list of skill concept-ids (same conceptId format as the other axes;
    // full-replace, optional, defaults to empty). Like the other dimensions
    // this MUST be threaded through every caller and pre-filled from the
    // profile read — saving any other dimension would otherwise zero it
    // (the full-replace page-wipe bug that bit region/kommun).
    preferredSkills: conceptIdList,
    experienceYears: z
      .number()
      .int()
      .min(EXPERIENCE_YEARS_MIN)
      .max(EXPERIENCE_YEARS_MAX)
      .nullable()
      .optional(),
    // exp-per-occ (ADR 0079-amendment PR-4): the per-occupation experience
    // overlay — a sparse `{conceptId, years}[]` keyed by occupation-group
    // concept-id. `years` is 0..70 OR `null` (the honest "not stated" state;
    // `0` means "stated zero years" and is distinct from `null`). Full-replace,
    // exactly like the other axes: the host sends the WHOLE current overlay
    // every PUT, so it MUST be threaded through every caller and pre-filled from
    // the profile read — saving any other dimension would otherwise zero it
    // (the page-wipe bug that bit region/kommun). `.max(400)` mirrors the
    // conceptId DoS cap. Backend additionally rejects an entry whose conceptId
    // is not also in `preferredOccupationGroups` (orphan → 400); the host only
    // ever projects years for still-selected occupations, so we don't duplicate
    // that subset guard here (backend is the last barrier). `.default([])` so a
    // missing key binds to an empty overlay (clears it).
    preferredOccupationExperience: z
      .array(
        z.object({
          conceptId: conceptIdString,
          years: z
            .number()
            .int()
            .min(EXPERIENCE_YEARS_MIN)
            .max(EXPERIENCE_YEARS_MAX)
            .nullable(),
        })
      )
      .max(MAX_CONCEPT_IDS)
      .default([]),
  });
}

export type SetMatchPreferencesInput = z.infer<
  ReturnType<typeof makeSetMatchPreferencesSchema>
>;
