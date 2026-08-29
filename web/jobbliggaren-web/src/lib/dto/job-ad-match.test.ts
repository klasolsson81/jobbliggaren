import { describe, it, expect } from "vitest";
import {
  LIST_MATCH_GRADES,
  WATCH_MATCHING_GRADES,
  isListMatchGrade,
  matchGradeSchema,
  jobAdMatchBatchSchema,
} from "./job-ad-match";

/**
 * #300 PR-5 (ADR 0084) — pinnar grad-taxonomins SSOT efter att `Related`
 * landat: den ordinala filter-ordningen, typvakten och — viktigast — att
 * `matchGradeSchema` ACCEPTERAR `Related` (annars `.catch({})`:ar batch-mappen
 * HELA sidan till tomt så fort en related-graderad annons dyker upp, samma
 * page-wipe-fälla som Top — CTO D2).
 */
describe("LIST_MATCH_GRADES (grad-taxonomins SSOT, #300 PR-5)", () => {
  it("ordinal ordning är [Basic, Related, Good, Strong] — Related mellan Basic och Good", () => {
    expect(LIST_MATCH_GRADES).toEqual(["Basic", "Related", "Good", "Strong"]);
  });

  it("innehåller ALDRIG Top (listfiltret är Fast-bandet, kan inte beräkna Topp)", () => {
    expect(LIST_MATCH_GRADES).not.toContain("Top");
  });

  it("isListMatchGrade accepterar de fyra filtrerbara graderna, avvisar Top + okänt", () => {
    expect(isListMatchGrade("Basic")).toBe(true);
    expect(isListMatchGrade("Related")).toBe(true);
    expect(isListMatchGrade("Good")).toBe(true);
    expect(isListMatchGrade("Strong")).toBe(true);
    expect(isListMatchGrade("Top")).toBe(false);
    expect(isListMatchGrade("Nonsense")).toBe(false);
  });
});

describe("matchGradeSchema (page-wipe-vakt, #300 PR-5)", () => {
  it("accepterar Related (annars blankar batch-mappen hela sidan)", () => {
    expect(matchGradeSchema.safeParse("Related").success).toBe(true);
  });

  it("accepterar fortfarande de befintliga fyra graderna", () => {
    for (const grade of ["Top", "Strong", "Good", "Basic"]) {
      expect(matchGradeSchema.safeParse(grade).success).toBe(true);
    }
  });

  it("batch-schemat parsar en Related-graderad entry utan att blanka mappen", () => {
    const parsed = jobAdMatchBatchSchema.parse({
      entries: {
        "00000000-0000-0000-0000-000000000001": {
          grade: "Related",
          ssykOverlap: "Match",
          titleSimilarity: "Partial",
          regionFit: "Match",
          employmentFit: "Match",
        },
      },
    });
    expect(
      parsed.entries["00000000-0000-0000-0000-000000000001"]?.grade,
    ).toBe("Related");
  });
});

/**
 * #1547 — the grade set the watched-company links filter `/jobb` on. Two arms, and
 * neither is redundant: the literal arm alone survives a reorder of the ordinality it
 * claims to follow, and the computed arm alone survives someone rewriting the constant
 * AS that slice — an expectation computed from the code under test cannot fail for its
 * own reason. Together they fail on either mutation.
 */
describe("WATCH_MATCHING_GRADES (bevakningarnas matchningströskel, #1547)", () => {
  it("är [Good, Strong] — samma par som backendens MatchingGrades", () => {
    expect(WATCH_MATCHING_GRADES).toEqual(["Good", "Strong"]);
  });

  it("är exakt svansen av LIST_MATCH_GRADES från Good och uppåt (rank >= Good)", () => {
    expect(WATCH_MATCHING_GRADES).toEqual(
      LIST_MATCH_GRADES.slice(LIST_MATCH_GRADES.indexOf("Good")),
    );
  });

  it("bär varken Top eller Related", () => {
    // Top would 400 the list query (validatorn avvisar den); Related is the
    // includeRelated rung and ranks BELOW Good, so it is outside the threshold.
    expect(WATCH_MATCHING_GRADES).not.toContain("Top");
    expect(WATCH_MATCHING_GRADES).not.toContain("Related");
  });
});
