export type ResumeVersionKind = "Master" | "Tailored";

export interface PersonalInfoDto {
  fullName: string;
  email: string | null;
  phone: string | null;
  location: string | null;
}

export interface ExperienceDto {
  company: string;
  role: string;
  /** "yyyy-MM-dd" — DateOnly serialiserad */
  startDate: string;
  /** "yyyy-MM-dd" eller null */
  endDate: string | null;
  description: string | null;
}

export interface EducationDto {
  institution: string;
  degree: string;
  /** "yyyy-MM-dd" */
  startDate: string;
  /** "yyyy-MM-dd" eller null */
  endDate: string | null;
}

export interface SkillDto {
  name: string;
  yearsExperience: number | null;
}

export interface ResumeContentDto {
  personalInfo: PersonalInfoDto;
  experiences: ExperienceDto[];
  educations: EducationDto[];
  skills: SkillDto[];
  summary: string | null;
}

export interface ResumeVersionDto {
  id: string;
  kind: ResumeVersionKind;
  content: ResumeContentDto;
  createdAt: string;
  updatedAt: string;
}

export interface ResumeListItemDto {
  id: string;
  name: string;
  versionCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface GetResumesResult {
  items: ResumeListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ResumeDetailDto {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  versions: ResumeVersionDto[];
}
