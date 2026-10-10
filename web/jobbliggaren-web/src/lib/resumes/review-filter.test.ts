import { describe, expect, it } from "vitest";
import {
  groupVerdicts,
  matchesFilter,
  matchesOutcome,
  outcomeCounts,
  parseReviewFilter,
  reviewFilterSearch,
} from "./review-filter";
import type {
  CriterionVerdict,
  CvCriterionVerdictDto,
  CvReviewDto,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

function verdict(
  criterionId: string,
  category: RubricCategory,
  v: CriterionVerdict,
  overrides: Partial<CvCriterionVerdictDto> = {},
): CvCriterionVerdictDto {
  return {
    criterionId,
    name: criterionId,
    category,
    verdict: v,
    evidence: [],
    notAssessedReason: null,
    userStatus: null,
    userStatusStaleAt: null,
    isIgnorable: false,
    ...overrides,
  };
}

function review(
  verdicts: CvCriterionVerdictDto[],
  critical: string[] = [],
  categories: RubricCategory[] = ["Content", "Structure", "Language", "AtsParsability"],
): CvReviewDto {
  return {
    rubricVersion: "2.3.0",
    profile: "Ats",
    categories: categories.map((category) => ({
      category,
      passCount: 0,
      warnCount: 0,
      failCount: 0,
      notAssessedCount: 0,
      band: null,
    })),
    verdicts,
    criticalFails: verdicts.filter((v) => critical.includes(v.criterionId)),
    assessedCount: 0,
    totalCount: verdicts.length,
  };
}

const ATS: RubricCategory[] = ["Content", "Structure", "Language", "AtsParsability"];

describe("parseReviewFilter", () => {
  it("reads a known dimension and outcome", () => {
    const params = new URLSearchParams("dim=Structure&visa=todo");
    expect(parseReviewFilter(params, ATS)).toEqual({ dim: "Structure", visa: "todo" });
  });

  it("defaults to every dimension and Alla", () => {
    expect(parseReviewFilter(new URLSearchParams(""), ATS)).toEqual({ dim: null, visa: "all" });
  });

  it("drops a dimension this profile does not have", () => {
    const params = new URLSearchParams("dim=VisualQuality");
    expect(parseReviewFilter(params, ATS).dim).toBeNull();
  });

  it("drops unknown values, case-sensitively", () => {
    const params = new URLSearchParams("dim=structure&visa=TODO");
    expect(parseReviewFilter(params, ATS)).toEqual({ dim: null, visa: "all" });
  });
});

describe("matchesOutcome", () => {
  it.each<[CriterionVerdict, string, boolean]>([
    ["Fail", "todo", true],
    ["Warn", "todo", true],
    ["Pass", "todo", false],
    ["NotAssessed", "todo", false],
    ["Pass", "pass", true],
    ["Warn", "pass", false],
    ["NotAssessed", "na", true],
    ["Fail", "na", false],
    ["NotAssessed", "all", true],
    ["Fail", "all", true],
  ])("%s under %s is %s", (v, visa, expected) => {
    expect(matchesOutcome(v, visa as "todo" | "pass" | "na" | "all")).toBe(expected);
  });

  it("keeps a row marked Åtgärdad or Ignorerad under Att åtgärda", () => {
    const resolved = verdict("A8", "Content", "Fail", { userStatus: "Resolved" });
    const ignored = verdict("C2", "Language", "Warn", { userStatus: "Ignored" });
    const filter = { dim: null, visa: "todo" } as const;
    expect(matchesFilter(resolved, filter)).toBe(true);
    expect(matchesFilter(ignored, filter)).toBe(true);
  });
});

describe("outcomeCounts", () => {
  const rows = [
    verdict("A1", "Content", "Fail"),
    verdict("A2", "Content", "Pass"),
    verdict("A3", "Content", "NotAssessed"),
    verdict("B1", "Structure", "Warn"),
    verdict("B2", "Structure", "Pass"),
  ];

  it("counts every dimension when none is selected", () => {
    expect(outcomeCounts(rows, null)).toEqual({ todo: 2, pass: 2, na: 1, all: 5 });
  });

  it("counts only the selected dimension", () => {
    expect(outcomeCounts(rows, "Structure")).toEqual({ todo: 1, pass: 1, na: 0, all: 2 });
  });
});

describe("groupVerdicts", () => {
  it("orders a group Underkänt, Delvis, Godkänt, Ej bedömt", () => {
    const groups = groupVerdicts(
      review([
        verdict("A1", "Content", "Pass"),
        verdict("A2", "Content", "NotAssessed"),
        verdict("A3", "Content", "Warn"),
        verdict("A4", "Content", "Fail"),
      ]),
    );
    expect(groups[0]?.verdicts.map((v) => v.criterionId)).toEqual(["A4", "A3", "A1", "A2"]);
  });

  it("puts critical criteria first within an outcome, then keeps the rubric order", () => {
    const groups = groupVerdicts(
      review(
        [
          verdict("A1", "Content", "Fail"),
          verdict("A2", "Content", "Fail"),
          verdict("A3", "Content", "Fail"),
        ],
        ["A3"],
      ),
    );
    expect(groups[0]?.verdicts.map((v) => v.criterionId)).toEqual(["A3", "A1", "A2"]);
  });

  it("follows the review's dimension order and keeps empty dimensions as empty groups", () => {
    const groups = groupVerdicts(
      review([verdict("C1", "Language", "Pass"), verdict("A1", "Content", "Pass")]),
    );
    expect(groups.map((g) => g.category)).toEqual(ATS);
    expect(groups.find((g) => g.category === "Structure")?.verdicts).toEqual([]);
  });

  it("never drops a verdict whose dimension the review does not list", () => {
    const groups = groupVerdicts(
      review([verdict("E1", "VisualQuality", "NotAssessed")], [], ["Content"]),
    );
    expect(groups.map((g) => g.category)).toEqual(["Content", "VisualQuality"]);
    expect(groups[1]?.verdicts).toHaveLength(1);
  });
});

describe("reviewFilterSearch", () => {
  it("keeps the profile and writes both filters", () => {
    expect(reviewFilterSearch("?profile=Visual", { dim: "Content", visa: "na" })).toBe(
      "?profile=Visual&dim=Content&visa=na",
    );
  });

  it("leaves the defaults out", () => {
    expect(reviewFilterSearch("?profile=Ats&dim=Content&visa=todo", { dim: null, visa: "all" })).toBe(
      "?profile=Ats",
    );
  });

  it("returns an empty query rather than a bare question mark", () => {
    expect(reviewFilterSearch("?visa=pass", { dim: null, visa: "all" })).toBe("");
  });
});
