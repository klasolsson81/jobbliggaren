import { describe, it, expect } from "vitest";
import {
  skillOptionSchema,
  skillOptionsSchema,
  skillOptionGroupSchema,
  skillOptionGroupsSchema,
  skillProposalGroupSchema,
} from "./skills";

describe("skillOptionSchema (STEG 3 / ADR 0079)", () => {
  it("accepterar ett giltigt {conceptId, label}", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "skill_react", label: "React" })
        .success
    ).toBe(true);
  });

  it("avvisar tom label", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "skill_react", label: "" }).success
    ).toBe(false);
  });

  it("avvisar conceptId med ogiltiga tecken (defense-in-depth)", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "bad id!", label: "X" }).success
    ).toBe(false);
  });

  it("avvisar conceptId över 32 tecken", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "a".repeat(33), label: "X" })
        .success
    ).toBe(false);
  });
});

describe("skillOptionsSchema", () => {
  it("accepterar en tom lista", () => {
    expect(skillOptionsSchema.safeParse([]).success).toBe(true);
  });

  it("accepterar en lista av giltiga optioner", () => {
    expect(
      skillOptionsSchema.safeParse([
        { conceptId: "skill_sql", label: "SQL" },
        { conceptId: "skill_react", label: "React" },
      ]).success
    ).toBe(true);
  });
});

describe("skillOptionGroupSchema (#277 — twin chips, search/resolve)", () => {
  it("normaliserar en twin-grupp till {conceptId, label, memberConceptIds}", () => {
    const parsed = skillOptionGroupSchema.safeParse({
      canonicalConceptId: "esco_csharp",
      label: "C#",
      memberConceptIds: ["esco_csharp", "af_csharp"],
    });
    expect(parsed.success).toBe(true);
    expect(parsed.success && parsed.data).toEqual({
      conceptId: "esco_csharp",
      label: "C#",
      memberConceptIds: ["esco_csharp", "af_csharp"],
    });
  });

  it("graceful: en SAKNAD memberConceptIds blir en singleton-grupp [canonical] (deploy-skew)", () => {
    const parsed = skillOptionGroupSchema.safeParse({
      canonicalConceptId: "skill_react",
      label: "React",
    });
    expect(parsed.success).toBe(true);
    expect(parsed.success && parsed.data.memberConceptIds).toEqual([
      "skill_react",
    ]);
  });

  it("graceful: en TOM memberConceptIds blir [canonical]", () => {
    const parsed = skillOptionGroupSchema.safeParse({
      canonicalConceptId: "skill_react",
      label: "React",
      memberConceptIds: [],
    });
    expect(parsed.success && parsed.data.memberConceptIds).toEqual([
      "skill_react",
    ]);
  });

  it("canonical leder alltid och member-id dedupas, ordning bevaras annars", () => {
    const parsed = skillOptionGroupSchema.safeParse({
      canonicalConceptId: "esco_csharp",
      // canonical listad mitt i + en dubblett: canonical lyfts först, dubbletter bort.
      memberConceptIds: ["af_csharp", "esco_csharp", "af_csharp", "x_csharp"],
      label: "C#",
    });
    expect(parsed.success && parsed.data.memberConceptIds).toEqual([
      "esco_csharp",
      "af_csharp",
      "x_csharp",
    ]);
  });

  it("avvisar tom label och ogiltigt canonical-id", () => {
    expect(
      skillOptionGroupSchema.safeParse({
        canonicalConceptId: "ok",
        label: "",
      }).success
    ).toBe(false);
    expect(
      skillOptionGroupSchema.safeParse({
        canonicalConceptId: "bad id!",
        label: "X",
      }).success
    ).toBe(false);
  });

  it("avvisar ett member-id med ogiltiga tecken (defense-in-depth)", () => {
    expect(
      skillOptionGroupSchema.safeParse({
        canonicalConceptId: "ok",
        label: "X",
        memberConceptIds: ["ok", "bad id!"],
      }).success
    ).toBe(false);
  });
});

describe("skillOptionGroupsSchema", () => {
  it("accepterar en tom lista", () => {
    expect(skillOptionGroupsSchema.safeParse([]).success).toBe(true);
  });

  it("accepterar och normaliserar en lista av grupper (med och utan members)", () => {
    const parsed = skillOptionGroupsSchema.safeParse([
      {
        canonicalConceptId: "esco_csharp",
        label: "C#",
        memberConceptIds: ["esco_csharp", "af_csharp"],
      },
      { canonicalConceptId: "skill_sql", label: "SQL" },
    ]);
    expect(parsed.success).toBe(true);
    expect(parsed.success && parsed.data).toEqual([
      {
        conceptId: "esco_csharp",
        label: "C#",
        memberConceptIds: ["esco_csharp", "af_csharp"],
      },
      { conceptId: "skill_sql", label: "SQL", memberConceptIds: ["skill_sql"] },
    ]);
  });
});

describe("skillProposalGroupSchema (#277 — CV-förslag)", () => {
  it("normaliserar {conceptId, label, memberConceptIds} (conceptId = canonical)", () => {
    const parsed = skillProposalGroupSchema.safeParse({
      conceptId: "esco_csharp",
      label: "C#",
      memberConceptIds: ["esco_csharp", "af_csharp"],
    });
    expect(parsed.success && parsed.data).toEqual({
      conceptId: "esco_csharp",
      label: "C#",
      memberConceptIds: ["esco_csharp", "af_csharp"],
    });
  });

  it("graceful: saknad memberConceptIds → [conceptId]", () => {
    const parsed = skillProposalGroupSchema.safeParse({
      conceptId: "skill_react",
      label: "React",
    });
    expect(parsed.success && parsed.data.memberConceptIds).toEqual([
      "skill_react",
    ]);
  });
});
