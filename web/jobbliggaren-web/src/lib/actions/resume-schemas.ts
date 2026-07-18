import { z } from "zod";
import type { useTranslations } from "next-intl";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). Callers build the schema
// via the `make*`-factories; Swedish messages live in
// `messages/sv/validation.json`.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

// makeDateString (the required-date variant) was removed with the honest-date-absence
// contract (CTO-bind 5a-pre): no editing surface may force a user to invent a date.
const makeOptionalDateString = (t: ValidationTranslator) =>
  z
    .string()
    .regex(DATE_REGEX, t("resume.dateInvalid"))
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null));

// `message` is a pre-resolved, localized string (e.g. `t("resume.phoneMax")`).
// We deliberately do NOT take a `label` arg and interpolate it into a shared
// template: the field noun must be localized too, so each call site passes a
// per-field message key (mirrors `resume.summaryMax`). A shared template with a
// hardcoded Swedish label leaked "Telefonnummer ..." into the English catalog.
const makeOptionalNullableString = (max: number, message: string) =>
  z
    .string()
    .trim()
    .max(max, message)
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null));

const makePersonalInfoSchema = (t: ValidationTranslator) =>
  z.object({
    fullName: z
      .string()
      .trim()
      .min(1, t("resume.fullNameRequired"))
      .max(200, t("resume.fullNameMax")),
    email: z
      .string()
      .trim()
      .nullish()
      .transform((v) => (v && v.length > 0 ? v : null))
      .pipe(z.union([z.email(t("resume.emailInvalid")), z.null()])),
    phone: makeOptionalNullableString(50, t("resume.phoneMax")),
    location: makeOptionalNullableString(200, t("resume.locationMax")),
  });

const makeExperienceSchema = (t: ValidationTranslator) =>
  z
    .object({
      company: z
        .string()
        .trim()
        .min(1, t("resume.companyRequired"))
        .max(200, t("resume.companyMax")),
      role: z
        .string()
        .trim()
        .min(1, t("resume.roleRequired"))
        .max(200, t("resume.roleMax")),
      // Ärligt frånvarande datum (CTO-bind 5a-pre): ett auto-promotat CV kan sakna
      // startdatum, och en redigering utan datum måste kunna sparas om utan att
      // användaren tvingas hitta på ett. rawPeriod är verbatim-perioden ur filen —
      // dold passthrough; strukturerade datum vinner när de väl sätts.
      startDate: makeOptionalDateString(t),
      endDate: makeOptionalDateString(t),
      description: makeOptionalNullableString(2000, t("resume.descriptionMax")),
      rawPeriod: makeOptionalNullableString(100, t("resume.rawPeriodMax")),
    })
    .refine((e) => !e.endDate || !e.startDate || e.endDate >= e.startDate, {
      message: t("resume.endBeforeStart"),
      path: ["endDate"],
    });

const makeEducationSchema = (t: ValidationTranslator) =>
  z
    .object({
      institution: z
        .string()
        .trim()
        .min(1, t("resume.institutionRequired"))
        .max(200, t("resume.institutionMax")),
      degree: z
        .string()
        .trim()
        .min(1, t("resume.degreeRequired"))
        .max(200, t("resume.degreeMax")),
      // Samma ärligt-frånvarande-datum-kontrakt som erfarenhet (CTO-bind 5a-pre).
      startDate: makeOptionalDateString(t),
      endDate: makeOptionalDateString(t),
      rawPeriod: makeOptionalNullableString(100, t("resume.rawPeriodMax")),
    })
    .refine((e) => !e.endDate || !e.startDate || e.endDate >= e.startDate, {
      message: t("resume.endBeforeStart"),
      path: ["endDate"],
    });

const makeSkillSchema = (t: ValidationTranslator) =>
  z.object({
    name: z
      .string()
      .trim()
      .min(1, t("resume.skillNameRequired"))
      .max(100, t("resume.skillNameMax")),
    yearsExperience: z
      .number()
      .int(t("resume.yearsInteger"))
      .min(0, t("resume.yearsNegative"))
      .max(70, t("resume.yearsMax"))
      .nullable()
      .optional()
      .transform((v) => (v === undefined ? null : v)),
  });

// Fas 4b AppCopy superset (ADR 0095). The backend `LanguageProficiency` SmartEnum
// tokens (English code identifiers). An unknown token maps tolerantly to
// NotStated backend-side, but the guide sends exact tokens.
const LANGUAGE_PROFICIENCY_TOKENS = [
  "NotStated",
  "Basic",
  "Good",
  "Fluent",
  "Native",
] as const;

// Mirrors the domain `Resume.ValidateContent`: the language name caps at 100 —
// the scored-atom bound shared with Skill.Name (#855, `Skill.NameMaxLength`). The
// domain is the authority; this client `.max(100)` mirrors it (the asymmetry the
// domain formerly lacked a max is now closed — the server refuses an over-long name too).
const makeSpokenLanguageSchema = (t: ValidationTranslator) =>
  z.object({
    name: z
      .string()
      .trim()
      .min(1, t("resume.languageNameRequired"))
      .max(100, t("resume.languageNameMax")),
    proficiency: z.enum(LANGUAGE_PROFICIENCY_TOKENS),
  });

