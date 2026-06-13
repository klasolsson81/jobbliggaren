import { describe, it, expect } from "vitest";
import {
  recentJobSearchDtoSchema,
  listRecentSearchesResultSchema,
} from "./recent-searches";

const wireBase = {
  id: "33333333-3333-3333-3333-333333333333",
  q: "backend",
  occupationGroupList: ["MVqp_eS8_kDZ"],
  municipalityList: ["zHxw_uJZ_NNh"],
  regionList: ["CifL_Rzy_Mku"],
  employmentTypeList: ["gro4_cWF_6D7"],
  worktimeExtentList: ["6YE1_gAC_R2G"],
  occupationGroupLabels: [
    { conceptId: "MVqp_eS8_kDZ", label: "Mjukvaruutveckling" },
  ],
  municipalityLabels: [{ conceptId: "zHxw_uJZ_NNh", label: "Solna" }],
  regionLabels: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
  sortBy: 0,
  label: "backend",
  currentCount: 42,
  newCount: 7,
  lastViewedAt: "2026-05-20T19:00:00Z",
};

describe("recentJobSearchDtoSchema", () => {
  it("parses a complete wire payload", () => {
    const parsed = recentJobSearchDtoSchema.parse(wireBase);
    expect(parsed.id).toBe(wireBase.id);
    expect(parsed.q).toBe("backend");
    expect(parsed.sortBy).toBe("PublishedAtDesc");
    expect(parsed.currentCount).toBe(42);
    expect(parsed.newCount).toBe(7);
  });

  it("accepts null q (only occupationGroup/region filter)", () => {
    const parsed = recentJobSearchDtoSchema.parse({ ...wireBase, q: null });
    expect(parsed.q).toBeNull();
  });

  it("defaults missing occupationGroupLabels/regionLabels to empty arrays", () => {
    const { occupationGroupLabels: _a, regionLabels: _b, ...rest } = wireBase;
    const parsed = recentJobSearchDtoSchema.parse(rest);
    expect(parsed.occupationGroupLabels).toEqual([]);
    expect(parsed.regionLabels).toEqual([]);
  });

  it("parses Relevance numeric sortBy index 4", () => {
    const parsed = recentJobSearchDtoSchema.parse({ ...wireBase, sortBy: 4 });
    expect(parsed.sortBy).toBe("Relevance");
  });

  it("rejects negative currentCount", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, currentCount: -1 })
    ).toThrow();
  });

  it("rejects negative newCount", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, newCount: -1 })
    ).toThrow();
  });

  it("rejects out-of-range sortBy index", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, sortBy: 9 })
    ).toThrow();
  });

  it("accepts multiple occupationGroup and region concept-ids", () => {
    const parsed = recentJobSearchDtoSchema.parse({
      ...wireBase,
      occupationGroupList: ["a1", "b2"],
      regionList: ["x1", "y2"],
    });
    expect(parsed.occupationGroupList).toEqual(["a1", "b2"]);
    expect(parsed.regionList).toEqual(["x1", "y2"]);
  });
});

describe("listRecentSearchesResultSchema", () => {
  it("parses an empty array", () => {
    const parsed = listRecentSearchesResultSchema.parse([]);
    expect(parsed).toEqual([]);
  });

  it("parses an array of recent searches", () => {
    const parsed = listRecentSearchesResultSchema.parse([wireBase, wireBase]);
    expect(parsed).toHaveLength(2);
  });
});
