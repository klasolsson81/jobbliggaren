import { describe, it, expect } from "vitest";
import {
  LIST_MATCH_GRADES,
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
