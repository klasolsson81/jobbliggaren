import { z } from "zod";
import type { useTranslations } from "next-intl";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). This schema carries no
// user-facing validation messages of its own — the conceptId/length guards are
// structural (defense-in-depth; backend is the authoritative barrier) — so the
// translator is accepted for factory-shape consistency but currently unused.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

/**
 * The body of `PATCH /api/v1/me/match-preferences` (ADR 0147): one to five parts, each replacing
 * that whole part, and an absent part left as stored. Every member of a present part is required,
 * so no default can turn a forgotten member into a clearing value; the one optional member is
 * `preferredOccupationExperience`, which absent keeps the stated years (D2).
 *
 * conceptId-mönstret (1–32 tecken, [A-Za-z0-9_-]) + `.max(400)` speglar
 * backend `SearchCriteria.MaxConceptIds`-taket (defense-in-depth + DoS-skydd;
 * backend är sista barriären).
 */

const MAX_CONCEPT_IDS = 400;

const conceptIdString = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

const conceptIdList = z.array(conceptIdString).max(MAX_CONCEPT_IDS);

// STEG 3 / ADR 0079 (Beslut 1): a single profile-level "antal års erfarenhet".
// Nullable — `null` is the honest "not stated" state (never 0,
// which would mean "stated zero years"). 0..70 mirrors the backend validator
// (a sane human-career bound + a DoS-/typo-guard; backend is the last barrier).
const EXPERIENCE_YEARS_MIN = 0;
const EXPERIENCE_YEARS_MAX = 70;

const experienceYears = z
  .number()
  .int()
  .min(EXPERIENCE_YEARS_MIN)
  .max(EXPERIENCE_YEARS_MAX)
  .nullable();

const occupationsPart = z.strictObject({
  preferredOccupationGroups: conceptIdList,
  preferredOccupationExperience: z
    .array(z.strictObject({ conceptId: conceptIdString, years: experienceYears }))
    .max(MAX_CONCEPT_IDS)
    .optional(),
});

const skillsPart = z.strictObject({
  preferredSkills: conceptIdList,
});

// Region, municipality and remote are one part: they fold into one "ort" dimension.
const locationsPart = z.strictObject({
  preferredRegions: conceptIdList,
  preferredMunicipalities: conceptIdList,
  preferredRemote: z.boolean(),
});

const employmentTypesPart = z.strictObject({
  preferredEmploymentTypes: conceptIdList,
});

const experiencePart = z.strictObject({
  experienceYears,
});

export function makeUpdateMatchPreferencesSchema(_t: ValidationTranslator) {
  return z
    .strictObject({
      occupations: occupationsPart.optional(),
      skills: skillsPart.optional(),
      locations: locationsPart.optional(),
      employmentTypes: employmentTypesPart.optional(),
      experience: experiencePart.optional(),
    })
    .refine((body) => Object.values(body).some((part) => part !== undefined));
}

export type UpdateMatchPreferencesInput = z.infer<
  ReturnType<typeof makeUpdateMatchPreferencesSchema>
>;
