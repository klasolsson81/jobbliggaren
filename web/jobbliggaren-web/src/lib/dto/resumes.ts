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

export const resumeContentDtoSchema = z.object({
  personalInfo: personalInfoDtoSchema,
  experiences: z.array(experienceDtoSchema),
  educations: z.array(educationDtoSchema),
  skills: z.array(skillDtoSchema),
  summary: z.string().nullable(),
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
});
export type ResumeListItemDto = z.infer<typeof resumeListItemDtoSchema>;

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
