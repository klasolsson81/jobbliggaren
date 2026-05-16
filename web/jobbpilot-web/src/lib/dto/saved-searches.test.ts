import { describe, it, expect } from "vitest";
import {
  savedSearchDtoSchema,
  createSavedSearchSchema,
  sortByToIndex,
  SAVED_SEARCH_SORT_ORDER,
} from "./saved-searches";

const wireBase = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "Java i Stockholm",
  ssyk: "MVqp_eS8_kDZ",
  region: "CifL_Rzy_Mku",
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

  it("accepts null criteria fields (only sort set is still a valid stored row)", () => {
    const parsed = savedSearchDtoSchema.parse({
      ...wireBase,
      ssyk: null,
      region: null,
      q: null,
      sortBy: 0,
    });
    expect(parsed.ssyk).toBeNull();
  });
});

describe("createSavedSearchSchema", () => {
  const ok = {
    name: "Min sökning",
    ssyk: "",
    region: "",
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

  it("rejects empty criteria (no ssyk/region/q) — mirrors backend SearchCriteria invariant", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      ssyk: "",
      region: "",
    });
    expect(r.success).toBe(false);
  });

  it("rejects invalid SSYK concept-id format", () => {
    const r = createSavedSearchSchema.safeParse({
      ...ok,
      q: "",
      ssyk: "inv alid!",
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
