import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  makeCreateResumeSchema,
  makeRenameResumeSchema,
  makeResumeContentSchema,
  makeUpdateMasterContentSchema,
} from "./resume-schemas";
import { pathToElementId } from "@/lib/forms/resume-path-routing";
import svValidation from "../../../messages/sv/validation.json";
import enValidation from "../../../messages/en/validation.json";

// Real next-intl translator scoped to the `validation` namespace (Swedish
// catalog = source of truth). In production the factories receive this `t` from
// `useTranslations("validation")` / `getTranslations("validation")`.
const t = createTranslator({
  locale: "sv",
  messages: { validation: svValidation },
  namespace: "validation",
});

const tEn = createTranslator({
  locale: "en",
  messages: { validation: enValidation },
  namespace: "validation",
});

const createResumeSchema = makeCreateResumeSchema(t);
const renameResumeSchema = makeRenameResumeSchema(t);
const resumeContentSchema = makeResumeContentSchema(t);
const updateMasterContentSchema = makeUpdateMasterContentSchema(t);

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

  // Ärligt frånvarande datum (CTO-bind 5a-pre): ingen redigeringsyta får tvinga
  // användaren att hitta på ett datum.
  it("accepts experience with absent startDate (honest date absence)", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [{ company: "Acme AB", role: "Utvecklare" }],
    });
    expect(result.success).toBe(true);
  });

  it("accepts end-only experience (endDate without startDate)", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [
        { company: "Acme AB", role: "Utvecklare", endDate: "2020-06-01" },
      ],
    });
    expect(result.success).toBe(true);
  });

  it("rejects rawPeriod longer than 100 chars", () => {
    const result = resumeContentSchema.safeParse({
      ...base,
      experiences: [
        { company: "Acme AB", role: "Utvecklare", rawPeriod: "x".repeat(101) },
      ],
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

describe("resumeContentSchema – refine() leaf-path regression (TD-40)", () => {
  // Bevakning: om någon i framtiden tar bort `path: ["endDate"]` från refines
  // i experienceSchema/educationSchema, eller skriver ny `.refine()` på en
  // z.object() utan explicit leaf-path, hamnar serverError på array-rot eller
  // toppnivå → ResumeContentForm.fieldA11y missar att flagga rätt fält +
  // pathToElementId returnerar null → ingen focus-flytt vid validerings-fel.
  // Testen kontraktslåser kompatibiliteten resume-schemas ↔ resume-path-routing.

  const baseContent = () => ({
    personalInfo: { fullName: "Anna Andersson" },
    experiences: [],
    educations: [],
    skills: [],
  });

  // Hittar issue via path-prefix (inte message-string) så framtida copy-tweaks
  // inte rödnar regression-bevakningen. Path är invarianten vi skyddar.
  const findIssueAtPath = (
    issues: ReadonlyArray<{ path: ReadonlyArray<PropertyKey> }>,
    path: string
  ) => issues.find((i) => i.path.join(".") === path);

  it("experiences refine pekar på leaf-path 'experiences.0.endDate' → pathToElementId mappar non-null", () => {
    const result = resumeContentSchema.safeParse({
      ...baseContent(),
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
    if (result.success) return;

    expect(findIssueAtPath(result.error.issues, "experiences.0.endDate")).toBeDefined();
    expect(pathToElementId("experiences.0.endDate")).toBe("exp-0-endDate");
  });

  it("educations refine pekar på leaf-path 'educations.0.endDate' → pathToElementId mappar non-null", () => {
    const result = resumeContentSchema.safeParse({
      ...baseContent(),
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
    if (result.success) return;

    expect(findIssueAtPath(result.error.issues, "educations.0.endDate")).toBeDefined();
    expect(pathToElementId("educations.0.endDate")).toBe("edu-0-endDate");
  });

  it("refine path bevarar array-index → pathToElementId mappar rätt fält för icke-0-index", () => {
    const result = resumeContentSchema.safeParse({
      ...baseContent(),
      experiences: [
        // Index 0 — valid (ska inte trigga refine)
        {
          company: "Valid",
          role: "Dev",
          startDate: "2020-01-01",
          endDate: "2021-01-01",
        },
        // Index 1 — invalid (triggerar refine)
        {
          company: "Bad",
          role: "Dev",
          startDate: "2025-06-01",
          endDate: "2024-01-01",
        },
      ],
    });

    expect(result.success).toBe(false);
    if (result.success) return;

    expect(findIssueAtPath(result.error.issues, "experiences.1.endDate")).toBeDefined();
    expect(pathToElementId("experiences.1.endDate")).toBe("exp-1-endDate");
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

describe("makeOptionalNullableString – per-field localized over-length message", () => {
  // Regression: the over-length error noun must be localized too. Previously a
  // shared `resume.maxChars` template received a hardcoded Swedish label
  // ("Telefonnummer"/"Ort"/"Beskrivning"), so an EN user saw "Telefonnummer may
  // be at most 50 characters." under a field labelled "Phone". We assert the
  // *message text* (not just `.success`) in both catalogs so the leak can't
  // silently return.
  const overLength = {
    phone: "0".repeat(51),
    location: "a".repeat(201),
    description: "a".repeat(2001),
  };

  function firstMessage(
    schema: ReturnType<typeof makeResumeContentSchema>,
    content: unknown,
  ): string {
    const result = schema.safeParse(content);
    expect(result.success).toBe(false);
    if (result.success) throw new Error("expected validation failure");
    return result.error.issues[0]?.message ?? "";
  }

  const svSchema = makeResumeContentSchema(t);
  const enSchema = makeResumeContentSchema(tEn);

  const baseExperience = {
    company: "Acme",
    role: "Dev",
    startDate: "2024-01-01",
  };

  it("phone over-length resolves to the SV catalog string", () => {
    const message = firstMessage(svSchema, {
      personalInfo: { fullName: "Anna", phone: overLength.phone },
      experiences: [],
      educations: [],
      skills: [],
    });
    expect(message).toBe(svValidation.resume.phoneMax);
  });

  it("phone over-length resolves to the EN catalog string (no Swedish leak)", () => {
    const message = firstMessage(enSchema, {
      personalInfo: { fullName: "Anna", phone: overLength.phone },
      experiences: [],
      educations: [],
      skills: [],
    });
    expect(message).toBe(enValidation.resume.phoneMax);
    expect(message).not.toMatch(/Telefon/);
  });

  it("location over-length resolves to the EN catalog string (no Swedish leak)", () => {
    const message = firstMessage(enSchema, {
      personalInfo: { fullName: "Anna", location: overLength.location },
      experiences: [],
      educations: [],
      skills: [],
    });
    expect(message).toBe(enValidation.resume.locationMax);
    expect(message).not.toMatch(/Ort/);
  });

  it("description over-length resolves to the EN catalog string (no Swedish leak)", () => {
    const message = firstMessage(enSchema, {
      personalInfo: { fullName: "Anna" },
      experiences: [{ ...baseExperience, description: overLength.description }],
      educations: [],
      skills: [],
    });
    expect(message).toBe(enValidation.resume.descriptionMax);
    expect(message).not.toMatch(/Beskrivning/);
  });
});
