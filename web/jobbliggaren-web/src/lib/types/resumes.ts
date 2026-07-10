// Re-export från lib/dto/resumes.ts. Zod-schemat är single source of truth
// per ADR 0020. Nya konsumenter bör importera från `@/lib/dto/resumes`.
export type {
  ResumeVersionKind,
  PersonalInfoDto,
  ExperienceDto,
  EducationDto,
  SkillDto,
  SpokenLanguageDto,
  SectionEntryDto,
  ResumeSectionDto,
  ResumeContentDto,
  ResumeVersionDto,
  ResumeListItemDto,
  GetResumesResult,
  ResumeDetailDto,
} from "@/lib/dto/resumes";
