import type { ParsedGapSummary } from "@/lib/dto/parsed-resume";

/**
 * The shared confirm-task definition behind BOTH the /cv hub action card's
 * "X av Y uppgifter klara" meter AND the Slutför-guide's per-step completion
 * indicators (Fas 4b PR-8.3, CTO-bind Q5). Single source of truth: the meter
 * and the guide must never disagree about what a task is. Parity with the
 * Domain `ParsedGapSummary` (the nine presence booleans) — the frontend counts
 * the SAME nine keys the backend denormalises at import.
 */
export const GAP_TASK_KEYS = [
  "hasFullName",
  "hasEmail",
  "hasPhone",
  "hasLocation",
  "hasProfile",
  "hasExperience",
  "hasEducation",
  "hasSkills",
  "hasLanguages",
] as const satisfies ReadonlyArray<keyof ParsedGapSummary>;

/** The total number of confirm-tasks (nine, parity Domain `ParsedGapSummary`). */
export const TOTAL_GAP_TASKS: number = GAP_TASK_KEYS.length;

/** Counts how many of the nine confirm-tasks a gap summary reports as complete. */
export function countCompletedTasks(gaps: ParsedGapSummary): number {
  return GAP_TASK_KEYS.reduce((done, key) => (gaps[key] ? done + 1 : done), 0);
}

/**
 * The minimal structural shape the guide's live form exposes for gap derivation.
 * Kept deliberately loose (`experiences`/`educations` as `unknown[]`) so this
 * module does not depend on the wizard's full `FormValues` type — the wizard's
 * values structurally satisfy this.
 */
export interface GapTaskFormValues {
  readonly personalInfo: {
    readonly fullName: string;
    readonly email: string;
    readonly phone: string;
    readonly location: string;
  };
  readonly summary: string;
  readonly experiences: ReadonlyArray<unknown>;
  readonly educations: ReadonlyArray<unknown>;
  readonly skills: ReadonlyArray<{ readonly name: string }>;
  readonly languages: ReadonlyArray<{ readonly name: string }>;
}

/**
 * Derives the nine presence flags from the guide's CURRENT form values, mirroring
 * the backend `ParsedGapSummary.FromContent` semantics exactly: whitespace-only
 * text counts as missing (`!string.IsNullOrWhiteSpace`), and an empty collection
 * counts as a missing section (`Count > 0`). This drives the wizard's per-step
 * task indicators so they track what the user has filled in, live.
 */
export function deriveGapSummaryFromForm(
  values: GapTaskFormValues,
): ParsedGapSummary {
  const present = (value: string) => value.trim().length > 0;
  return {
    hasFullName: present(values.personalInfo.fullName),
    hasEmail: present(values.personalInfo.email),
    hasPhone: present(values.personalInfo.phone),
    hasLocation: present(values.personalInfo.location),
    hasProfile: present(values.summary),
    hasExperience: values.experiences.length > 0,
    hasEducation: values.educations.length > 0,
    hasSkills: values.skills.some((skill) => present(skill.name)),
    hasLanguages: values.languages.some((language) => present(language.name)),
  };
}
