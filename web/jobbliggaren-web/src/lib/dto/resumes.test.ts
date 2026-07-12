import { describe, it, expect } from "vitest";
import {
  atsTextResponseSchema,
  cvTemplateOptionsDtoSchema,
  getResumesResultSchema,
  resumeContentDtoSchema,
  resumeDetailDtoSchema,
  resumeLanguageSchema,
  resumeListItemDtoSchema,
  resumeVersionKindSchema,
  templateCatalogDtoSchema,
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
  openFindingCount: null,
  origin: "Import",
  template: "Klar",
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

  // Fas 4b AppCopy superset (ADR 0095): languages/sections är optional-med-default.
  it("parsar rent när languages OCH sections utelämnas (pre-superset back-compat)", () => {
    // validContent saknar redan båda fälten — pinnar att en pre-superset payload
    // (komplettera-föregångaren) fortfarande validerar.
    expect("languages" in validContent).toBe(false);
    expect("sections" in validContent).toBe(false);
    expect(resumeContentDtoSchema.safeParse(validContent).success).toBe(true);
  });

  it("accepterar en superset-payload med languages (NotStated) och sections", () => {
    const superset = {
      ...validContent,
      languages: [{ name: "Svenska", proficiency: "NotStated" }],
      sections: [
        { heading: "Projekt", entries: [{ title: "Jobbpilot", lines: ["Rad"] }] },
      ],
    };
    expect(resumeContentDtoSchema.safeParse(superset).success).toBe(true);
  });

  // #815: domänen tillåter en sektionspost UTAN titel ("Referenser / Lämnas på begäran."),
  // så API:t kan emittera `"title": null`. Läs-schemat MÅSTE ta emot det. Gjorde det inte
  // det skulle ett enda sådant sparat CV få HELA detaljsidan att falla till felläge —
  // skrivvägen hade kunnat persistera ett tillstånd läsvägen inte kan tolka.
  it.each([[null], [undefined]])(
    "accepterar en sektionspost vars titel är %s (domänen tillåter titellös post)",
    (title) => {
      const withTitlelessEntry = {
        ...validContent,
        sections: [
          {
            heading: "Referenser",
            entries: [{ title, lines: ["Lämnas på begäran."] }],
          },
        ],
      };

      expect(resumeContentDtoSchema.safeParse(withTitlelessEntry).success).toBe(true);
    },
  );

  it("avvisar ett språk med ogiltig proficiency-token (LÅST z.enum)", () => {
    const broken = {
      ...validContent,
      languages: [{ name: "Svenska", proficiency: "Flytande" }],
    };
    expect(resumeContentDtoSchema.safeParse(broken).success).toBe(false);
  });
});

describe("atsTextResponseSchema (Fas 4b PR-8.2 — kanonisk ATS-textvy)", () => {
  it("accepterar { source, text }", () => {
    expect(
      atsTextResponseSchema.safeParse({
        source: "Linearized",
        text: "Anna Andersson\nUtvecklare",
      }).success,
    ).toBe(true);
  });

  it("avvisar när text saknas", () => {
    expect(
      atsTextResponseSchema.safeParse({ source: "Linearized" }).success,
    ).toBe(false);
  });

  it("avvisar när source saknas", () => {
    expect(atsTextResponseSchema.safeParse({ text: "Anna" }).success).toBe(false);
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

  // Fas 4b PR-8 (CTO-bind Q1): de tre nya fälten är obligatoriska på wire.
  it("rejects when openFindingCount missing", () => {
    const { openFindingCount, ...rest } = validListItem;
    void openFindingCount;
    expect(resumeListItemDtoSchema.safeParse(rest).success).toBe(false);
  });

  it("rejects when origin missing", () => {
    const { origin, ...rest } = validListItem;
    void origin;
    expect(resumeListItemDtoSchema.safeParse(rest).success).toBe(false);
  });

  it("rejects when template missing", () => {
    const { template, ...rest } = validListItem;
    void template;
    expect(resumeListItemDtoSchema.safeParse(rest).success).toBe(false);
  });

  it("rejects unknown origin value (LÅST z.enum)", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        origin: "Nonsense",
      }).success,
    ).toBe(false);
  });

  // §5-ärlighet: null = inte granskad ("Granska"), 0 = granskad-och-ren, N = N kvar.
  it.each([null, 0, 3])(
    "accepts openFindingCount %s (null/0/N alla giltiga)",
    (openFindingCount) => {
      expect(
        resumeListItemDtoSchema.safeParse({
          ...validListItem,
          openFindingCount,
        }).success,
      ).toBe(true);
    },
  );

  it("rejects negative openFindingCount", () => {
    expect(
      resumeListItemDtoSchema.safeParse({
        ...validListItem,
        openFindingCount: -1,
      }).success,
    ).toBe(false);
  });
});

const validTemplateOptions = {
  template: "Klar",
  accentColor: "NavyBlue",
  fontPair: "Modern",
  density: "Normal",
  photoEnabled: false,
  photoShape: "Circle",
  effectiveAtsSafe: true,
};

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
      templateOptions: validTemplateOptions,
    };
    expect(resumeDetailDtoSchema.safeParse(detail).success).toBe(true);
  });

  it("rejects when templateOptions is missing (BE always emits it)", () => {
    const detail = {
      id: "33333333-3333-3333-3333-333333333333",
      name: "Master CV",
      createdAt: "2026-05-11T10:00:00Z",
      updatedAt: "2026-05-11T10:00:00Z",
      versions: [],
    };
    expect(resumeDetailDtoSchema.safeParse(detail).success).toBe(false);
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

describe("cvTemplateOptionsDtoSchema", () => {
  it("accepts the persisted six member names + effectiveAtsSafe", () => {
    expect(
      cvTemplateOptionsDtoSchema.safeParse(validTemplateOptions).success
    ).toBe(true);
  });

  it("rejects a non-boolean effectiveAtsSafe", () => {
    expect(
      cvTemplateOptionsDtoSchema.safeParse({
        ...validTemplateOptions,
        effectiveAtsSafe: "true",
      }).success
    ).toBe(false);
  });
});

describe("templateCatalogDtoSchema", () => {
  it("accepts the closed catalog shape (names + atsSafe + hex)", () => {
    const catalog = {
      templates: [
        { name: "Klar", atsSafe: true },
        { name: "MorkPanel", atsSafe: false },
      ],
      accents: [{ name: "NavyBlue", hex: "#1E3A5F" }],
      fontPairs: [{ name: "Modern" }, { name: "Classic" }],
      densities: [{ name: "Airy" }, { name: "Normal" }, { name: "Compact" }],
    };
    expect(templateCatalogDtoSchema.safeParse(catalog).success).toBe(true);
  });

  it("rejects a template entry missing atsSafe (FE must not re-derive it)", () => {
    const catalog = {
      templates: [{ name: "Klar" }],
      accents: [{ name: "NavyBlue", hex: "#1E3A5F" }],
      fontPairs: [{ name: "Modern" }],
      densities: [{ name: "Normal" }],
    };
    expect(templateCatalogDtoSchema.safeParse(catalog).success).toBe(false);
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
