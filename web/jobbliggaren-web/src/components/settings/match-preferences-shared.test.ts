import { describe, expect, it } from "vitest";
import {
  projectOccupationExperience,
  recordFromOccupationExperience,
} from "./match-preferences-shared";

// exp-per-occ (ADR 0079-amendment PR-4) — the load-bearing pure overlay helpers. A direct
// regression anchor for the 0-vs-null distinction + the subset/orphan projection that the
// component tests exercise only indirectly.

describe("recordFromOccupationExperience", () => {
  it("preserves 0 and null distinctly when building the draft map", () => {
    const record = recordFromOccupationExperience([
      { conceptId: "grp_a", years: 0 }, // a parsed sub-year role
      { conceptId: "grp_b", years: null }, // stated but unspecified
      { conceptId: "grp_c", years: 7 },
    ]);

    expect(record).toEqual({ grp_a: 0, grp_b: null, grp_c: 7 });
    // 0 must NOT collapse to null/absent.
    expect(record.grp_a).toBe(0);
    expect(record.grp_b).toBeNull();
  });

  it("returns an empty map for an empty overlay", () => {
    expect(recordFromOccupationExperience([])).toEqual({});
  });
});

describe("projectOccupationExperience", () => {
  it("projects only still-selected occupations (subset rule), preserving 0 and null", () => {
    const overlay = { grp_a: 0, grp_b: null, grp_c: 7 };

    const result = projectOccupationExperience(overlay, ["grp_a", "grp_b", "grp_c"]);

    expect(result).toEqual([
      { conceptId: "grp_a", years: 0 },
      { conceptId: "grp_b", years: null },
      { conceptId: "grp_c", years: 7 },
    ]);
  });

  it("drops the row for an occupation that is no longer selected (orphan guard)", () => {
    const overlay = { grp_a: 5, grp_removed: 3 };

    const result = projectOccupationExperience(overlay, ["grp_a"]);

    expect(result).toEqual([{ conceptId: "grp_a", years: 5 }]);
    expect(result.some((e) => e.conceptId === "grp_removed")).toBe(false);
  });

  it("omits a selected occupation that has no overlay key (no opinion ≠ null row)", () => {
    const overlay = { grp_a: 5 };

    const result = projectOccupationExperience(overlay, ["grp_a", "grp_no_key"]);

    // grp_no_key is selected but never given years → omitted entirely, not a {years:null} row.
    expect(result).toEqual([{ conceptId: "grp_a", years: 5 }]);
  });

  it("follows the selected-occupation order (determinism)", () => {
    const overlay = { grp_a: 1, grp_b: 2 };

    expect(projectOccupationExperience(overlay, ["grp_b", "grp_a"])).toEqual([
      { conceptId: "grp_b", years: 2 },
      { conceptId: "grp_a", years: 1 },
    ]);
  });
});
