import type {
  EducationDto,
  ExperienceDto,
  ResumeContentDto,
  ResumeDetailDto,
  ResumeVersionDto,
  SkillDto,
} from "@/lib/types/resumes";

export function findMasterVersion(
  resume: ResumeDetailDto
): ResumeVersionDto | null {
  return resume.versions.find((v) => v.kind === "Master") ?? null;
}

export function emptyExperience(): ExperienceDto {
  return {
    company: "",
    role: "",
    startDate: "",
    endDate: null,
    description: null,
  };
}

export function emptyEducation(): EducationDto {
  return {
    institution: "",
    degree: "",
    startDate: "",
    endDate: null,
  };
}

export function emptySkill(): SkillDto {
  return {
    name: "",
    yearsExperience: null,
  };
}

export function emptyContent(fullName = ""): ResumeContentDto {
  return {
    personalInfo: {
      fullName,
      email: null,
      phone: null,
      location: null,
    },
    experiences: [],
    educations: [],
    skills: [],
    summary: null,
  };
}
