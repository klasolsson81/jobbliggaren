import { z } from "zod";
import { pagedResult } from "./_helpers";

export const resumeVersionKindSchema = z.enum(["Master", "Tailored"]);
export type ResumeVersionKind = z.infer<typeof resumeVersionKindSchema>;

export const personalInfoDtoSchema = z.object({
  fullName: z.string(),
  email: z.string().nullable(),
  phone: z.string().nullable(),
  location: z.string().nullable(),
});
export type PersonalInfoDto = z.infer<typeof personalInfoDtoSchema>;

export const experienceDtoSchema = z.object({
  company: z.string(),
  role: z.string(),
  /** "yyyy-MM-dd" — DateOnly serialiserad */
  startDate: z.string(),
  /** "yyyy-MM-dd" eller null */
  endDate: z.string().nullable(),
  description: z.string().nullable(),
});
export type ExperienceDto = z.infer<typeof experienceDtoSchema>;

export const educationDtoSchema = z.object({
  institution: z.string(),
  degree: z.string(),
  /** "yyyy-MM-dd" */
  startDate: z.string(),
  /** "yyyy-MM-dd" eller null */
  endDate: z.string().nullable(),
});
export type EducationDto = z.infer<typeof educationDtoSchema>;

export const skillDtoSchema = z.object({
  name: z.string(),
  yearsExperience: z.number().nullable(),
});
export type SkillDto = z.infer<typeof skillDtoSchema>;

/** Talat språk (Fas 4b AppCopy superset, ADR 0095 D-C). `proficiency` bär
 * `LanguageProficiency`-SmartEnumets Name-token (engelska: NotStated/Basic/Good/
 * Fluent/Native). LÅST mängd → strikt `z.enum` så drift fail-loud:ar; backend
 * mappar okänd/utelämnad token till NotStated (aldrig syntetiserad). */
export const spokenLanguageDtoSchema = z.object({
  name: z.string(),
  proficiency: z.enum(["NotStated", "Basic", "Good", "Fluent", "Native"]),
});
export type SpokenLanguageDto = z.infer<typeof spokenLanguageDtoSchema>;

/** En post i en dynamisk yrkesstyrd CV-sektion (Fas 4b superset, ADR 0095 D-B).
 * `lines` är valfria brödrader (STJ passerar undefined när nyckeln utelämnas). */
export const sectionEntryDtoSchema = z.object({
  title: z.string(),
  lines: z.array(z.string()).optional(),
});
export type SectionEntryDto = z.infer<typeof sectionEntryDtoSchema>;

/** En dynamisk yrkesstyrd CV-sektion utöver de fyra standard-sektionerna
 * (Fas 4b superset, ADR 0095 D-B). `heading` är fri användartext. */
export const resumeSectionDtoSchema = z.object({
  heading: z.string(),
  entries: z.array(sectionEntryDtoSchema).optional(),
});
export type ResumeSectionDto = z.infer<typeof resumeSectionDtoSchema>;

export const resumeContentDtoSchema = z.object({
  personalInfo: personalInfoDtoSchema,
  experiences: z.array(experienceDtoSchema),
  educations: z.array(educationDtoSchema),
  skills: z.array(skillDtoSchema),
  summary: z.string().nullable(),
  // Fas 4b AppCopy superset (ADR 0095): optional-med-default paritet mot backend
  // `ResumeContentDto` (pre-superset payloads utelämnar dem → parsar rent).
  // `skillGroups` är utanför PR-8.3-scope (CTO Q7(b)) och modelleras inte här;
  // okända nycklar ignoreras av zod (icke-strict).
  languages: z.array(spokenLanguageDtoSchema).optional(),
  sections: z.array(resumeSectionDtoSchema).optional(),
});
export type ResumeContentDto = z.infer<typeof resumeContentDtoSchema>;

export const resumeVersionDtoSchema = z.object({
  id: z.string(),
  kind: resumeVersionKindSchema,
  content: resumeContentDtoSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type ResumeVersionDto = z.infer<typeof resumeVersionDtoSchema>;

export const resumeLanguageSchema = z.enum(["Sv", "En"]);
export type ResumeLanguage = z.infer<typeof resumeLanguageSchema>;

/** CV:ts ursprung (paritet `ResumeSourceOrigin`, ADR 0096). LÅST mängd → strikt
 * `z.enum` så drift fail-loud:ar. Driver hubb-kortets badge: `Import` →
 * "Importerad", `Template` → "Skapad", `Legacy` → ingen badge (pre-origin-CV). */
export const resumeOriginSchema = z.enum(["Legacy", "Import", "Template"]);
export type ResumeOrigin = z.infer<typeof resumeOriginSchema>;

export const resumeListItemDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  versionCount: z.number().int().nonnegative(),
  createdAt: z.string(),
  updatedAt: z.string(),
  isPrimary: z.boolean(),
  language: resumeLanguageSchema,
  latestRole: z.string().nullable(),
  sectionCount: z.number().int().min(0).max(4),
  topSkills: z.array(z.string()).max(5),
  /** Öppna åtgärder ur den DEK-fria finding-status-ledgern (Fas 4b PR-8, CTO-bind
   * Q1). `null` = INTE granskad vid den nuvarande rubrikversionen → UI:t renderar
   * "Granska", ALDRIG noll (§5-ärlighet: "0" får bara betyda granskad-och-ren).
   * `0` = granskad, inga åtgärder. `N` = N att åtgärda. */
  openFindingCount: z.number().int().nonnegative().nullable(),
  origin: resumeOriginSchema,
  /** Mallnamn (icke-PII root-metadata, ADR 0096). Öppen sträng — nya mallar ska
   * inte fail-loud:a listvyn; visas bara som kort-metadata för Skapad-CV. */
  template: z.string(),
});
export type ResumeListItemDto = z.infer<typeof resumeListItemDtoSchema>;

/** Kanonisk ATS-textvy (`GET /api/v1/resumes/{id}/ats-text`, Fas 4b PR-8.2).
 * `source` är en diskriminator (`Linearized` idag; RawText-syskonet är en
 * framtidsflagga) — öppen sträng så ett framtida värde inte fail-loud:ar den
 * read-only vyn. `text` = linjäriserad, pnr-redigerad CV-text (redan ren vid
 * motorns choke point; BFF:n läser bara vad backend redan garanterat). */
export const atsTextResponseSchema = z.object({
  source: z.string(),
  text: z.string(),
});
export type AtsTextResponse = z.infer<typeof atsTextResponseSchema>;

export const resumeDetailDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
  versions: z.array(resumeVersionDtoSchema),
});
export type ResumeDetailDto = z.infer<typeof resumeDetailDtoSchema>;

export const getResumesResultSchema = pagedResult(resumeListItemDtoSchema);
export type GetResumesResult = z.infer<typeof getResumesResultSchema>;
