import { describe, it, expect } from "vitest";
import {
  createResumeSchema,
  renameResumeSchema,
  resumeContentSchema,
  updateMasterContentSchema,
} from "./resume-schemas";

const VALID_GUID = "550e8400-e29b-41d4-a716-446655440000";

describe("createResumeSchema", () => {
  it("accepts valid name and fullName", () => {
    expect(
      createResumeSchema.safeParse({ name: "Mitt CV", fullName: "Anna Andersson" }).success
    ).toBe(true);
  });

  it("rejects empty name", () => {
    expect(
      createResumeSchema.safeParse({ name: "", fullName: "Anna" }).success
    ).toBe(false);
  });

  it("rejects empty fullName", () => {
    expect(
      createResumeSchema.safeParse({ name: "CV", fullName: "" }).success
    ).toBe(false);
  });

  it("rejects name longer than 200 chars", () => {
    expect(
      createResumeSchema.safeParse({ name: "a".repeat(201), fullName: "Anna" }).success
    ).toBe(false);
  });

  it("trims whitespace from name", () => {
    const result = createResumeSchema.safeParse({ name: "  CV  ", fullName: "Anna" });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.name).toBe("CV");
  });
});

describe("renameResumeSchema", () => {
  it("accepts valid GUID and name", () => {
    expect(
      renameResumeSchema.safeParse({ resumeId: VALID_GUID, name: "Nytt namn" }).success
    ).toBe(true);
  });

  it("rejects invalid GUID", () => {
    expect(
      renameResumeSchema.safeParse({ resumeId: "not-a-guid", name: "Nytt namn" }).success
    ).toBe(false);
  });

  it("rejects empty name", () => {
    expect(
      renameResumeSchema.safeParse({ resumeId: VALID_GUID, name: "" }).success
    ).toBe(false);
  });
});

describe("resumeContentSchema – personalInfo", () => {
  function baseContent() {
    return {
      personalInfo: { fullName: "Anna Andersson" },
      experiences: [],
      educations: [],
      skills: [],
    };
  }

  it("accepts minimal valid content", () => {
    expect(resumeContentSchema.safeParse(baseContent()).success).toBe(true);
  });

  it("rejects missing fullName", () => {
    const c = baseContent();
    c.personalInfo = { fullName: "" };
    expect(resumeContentSchema.safeParse(c).success).toBe(false);
  });

  it("rejects invalid email format", () => {
    const c = { ...baseContent(), personalInfo: { fullName: "Anna", email: "not-an-email" } };
    expect(resumeContentSchema.safeParse(c).success).toBe(false);
  });

  it("treats empty email string as null", () => {
    const c = { ...baseContent(), personalInfo: { fullName: "Anna", email: "" } };
    const result = resumeContentSchema.safeParse(c);
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.personalInfo.email).toBeNull();
  });

  it("rejects summary longer than 2000 chars", () => {
    const c = { ...baseContent(), summary: "a".repeat(2001) };
    expect(resumeContentSchema.safeParse(c).success).toBe(false);
  });

  it("accepts summary at exactly 2000 chars", () => {
    const c = { ...baseContent(), summary: "a".repeat(2000) };
    expect(resumeContentSchema.safeParse(c).success).toBe(true);
  });
});

describe("resumeContentSchema – experiences", () => {
  const base = {
    personalInfo: { fullName: "Anna" },
    educations: [],
    skills: [],
  };

  it("accepts a valid experience", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [
        {
          company: "Acme AB",
          role: "Utvecklare",
          startDate: "2024-01-01",
          endDate: "2025-06-30",
        },
      ],
    });
    expect(result.success).toBe(true);
  });

  it("rejects experience with missing company", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [{ company: "", role: "Utvecklare", startDate: "2024-01-01" }],
    });
    expect(result.success).toBe(false);
  });

  it("rejects experience with endDate before startDate", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [
        {
          company: "Acme",
          role: "Dev",
          startDate: "2025-06-01",
          endDate: "2024-01-01",
        },
      ],
    });
    expect(result.success).toBe(false);
  });

  it("rejects invalid startDate format", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [
        { company: "Acme", role: "Dev", startDate: "01/01/2024" },
      ],
    });
    expect(result.success).toBe(false);
  });
});

describe("resumeContentSchema – educations", () => {
  const base = {
    personalInfo: { fullName: "Anna" },
    experiences: [],
    skills: [],
  };

  it("accepts valid education", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      educations: [
        {
          institution: "KTH",
          degree: "Civilingenjör",
          startDate: "2018-09-01",
          endDate: "2023-06-15",
        },
      ],
    });
    expect(result.success).toBe(true);
  });

  it("rejects education with endDate before startDate", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      educations: [
        {
          institution: "KTH",
          degree: "Civ.ing.",
          startDate: "2023-06-15",
          endDate: "2018-09-01",
        },
      ],
    });
    expect(result.success).toBe(false);
  });

  it("rejects education with empty institution", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      educations: [
        { institution: "", degree: "Examen", startDate: "2020-01-01" },
      ],
    });
    expect(result.success).toBe(false);
  });
});

describe("resumeContentSchema – skills", () => {
  const base = {
    personalInfo: { fullName: "Anna" },
    experiences: [],
    educations: [],
  };

  it("accepts valid skill with years", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "TypeScript", yearsExperience: 5 }],
    });
    expect(result.success).toBe(true);
  });

  it("accepts skill without years (null)", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "Git", yearsExperience: null }],
    });
    expect(result.success).toBe(true);
  });

  it("rejects skill with empty name", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "", yearsExperience: 3 }],
    });
    expect(result.success).toBe(false);
  });

  it("rejects yearsExperience > 70", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "C#", yearsExperience: 75 }],
    });
    expect(result.success).toBe(false);
  });

  it("rejects negative yearsExperience", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "C#", yearsExperience: -1 }],
    });
    expect(result.success).toBe(false);
  });

  it("accepts yearsExperience = 0 (boundary)", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "Rust", yearsExperience: 0 }],
    });
    expect(result.success).toBe(true);
  });

  it("accepts yearsExperience = 70 (boundary)", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      skills: [{ name: "Cobol", yearsExperience: 70 }],
    });
    expect(result.success).toBe(true);
  });
});

describe("updateMasterContentSchema", () => {
  it("accepts valid composed payload", () => {
    expect(
      updateMasterContentSchema.safeParse({
        resumeId: VALID_GUID,
        content: {
          personalInfo: { fullName: "Anna Andersson" },
          experiences: [],
          educations: [],
          skills: [],
        },
      }).success
    ).toBe(true);
  });

  it("rejects bad GUID", () => {
    expect(
      updateMasterContentSchema.safeParse({
        resumeId: "x",
        content: {
          personalInfo: { fullName: "Anna" },
          experiences: [],
          educations: [],
          skills: [],
        },
      }).success
    ).toBe(false);
  });
});
