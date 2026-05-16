import { describe, it, expect } from "vitest";
import {
  savedSearchDtoSchema,
  createSavedSearchSchema,
  sortByToIndex,
  SAVED_SEARCH_SORT_ORDER,
} from "./saved-searches";

// ADR 0042 Beslut B — ssyk/region är arrays på wire (aldrig null från VO).
const wireBase = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "Java i Stockholm",
  ssyk: ["MVqp_eS8_kDZ"],
  region: ["CifL_Rzy_Mku"],
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
      ssyk: [],
      region: [],
      q: null,
      sortBy: 0,
    });
    expect(parsed.ssyk).toEqual([]);
    expect(parsed.region).toEqual([]);
  });

  it("parses Relevance numeric sortBy index 4 (ADR 0042 Beslut D)", () => {
    const parsed = savedSearchDtoSchema.parse({ ...wireBase, sortBy: 4 });
    expect(parsed.sortBy).toBe("Relevance");
  });

  it("accepts multiple ssyk/region values (OR-bevakning)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      ssyk: ["a1", "b2"],
      region: ["r1", "r2"],
      sortBy: 0,
    });
    expect(parsed.ssyk).toEqual(["a1", "b2"]);
  });
});

describe("createSavedSearchSchema", () => {
  const ok = {
    name: "Min sökning",
    ssyk: [] as string[],
    region: [] as string[],
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

  it("accepts multiple ssyk/region values (ADR 0042 Beslut B OR-bevakning)", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      ssyk: ["MVqp_eS8_kDZ", "CifL_Rzy_Mku"],
      region: ["a1"],
    });
    expect(r.success).toBe(true);
  });

  it("rejects empty criteria (no ssyk/region/q) — mirrors backend SearchCriteria invariant", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      ssyk: [],
      region: [],
    });
    expect(r.success).toBe(false);
  });

  it("rejects invalid SSYK concept-id format", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      ssyk: ["inv alid!"],
    });
    expect(r.success).toBe(false);
  });

  it("rejects more than 10 ssyk values (cap mirrors backend)", () => {
    const eleven = Array.from({ length: 11 }, (_, i) => `code${i}`);
    const r = createSavedSearchSchema.safeParse({ ...ok, ssyk: eleven });
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
