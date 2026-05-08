import { describe, it, expect } from "vitest";
import {
  emptyContent,
  emptyEducation,
  emptyExperience,
  emptySkill,
  findMasterVersion,
} from "./content-utils";
import type { ResumeDetailDto, ResumeVersionDto } from "@/lib/types/resumes";

function makeVersion(
  kind: "Master" | "Tailored",
  id = "v1"
): ResumeVersionDto {
  return {
    id,
    kind,
    content: emptyContent("Anna"),
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}

describe("findMasterVersion", () => {
  it("returns the Master version when present", () => {
    const resume: ResumeDetailDto = {
      id: "r1",
      name: "CV",
      createdAt: "2026-01-01T00:00:00Z",
      updatedAt: "2026-01-01T00:00:00Z",
      versions: [makeVersion("Tailored", "t1"), makeVersion("Master", "m1")],
    };
    expect(findMasterVersion(resume)?.id).toBe("m1");
  });

  it("returns null when there is no Master", () => {
    const resume: ResumeDetailDto = {
      id: "r1",
      name: "CV",
      createdAt: "2026-01-01T00:00:00Z",
      updatedAt: "2026-01-01T00:00:00Z",
      versions: [],
    };
    expect(findMasterVersion(resume)).toBeNull();
  });
});

describe("empty constructors", () => {
  it("emptyContent uses provided fullName", () => {
    expect(emptyContent("Anna").personalInfo.fullName).toBe("Anna");
  });

  it("emptyContent has empty arrays", () => {
    const c = emptyContent();
    expect(c.experiences).toEqual([]);
    expect(c.educations).toEqual([]);
    expect(c.skills).toEqual([]);
  });

  it("emptyExperience has empty fields", () => {
    expect(emptyExperience().company).toBe("");
  });

  it("emptyEducation has empty fields", () => {
    expect(emptyEducation().institution).toBe("");
  });

  it("emptySkill has null years", () => {
    expect(emptySkill().yearsExperience).toBeNull();
  });
});
