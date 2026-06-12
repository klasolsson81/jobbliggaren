import { describe, it, expect } from "vitest";
import {
  savedSearchDtoSchema,
  createSavedSearchSchema,
  sortByToIndex,
  SAVED_SEARCH_SORT_ORDER,
  MAX_CONCEPT_IDS,
} from "./saved-searches";

// ADR 0067 Fas C2/B2 — list-dimensionerna är arrays på wire (aldrig null från
// VO:t): occupationGroup/municipality/region + Klass 2 employmentType/
// worktimeExtent. Backend camelCase (System.Text.Json), SortBy som heltal.
const wireBase = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "Java i Stockholm",
  occupationGroup: ["MVqp_eS8_kDZ"],
  municipality: ["dMFa_Rsm_4aA"],
  region: ["CifL_Rzy_Mku"],
  employmentType: ["PFZr_Syz_cUq"],
  worktimeExtent: ["6YE1_gAC_R2G"],
  q: "java",
  notificationEnabled: false,
  lastRunAt: null,
  createdAt: "2026-05-16T08:00:00Z",
  updatedAt: "2026-05-16T08:00:00Z",
};

describe("savedSearchDtoSchema", () => {
  it("parses numeric sortBy (backend enum-int wire form) to string union", () => {
    const parsed = savedSearchDtoSchema.parse({ ...wireBase, sortBy: 2 });
    expect(parsed.sortBy).toBe("ExpiresAtDesc");
  });

  it("also accepts string sortBy (forward-compat if converter added)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      sortBy: "PublishedAtAsc",
    });
    expect(parsed.sortBy).toBe("PublishedAtAsc");
  });

  it("rejects out-of-range numeric sortBy index", () => {
    expect(() =>
      savedSearchDtoSchema.parse({ ...wireBase, sortBy: 9 })
    ).toThrow();
  });

  it("accepts empty criteria arrays + null q (only sort set is still valid)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      occupationGroup: [],
      municipality: [],
      region: [],
      employmentType: [],
      worktimeExtent: [],
      q: null,
      sortBy: 0,
    });
    expect(parsed.occupationGroup).toEqual([]);
    expect(parsed.region).toEqual([]);
  });

  it("parses Relevance numeric sortBy index 4 (ADR 0042 Beslut D)", () => {
    const parsed = savedSearchDtoSchema.parse({ ...wireBase, sortBy: 4 });
    expect(parsed.sortBy).toBe("Relevance");
  });

  it("accepts multiple occupationGroup/region values (OR-bevakning)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      occupationGroup: ["a1", "b2"],
      region: ["r1", "r2"],
      sortBy: 0,
    });
    expect(parsed.occupationGroup).toEqual(["a1", "b2"]);
  });

  it("parses Klass 2 employmentType/worktimeExtent arrays (ADR 0067 Beslut 6)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      employmentType: ["et1", "et2"],
      worktimeExtent: ["wt1"],
      sortBy: 0,
    });
    expect(parsed.employmentType).toEqual(["et1", "et2"]);
    expect(parsed.worktimeExtent).toEqual(["wt1"]);
  });

  it("parses additive per-dimension labels (ADR 0043 Approach A)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      sortBy: 0,
      occupationGroupLabels: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
      ],
      regionLabels: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
    });
    expect(parsed.occupationGroupLabels).toEqual([
      { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
    ]);
    expect(parsed.regionLabels[0]?.label).toBe("Stockholms län");
  });

  it("defaults label lists to [] when absent (detalj-endpoint scope)", () => {
    const parsed = savedSearchDtoSchema.parse({ ...wireBase, sortBy: 0 });
    expect(parsed.occupationGroupLabels).toEqual([]);
    expect(parsed.municipalityLabels).toEqual([]);
    expect(parsed.regionLabels).toEqual([]);
  });

  it("accepts a stale-id label verbatim (backend 'Okänd kod (<id>)')", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      sortBy: 0,
      occupationGroupLabels: [
        { conceptId: "gone_99", label: "Okänd kod (gone_99)" },
      ],
    });
    expect(parsed.occupationGroupLabels[0]?.label).toBe("Okänd kod (gone_99)");
  });
});

describe("createSavedSearchSchema", () => {
  const ok = {
    name: "Min sökning",
    occupationGroup: [] as string[],
    municipality: [] as string[],
    region: [] as string[],
    employmentType: [] as string[],
    worktimeExtent: [] as string[],
    q: "java",
    sortBy: "PublishedAtDesc" as const,
  };

  it("accepts a valid payload with at least one criterion", () => {
    expect(createSavedSearchSchema.safeParse(ok).success).toBe(true);
  });

  it("requires a name", () => {
    const r = createSavedSearchSchema.safeParse({ ...ok, name: "" });
    expect(r.success).toBe(false);
  });

  it("rejects name over 120 chars", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      name: "x".repeat(121),
    });
    expect(r.success).toBe(false);
  });

  it("accepts multiple occupationGroup/region values (ADR 0042 Beslut B OR-bevakning)", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      occupationGroup: ["MVqp_eS8_kDZ", "CifL_Rzy_Mku"],
      region: ["a1"],
    });
    expect(r.success).toBe(true);
  });

  it("accepts a Klass 2-only criterion (employmentType, ADR 0067 Beslut 6)", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      employmentType: ["et1"],
    });
    expect(r.success).toBe(true);
  });

  it("rejects empty criteria (no list/q) — mirrors backend SearchCriteria invariant", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
    });
    expect(r.success).toBe(false);
  });

  it("rejects invalid concept-id format", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      occupationGroup: ["inv alid!"],
    });
    expect(r.success).toBe(false);
  });

  it(`rejects more than ${MAX_CONCEPT_IDS} values (cap mirrors backend)`, () => {
    const tooMany = Array.from(
      { length: MAX_CONCEPT_IDS + 1 },
      (_, i) => `code${i}`
    );
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      occupationGroup: tooMany,
    });
    expect(r.success).toBe(false);
  });

  it("rejects q shorter than 2 chars", () => {
    const r = createSavedSearchSchema.safeParse({ ...ok, q: "a" });
    expect(r.success).toBe(false);
  });
});

describe("sortByToIndex", () => {
  it("round-trips with SAVED_SEARCH_SORT_ORDER", () => {
    SAVED_SEARCH_SORT_ORDER.forEach((name, i) => {
      expect(sortByToIndex(name)).toBe(i);
    });
  });
});
