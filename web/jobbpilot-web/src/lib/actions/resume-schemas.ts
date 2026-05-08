import { z } from "zod";

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

const dateString = z
  .string()
  .regex(DATE_REGEX, "Ogiltigt datum (yyyy-MM-dd).");

const optionalDateString = z
  .string()
  .regex(DATE_REGEX, "Ogiltigt datum (yyyy-MM-dd).")
  .nullish()
  .transform((v) => (v && v.length > 0 ? v : null));

export const createResumeSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Namn krävs.")
    .max(200, "Namn får vara max 200 tecken."),
  fullName: z
    .string()
    .trim()
    .min(1, "Fullständigt namn krävs.")
    .max(200, "Fullständigt namn får vara max 200 tecken."),
});

export const renameResumeSchema = z.object({
  resumeId: z.string().regex(GUID_REGEX, "Ogiltigt CV-ID."),
  name: z
    .string()
    .trim()
    .min(1, "Namn krävs.")
    .max(200, "Namn får vara max 200 tecken."),
});

const optionalNullableString = (max: number, label: string) =>
  z
    .string()
    .trim()
    .max(max, `${label} får vara max ${max} tecken.`)
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null));

const personalInfoSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(1, "Fullständigt namn krävs.")
    .max(200, "Fullständigt namn får vara max 200 tecken."),
  email: z
    .string()
    .trim()
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null))
    .pipe(
      z
        .union([z.email("Ogiltig e-postadress."), z.null()])
    ),
  phone: optionalNullableString(50, "Telefonnummer"),
  location: optionalNullableString(200, "Ort"),
});

const experienceSchema = z
  .object({
    company: z
      .string()
      .trim()
      .min(1, "Företag krävs.")
      .max(200, "Företag får vara max 200 tecken."),
    role: z
      .string()
      .trim()
      .min(1, "Roll krävs.")
      .max(200, "Roll får vara max 200 tecken."),
    startDate: dateString,
    endDate: optionalDateString,
    description: optionalNullableString(2000, "Beskrivning"),
  })
  .refine(
    (e) => !e.endDate || e.endDate >= e.startDate,
    { message: "Slutdatum kan inte vara före startdatum.", path: ["endDate"] }
  );

const educationSchema = z
  .object({
    institution: z
      .string()
      .trim()
      .min(1, "Lärosäte krävs.")
      .max(200, "Lärosäte får vara max 200 tecken."),
    degree: z
      .string()
      .trim()
      .min(1, "Examen krävs.")
      .max(200, "Examen får vara max 200 tecken."),
    startDate: dateString,
    endDate: optionalDateString,
  })
  .refine(
    (e) => !e.endDate || e.endDate >= e.startDate,
    { message: "Slutdatum kan inte vara före startdatum.", path: ["endDate"] }
  );

const skillSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Färdighet krävs.")
    .max(100, "Färdighet får vara max 100 tecken."),
  yearsExperience: z
    .number()
    .int("Ange antal år som heltal.")
    .min(0, "År kan inte vara negativt.")
    .max(70, "Maxvärde för år är 70.")
    .nullable()
    .optional()
    .transform((v) => (v === undefined ? null : v)),
});

export const resumeContentSchema = z.object({
  personalInfo: personalInfoSchema,
  experiences: z.array(experienceSchema),
  educations: z.array(educationSchema),
  skills: z.array(skillSchema),
  summary: z
    .string()
    .max(2000, "Sammanfattning får vara max 2 000 tecken.")
    .nullish()
    .transform((v) => (v && v.length > 0 ? v : null)),
});

export const updateMasterContentSchema = z.object({
  resumeId: z.string().regex(GUID_REGEX, "Ogiltigt CV-ID."),
  content: resumeContentSchema,
});

export type CreateResumeInput = z.infer<typeof createResumeSchema>;
export type RenameResumeInput = z.infer<typeof renameResumeSchema>;
export type ResumeContentInput = z.infer<typeof resumeContentSchema>;
export type UpdateMasterContentInput = z.infer<typeof updateMasterContentSchema>;
