import { describe, it, expect } from "vitest";
import {
  taxonomyTreeSchema,
  taxonomyLabelsResultSchema,
  taxonomyRegionSchema,
} from "./taxonomy";

const validTree = {
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [
        { conceptId: "AvNB_uwa_6n6", label: "Stockholm" },
        { conceptId: "zHxw_uJZ_NNh", label: "Solna" },
      ],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
      ],
    },
  ],
  // ADR 0043-amendment 2026-06-13 (Klass 2) — platta options.
  employmentTypes: [{ conceptId: "PFZr_Syz_cUq", label: "Vanlig anställning" }],
  worktimeExtents: [{ conceptId: "6YE1_gAC_R2G", label: "Heltid" }],
};

describe("taxonomyTreeSchema", () => {
  it("accepts a well-formed tree (camelCase wire shape, ADR 0020)", () => {
    expect(taxonomyTreeSchema.safeParse(validTree).success).toBe(true);
  });

  it("accepts empty region/field arrays (degraded snapshot is still valid)", () => {
    expect(
      taxonomyTreeSchema.safeParse({
        regions: [],
        occupationFields: [],
        employmentTypes: [],
        worktimeExtents: [],
      }).success
    ).toBe(true);
  });

  it("rejects a tree WITHOUT employmentTypes (REQUIRED — Klass 2 contract drift must fail loud)", () => {
    const { employmentTypes, ...missing } = validTree;
    void employmentTypes;
    expect(taxonomyTreeSchema.safeParse(missing).success).toBe(false);
  });

  it("rejects a tree WITHOUT worktimeExtents (REQUIRED — Klass 2 contract drift must fail loud)", () => {
    const { worktimeExtents, ...missing } = validTree;
    void worktimeExtents;
    expect(taxonomyTreeSchema.safeParse(missing).success).toBe(false);
  });

  it("rejects a Klass 2 option concept-id outside the 1–32 format (defense-in-depth)", () => {
    const bad = {
      ...validTree,
      worktimeExtents: [{ conceptId: "bad id!", label: "Heltid" }],
    };
    expect(taxonomyTreeSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects a region concept-id outside the 1–32 [A-Za-z0-9_-] format", () => {
    const bad = {
      ...validTree,
      regions: [{ conceptId: "bad id!", label: "X", municipalities: [] }],
    };
    expect(taxonomyTreeSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects an empty label (UI must always have a name to show)", () => {
    expect(
      taxonomyRegionSchema.safeParse({
        conceptId: "CifL_Rzy_Mku",
        label: "",
        municipalities: [],
      }).success
    ).toBe(false);
  });

  it("rejects a region WITHOUT municipalities array (REQUIRED — contract drift must fail loud, E2b)", () => {
    const missing = {
      ...validTree,
      regions: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
    };
    expect(taxonomyTreeSchema.safeParse(missing).success).toBe(false);
  });

  it("rejects a municipality concept-id outside the format (defense-in-depth)", () => {
    const bad = {
      ...validTree,
      regions: [
        {
          conceptId: "CifL_Rzy_Mku",
          label: "Stockholms län",
          municipalities: [{ conceptId: "bad id!", label: "X" }],
        },
      ],
    };
    expect(taxonomyTreeSchema.safeParse(bad).success).toBe(false);
  });

  it("accepts a region with empty municipalities (backend guarantees the array, may be empty)", () => {
    const emptyMunis = {
      ...validTree,
      regions: [
        { conceptId: "CifL_Rzy_Mku", label: "Stockholms län", municipalities: [] },
      ],
    };
    expect(taxonomyTreeSchema.safeParse(emptyMunis).success).toBe(true);
  });

  it("rejects PascalCase keys (guards against ADR 0020 casing drift)", () => {
    const pascal = {
      Regions: [{ ConceptId: "CifL_Rzy_Mku", Label: "Stockholms län" }],
      OccupationFields: [],
    };
    expect(taxonomyTreeSchema.safeParse(pascal).success).toBe(false);
  });
});

describe("taxonomyLabelsResultSchema", () => {
  it("accepts reverse-lookup rows", () => {
    const rows = [{ conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" }];
    expect(taxonomyLabelsResultSchema.safeParse(rows).success).toBe(true);
  });

  it("accepts the backend 'Okänd kod (<id>)' fallback label as plain text", () => {
    const rows = [{ conceptId: "stale_id", label: "Okänd kod (stale_id)" }];
    const parsed = taxonomyLabelsResultSchema.safeParse(rows);
    expect(parsed.success).toBe(true);
  });

  it("does NOT constrain conceptId format here (stale saved id may differ)", () => {
    // En sparad sökning kan bära ett id vars format inte matchar nuvarande
    // mönster — reverse-lookup ska ändå returnera en rad, inte 400:a på FE.
    const rows = [{ conceptId: "legacy.id.with.dots", label: "Gammalt namn" }];
    expect(taxonomyLabelsResultSchema.safeParse(rows).success).toBe(true);
  });

  it("accepts an empty result", () => {
    expect(taxonomyLabelsResultSchema.safeParse([]).success).toBe(true);
  });
});
