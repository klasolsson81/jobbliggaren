import { z } from "zod";
import type { useTranslations } from "next-intl";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). Callers build the schema
// via the `make*`-factories; Swedish messages live in
// `messages/sv/validation.json`.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

const makeDateString = (t: ValidationTranslator) =>
  z.string().regex(DATE_REGEX, t("resume.dateInvalid"));

const makeOptionalDateString = (t: ValidationTranslator) =>
  z
    .string()
    .regex(DATE_REGEX, t("resume.dateInvalid"))
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null));

const makeOptionalNullableString = (
  t: ValidationTranslator,
  max: number,
  label: string
) =>
  z
    .string()
    .trim()
    .max(max, t("resume.maxChars", { label, max }))
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
    phone: makeOptionalNullableString(t, 50, "Telefonnummer"),
    location: makeOptionalNullableString(t, 200, "Ort"),
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
      startDate: makeDateString(t),
      endDate: makeOptionalDateString(t),
      description: makeOptionalNullableString(t, 2000, "Beskrivning"),
    })
    .refine((e) => !e.endDate || e.endDate >= e.startDate, {
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
      startDate: makeDateString(t),
      endDate: makeOptionalDateString(t),
    })
    .refine((e) => !e.endDate || e.endDate >= e.startDate, {
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
  });
}

export function makeUpdateMasterContentSchema(t: ValidationTranslator) {
  return z.object({
    resumeId: z.string().regex(GUID_REGEX, t("resume.resumeIdInvalid")),
    content: makeResumeContentSchema(t),
  });
}

// Befordra en tolkad CV-stagingartefakt (F4-8 / STEG A) till en kanonisk Resume
// (Fas 4 STEG B / F2). `content` återbrukar resumeContentSchema — exakt paritet
// med domänens stränga Resume.ValidateContent (företag/roll/lärosäte/examen krävs,
// strukturerade datum yyyy-MM-dd, slut >= start, färdighet 0–70 år). `name` är
// CV-variantens interna namn (skilt från PersonalInfo.FullName).
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
