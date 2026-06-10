import { describe, it, expect } from "vitest";
import {
  taxonomyTreeSchema,
  taxonomyLabelsResultSchema,
  taxonomyRegionSchema,
} from "./taxonomy";

const validTree = {
  regions: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
      ],
    },
  ],
};

describe("taxonomyTreeSchema", () => {
  it("accepts a well-formed tree (camelCase wire shape, ADR 0020)", () => {
    expect(taxonomyTreeSchema.safeParse(validTree).success).toBe(true);
  });

  it("accepts empty region/field arrays (degraded snapshot is still valid)", () => {
    expect(
      taxonomyTreeSchema.safeParse({ regions: [], occupationFields: [] })
        .success
    ).toBe(true);
  });

  it("rejects a region concept-id outside the 1–32 [A-Za-z0-9_-] format", () => {
    const bad = {
      ...validTree,
      regions: [{ conceptId: "bad id!", label: "X" }],
    };
    expect(taxonomyTreeSchema.safeParse(bad).success).toBe(false);
  });

  it("rejects an empty label (UI must always have a name to show)", () => {
    expect(
      taxonomyRegionSchema.safeParse({ conceptId: "CifL_Rzy_Mku", label: "" })
        .success
    ).toBe(false);
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
