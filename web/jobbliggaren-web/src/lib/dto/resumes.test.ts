import { describe, it, expect } from "vitest";
import {
  getResumesResultSchema,
  resumeContentDtoSchema,
  resumeDetailDtoSchema,
  resumeLanguageSchema,
  resumeListItemDtoSchema,
  resumeVersionKindSchema,
} from "./resumes";

const validContent = {
  personalInfo: {
    fullName: "Anna Andersson",
    email: "anna@example.com",
    phone: null,
    location: null,
  },
  experiences: [
    {
      company: "Acme",
      role: "Dev",
      startDate: "2024-01-01",
      endDate: null,
      description: null,
    },
  ],
  educations: [],
  skills: [{ name: "C#", yearsExperience: 5 }],
  summary: null,
};

const validListItem = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "Master CV",
  versionCount: 1,
  createdAt: "2026-05-11T10:00:00Z",
  updatedAt: "2026-05-11T10:00:00Z",
  isPrimary: true,
  language: "Sv",
  latestRole: "Backend-utvecklare",
  sectionCount: 3,
  topSkills: ["C#", "TypeScript", "PostgreSQL"],
};

describe("resumeVersionKindSchema", () => {
  it("accepts Master and Tailored", () => {
    expect(resumeVersionKindSchema.safeParse("Master").success).toBe(true);
    expect(resumeVersionKindSchema.safeParse("Tailored").success).toBe(true);
  });

  it("rejects unknown kind", () => {
    expect(resumeVersionKindSchema.safeParse("Draft").success).toBe(false);
  });
});

describe("resumeContentDtoSchema", () => {
  it("accepts valid content", () => {
    expect(resumeContentDtoSchema.safeParse(validContent).success).toBe(true);
  });

  it("rejects when personalInfo.fullName missing", () => {
    const broken = {
      ...validContent,
      personalInfo: { email: null, phone: null, location: null },
    };
    expect(resumeContentDtoSchema.safeParse(broken).success).toBe(false);
  });

  it("rejects when experience.startDate missing", () => {
    const broken = {
      ...validContent,
      experiences: [
        { company: "Acme", role: "Dev", endDate: null, description: null },
      ],
    };
    expect(resumeContentDtoSchema.safeParse(broken).success).toBe(false);
  });
});

describe("resumeLanguageSchema", () => {
  it("accepts Sv and En", () => {
    expect(resumeLanguageSchema.safeParse("Sv").success).toBe(true);
    expect(resumeLanguageSchema.safeParse("En").success).toBe(true);
  });

  it("rejects unknown languages", () => {
    expect(resumeLanguageSchema.safeParse("sv").success).toBe(false);
    expect(resumeLanguageSchema.safeParse("Fr").success).toBe(false);
  });
});

describe("resumeListItemDtoSchema", () => {
  it("accepts valid list item", () => {
    expect(resumeListItemDtoSchema.safeParse(validListItem).success).toBe(true);
  });

  it("rejects negative versionCount", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        versionCount: -1,
      }).success
    ).toBe(false);
  });

  it("accepts latestRole null and topSkills empty array", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        latestRole: null,
        topSkills: [],
        sectionCount: 0,
      }).success
    ).toBe(true);
  });

  it("rejects sectionCount above 4", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        sectionCount: 5,
      }).success
    ).toBe(false);
  });

  it("rejects topSkills exceeding 5 entries", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        topSkills: ["a", "b", "c", "d", "e", "f"],
      }).success
    ).toBe(false);
  });

  it("rejects invalid language enum value", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        language: "Fr",
      }).success
    ).toBe(false);
  });

  it("rejects when isPrimary missing", () => {
    const { isPrimary, ...rest } = validListItem;
    void isPrimary;
    expect(resumeListItemDtoSchema.safeParse(rest).success).toBe(false);
  });
});

describe("resumeDetailDtoSchema", () => {
  it("accepts valid detail with nested versions", () => {
    const detail = {
      id: "33333333-3333-3333-3333-333333333333",
      name: "Master CV",
      createdAt: "2026-05-11T10:00:00Z",
      updatedAt: "2026-05-11T10:00:00Z",
      versions: [
        {
          id: "v1",
          kind: "Master",
          content: validContent,
          createdAt: "2026-05-11T10:00:00Z",
          updatedAt: "2026-05-11T10:00:00Z",
        },
      ],
    };
    expect(resumeDetailDtoSchema.safeParse(detail).success).toBe(true);
  });

  it("rejects when version.content broken", () => {
    const detail = {
      id: "33333333-3333-3333-3333-333333333333",
      name: "Master CV",
      createdAt: "2026-05-11T10:00:00Z",
      updatedAt: "2026-05-11T10:00:00Z",
      versions: [
        {
          id: "v1",
          kind: "Master",
          content: { ...validContent, personalInfo: { fullName: 123 } },
          createdAt: "2026-05-11T10:00:00Z",
          updatedAt: "2026-05-11T10:00:00Z",
        },
      ],
    };
    expect(resumeDetailDtoSchema.safeParse(detail).success).toBe(false);
  });
});

describe("getResumesResultSchema", () => {
  it("accepts valid paged result", () => {
    const result = {
      items: [validListItem],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(getResumesResultSchema.safeParse(result).success).toBe(true);
  });
});