// Mirrors the domain `Resume.ValidateContent` (ADR 0095 D-E, amended by #815): the entry
// title is OPTIONAL — "Referenser / Lämnas på begäran." is an ordinary CV shape, and the
// deterministic parser will not invent a heading for it (ADR 0071). What the domain forbids
// is an entry carrying NEITHER title nor lines (`Resume.SectionEntryEmpty`), so the schema
// enforces exactly that, cross-field. The lines are capped by their SUMMED length (max
// 2 000 across all lines, NOT per line — the domain sums `l?.Length`). The 200-char title
// cap is a UI affordance, matching the label-field convention (Company/Role = 200).
const makeSectionEntrySchema = (t: ValidationTranslator) =>
  z
    .object({
      title: z
        .string()
        .trim()
        .max(200, t("resume.sectionEntryTitleMax"))
        .optional(),
      lines: z
        .array(z.string())
        .optional()
        .refine(
          (lines) =>
            !lines || lines.reduce((sum, line) => sum + line.length, 0) <= 2_000,
          { message: t("resume.sectionEntryTooLong") },
        ),
    })
    .refine(
      (entry) =>
        (entry.title?.trim().length ?? 0) > 0 ||
        (entry.lines?.some((line) => line.trim().length > 0) ?? false),
      { message: t("resume.sectionEntryEmpty"), path: ["title"] },
    );

const makeResumeSectionSchema = (t: ValidationTranslator) =>
  z.object({
    heading: z
      .string()
      .trim()
      .min(1, t("resume.sectionHeadingRequired"))
      .max(200, t("resume.sectionHeadingMax")),
    entries: z.array(makeSectionEntrySchema(t)).optional(),
  });

export function makeCreateResumeSchema(t: ValidationTranslator) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("resume.nameRequired"))
      .max(200, t("resume.nameMax")),
    fullName: z
      .string()
      .trim()
      .min(1, t("resume.fullNameRequired"))
      .max(200, t("resume.fullNameMax")),
  });
}

export function makeRenameResumeSchema(t: ValidationTranslator) {
  return z.object({
    resumeId: z.string().regex(GUID_REGEX, t("resume.resumeIdInvalid")),
    name: z
      .string()
      .trim()
      .min(1, t("resume.nameRequired"))
      .max(200, t("resume.nameMax")),
  });
}

export function makeResumeContentSchema(t: ValidationTranslator) {
  return z.object({
    personalInfo: makePersonalInfoSchema(t),
    experiences: z.array(makeExperienceSchema(t)),
    educations: z.array(makeEducationSchema(t)),
    skills: z.array(makeSkillSchema(t)),
    summary: z
      .string()
      .max(2000, t("resume.summaryMax"))
      .nullish()
      .transform((v) => (v && v.length > 0 ? v : null)),
    // Fas 4b AppCopy superset (ADR 0095). Optional + undefined-friendly so the
    // komplettera predecessor and other pre-superset callers (which omit them)
    // stay valid; the Slutför-guide is the first sender. `skillGroups` is out of
    // scope (CTO Q7(b)) and deliberately not modelled here.
    languages: z.array(makeSpokenLanguageSchema(t)).optional(),
    sections: z.array(makeResumeSectionSchema(t)).optional(),
  });
}

export function makeUpdateMasterContentSchema(t: ValidationTranslator) {
  return z.object({
    resumeId: z.string().regex(GUID_REGEX, t("resume.resumeIdInvalid")),
    content: makeResumeContentSchema(t),
  });
}

// Befordra en tolkad CV-stagingartefakt (F4-8 / STEG A) till en kanonisk Resume
// (Fas 4 STEG B / F2). `content` återbrukar resumeContentSchema — paritet med
// domänens Resume.ValidateContent (företag/roll/lärosäte/examen krävs; strukturerade
// datum yyyy-MM-dd är VALFRIA sedan honest-date-absence, CTO-bind 5a-pre; slut >= start
// gäller bara när båda finns; färdighet 0–70 år). `name` är CV-variantens interna
// namn (skilt från PersonalInfo.FullName).
export function makePromoteParsedResumeSchema(t: ValidationTranslator) {
  return z.object({
    parsedResumeId: z.string().regex(GUID_REGEX, t("resume.resumeIdInvalid")),
    name: z
      .string()
      .trim()
      .min(1, t("resume.cvNameRequired"))
      .max(200, t("resume.nameMax")),
    content: makeResumeContentSchema(t),
  });
}

export type CreateResumeInput = z.infer<
  ReturnType<typeof makeCreateResumeSchema>
>;
export type RenameResumeInput = z.infer<
  ReturnType<typeof makeRenameResumeSchema>
>;
export type ResumeContentInput = z.infer<
  ReturnType<typeof makeResumeContentSchema>
>;
export type UpdateMasterContentInput = z.infer<
  ReturnType<typeof makeUpdateMasterContentSchema>
>;
export type PromoteParsedResumeInput = z.infer<
  ReturnType<typeof makePromoteParsedResumeSchema>
>;
