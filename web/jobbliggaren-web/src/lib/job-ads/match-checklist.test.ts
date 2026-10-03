import { describe, expect, it } from "vitest";
import {
  buildDimensionRows,
  buildMatchChecklist,
  buildSkillChecklist,
  overflowStart,
  type SkillChecklist,
} from "./match-checklist";
import type {
  JobAdMatchDetail,
  MatchCause,
  MatchCodedDimensionDetail,
  MatchConceptGroup,
  MatchDimensionDetail,
  MatchRegisterDimensionDetail,
  MatchSkillDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

// Fixtures follow the producers in src/Jobbliggaren.Infrastructure/Matching/MatchScorer.cs:
// ScoreSsykMembership, ScoreTitle, ScoreOrtUnion, ScoreEmploymentMembership and
// ScoreConceptCoverage, graded by MatchGradeCalculator (null unless Yrke is Match).

type Entry = [conceptId: string, label: string | null];

function register(
  verdict: MatchVerdict,
  matched: Entry[] = [],
  missing: Entry[] = [],
  cause: MatchCause | null = null,
): MatchRegisterDimensionDetail {
  const map = (entries: Entry[]) => entries.map(([conceptId, label]) => ({ conceptId, label }));
  return { verdict, matched: map(matched), missing: map(missing), cause };
}

function coded(
  verdict: MatchVerdict,
  matchedConceptIds: string[] = [],
  missingConceptIds: string[] = [],
  cause: MatchCause | null = null,
): MatchCodedDimensionDetail {
  return { verdict, matchedConceptIds, missingConceptIds, cause };
}

function title(verdict: MatchVerdict): MatchDimensionDetail {
  return { verdict, matched: [], missing: [] };
}

function group(display: string, ...members: Array<[string, string]>): MatchConceptGroup {
  return { display, members: members.map(([conceptId, memberDisplay]) => ({ conceptId, display: memberDisplay })) };
}

/** A skill row in the producer's shape: legacy displays are the members' displays. */
function skill(
  verdict: MatchVerdict,
  matched: MatchConceptGroup[] = [],
  missing: MatchConceptGroup[] = [],
): MatchSkillDimensionDetail {
  const displays = (groups: MatchConceptGroup[]) =>
    groups.flatMap((g) => g.members.map((member) => member.display));
  return {
    verdict,
    matched: displays(matched),
    missing: displays(missing),
    conceptEvidence: { matched, missing },
  };
}

const noSkills = (): Pick<
  JobAdMatchDetail,
  "skillOverlap" | "mustHaveCoverage" | "niceToHaveCoverage"
> => ({
  // ScoreConceptCoverage with no confirmed skills: NotAssessed in all three rows.
  skillOverlap: skill("NotAssessed"),
  mustHaveCoverage: skill("NotAssessed"),
  niceToHaveCoverage: skill("NotAssessed"),
});

function detail(over: Partial<JobAdMatchDetail> = {}): JobAdMatchDetail {
  return {
    grade: "Good",
    ssykOverlap: register("Match", [["DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl."]]),
    titleSimilarity: title("NotAssessed"),
    regionFit: register("Match", [["PVZL_BQT_XtL", "Göteborg"]]),
    employmentFit: coded("Match", ["kpPX_CNN_gDU"]),
    skillOverlap: skill("Vacuous"),
    mustHaveCoverage: skill("Vacuous"),
    niceToHaveCoverage: skill("Vacuous"),
    ...over,
  };
}

const options = {
  codedName: (id: string) => (id === "kpPX_CNN_gDU" ? "Vanlig anställning" : `code:${id}`),
  ortGranularityByConceptId: {
    PVZL_BQT_XtL: "municipality" as const,
    zdoY_6u5_Krt: "region" as const,
  },
};

function assessed(checklist: SkillChecklist) {
  if (checklist.state !== "assessed") throw new Error("expected an assessed checklist");
  return checklist;
}

describe("buildMatchChecklist", () => {
  it("returns the no-occupation sign when the user states no occupation (grade null, PreferenceUnstated)", () => {
    const checklist = buildMatchChecklist(
      detail({
        grade: null,
        ssykOverlap: register("NotAssessed", [], [], "PreferenceUnstated"),
        ...noSkills(),
      }),
      options,
    );
    expect(checklist).toEqual({ kind: "noStatedOccupation" });
  });

  it("marks an ad without an occupation group: no grade, no Yrke row, the reason stands alone", () => {
    const checklist = buildMatchChecklist(
      detail({ grade: null, ssykOverlap: register("NotAssessed", [], [], "AdSilent") }),
      options,
    );
    expect(checklist.kind).toBe("checklist");
    if (checklist.kind !== "checklist") return;
    expect(checklist.occupationSilent).toBe(true);
    expect(checklist.rows.map((row) => row.key)).not.toContain("ssykOverlap");
  });

  it("does not mark a stated, matching occupation as silent", () => {
    const checklist = buildMatchChecklist(detail(), options);
    expect(checklist.kind === "checklist" && checklist.occupationSilent).toBe(false);
  });
});

describe("buildDimensionRows", () => {
  it("renders the assessed dimensions in Yrke, Ort, Anställningsform order with their names", () => {
    const rows = buildDimensionRows(detail(), options);
    expect(rows).toEqual([
      { key: "ssykOverlap", tone: "match", word: "Match", unnamedCount: 0,
        value: { kind: "names", names: ["Mjukvaru- och systemutvecklare m.fl."] } },
      { key: "regionFit", tone: "match", word: "Match", unnamedCount: 0,
        value: { kind: "names", names: ["Göteborg"] } },
      { key: "employmentFit", tone: "match", word: "Match", unnamedCount: 0,
        value: { kind: "names", names: ["Vanlig anställning"] } },
    ]);
  });

  it("hides every dimension without an assessment (unstated preferences, no CV role)", () => {
    const rows = buildDimensionRows(
      detail({
        titleSimilarity: title("NotAssessed"),
        regionFit: register("NotAssessed", [], [], "PreferenceUnstated"),
        employmentFit: coded("NotAssessed", [], [], "PreferenceUnstated"),
      }),
      options,
    );
    expect(rows.map((row) => row.key)).toEqual(["ssykOverlap"]);
  });

  it("hides a county-only ad whose county holds the user's municipality (ScoreOrtUnion containment)", () => {
    const rows = buildDimensionRows(
      detail({ regionFit: register("NotAssessed", [], [], "RegionContainsPreferredMunicipality") }),
      options,
    );
    expect(rows.map((row) => row.key)).not.toContain("regionFit");
  });

  it("keeps an ad-silent Ort row as Matchar inte with the ad's reason as its value (Klas 2026-10-03)", () => {
    const [, ort] = buildDimensionRows(
      detail({ grade: "Basic", regionFit: register("NoMatch", [], [], "AdSilent") }),
      options,
    );
    expect(ort).toEqual({
      key: "regionFit", tone: "warn", word: "NoMatch", unnamedCount: 0,
      value: { kind: "cause", cause: { dimension: "regionFit", cause: "AdSilent" } },
    });
  });

  it("keeps an ad-silent Anställningsform row the same way (ScoreEmploymentMembership)", () => {
    const rows = buildDimensionRows(
      detail({ grade: "Basic", employmentFit: coded("NoMatch", [], [], "AdSilent") }),
      options,
    );
    expect(rows.at(-1)).toEqual({
      key: "employmentFit", tone: "warn", word: "NoMatch", unnamedCount: 0,
      value: { kind: "cause", cause: { dimension: "employmentFit", cause: "AdSilent" } },
    });
  });

  it("shows a remote ad's Ort as a match carrying the remote reason (RemoteOverride, empty evidence)", () => {
    const [, ort] = buildDimensionRows(
      detail({ regionFit: register("Match", [], [], "RemoteOverride") }),
      options,
    );
    expect(ort).toMatchObject({
      tone: "match",
      word: "Match",
      value: { kind: "cause", cause: { dimension: "regionFit", cause: "RemoteOverride" } },
    });
  });

  it("names the ad's own place on a mismatch, municipalities before counties before unclassified", () => {
    const [, ort] = buildDimensionRows(
      detail({
        grade: "Basic",
        regionFit: register("NoMatch", [], [
          ["zdoY_6u5_Krt", "Västra Götalands län"],
          ["UNCLASSIFIED", "Okänd ort"],
          ["PVZL_BQT_XtL", "Göteborg"],
        ]),
      }),
      options,
    );
    expect(ort).toMatchObject({
      tone: "warn",
      word: "NoMatch",
      value: { kind: "names", names: ["Göteborg", "Västra Götalands län", "Okänd ort"] },
    });
  });

  it("counts register entries the snapshot cannot name beside the named ones (#1598)", () => {
    const [yrke] = buildDimensionRows(
      detail({
        grade: null,
        ssykOverlap: register("NoMatch", [], [["kTH4_ZnA_xxx", "Lagerarbetare"], ["LOST_1", null]]),
      }),
      options,
    );
    expect(yrke).toMatchObject({ value: { kind: "names", names: ["Lagerarbetare"] }, unnamedCount: 1 });
  });

  it("makes the unnamed count the value when no entry can be named, never a word over an empty value", () => {
    const [yrke] = buildDimensionRows(
      detail({ grade: null, ssykOverlap: register("NoMatch", [], [["LOST_1", null], ["LOST_2", null]]) }),
      options,
    );
    expect(yrke).toMatchObject({ value: { kind: "unnamed", count: 2 }, unnamedCount: 0 });
  });

  it("words the Yrke row Liknande yrke in the warning tone under a Related grade (verdict Match)", () => {
    const [yrke] = buildDimensionRows(detail({ grade: "Related" }), options);
    expect(yrke).toMatchObject({ key: "ssykOverlap", tone: "warn", word: "Related" });
  });

  it.each([
    ["Match", "match"],
    ["Partial", "warn"],
    ["NoMatch", "warn"],
  ] as const)("renders a %s title (ScoreTitle with a CV role) with its summary", (verdict, tone) => {
    const rows = buildDimensionRows(detail({ titleSimilarity: title(verdict) }), options);
    expect(rows[1]).toEqual({
      key: "titleSimilarity", tone, word: verdict, unnamedCount: 0,
      value: { kind: "titleSummary", verdict },
    });
  });
});

describe("buildSkillChecklist", () => {
  it("is not assessed when the user has no confirmed skills (one predicate for all three rows)", () => {
    expect(buildSkillChecklist(detail(noSkills()))).toEqual({ state: "notAssessed" });
  });

  it("groups requirements first, marks a missing obligatory requirement apart from other missing skills", () => {
    const checklist = assessed(
      buildSkillChecklist(
        detail({
          mustHaveCoverage: skill(
            "Partial",
            [group("C, programmeringsspråk", ["o8wR_57f_jv9", "C, programmeringsspråk"])],
            [group("VHDL, programmeringsspråk", ["vhdl_1", "VHDL, programmeringsspråk"])],
          ),
          niceToHaveCoverage: skill(
            "Partial",
            [group("C++", ["cpp_esco", "C++"])],
            [group("Realtidssystem", ["rt_1", "Realtidssystem"])],
          ),
          skillOverlap: skill(
            "Partial",
            [
              group("C, programmeringsspråk", ["o8wR_57f_jv9", "C, programmeringsspråk"]),
              group("C#", ["rPUY_2rX_2yN", "C#"], ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"]),
              group("C++", ["cpp_esco", "C++"]),
            ],
            [
              group("Realtidssystem", ["rt_1", "Realtidssystem"]),
              group("VHDL, programmeringsspråk", ["vhdl_1", "VHDL, programmeringsspråk"]),
              group("Teknisk fysik", ["tf_1", "Teknisk fysik"]),
            ],
          ),
        }),
      ),
    );
    expect(checklist.groups.map((g) => [g.kind, g.chips.map((chip) => [chip.label, chip.tone])])).toEqual([
      ["must", [["C, programmeringsspråk", "met"], ["VHDL, programmeringsspråk", "missingRequired"]]],
      ["nice", [["C++", "met"], ["Realtidssystem", "missing"]]],
      ["profileMatched", [["C#", "met"]]],
      ["profileMissing", [["Teknisk fysik", "missing"]]],
    ]);
    expect(checklist.counts).toEqual({ matched: 3, missing: 3 });
    expect(checklist.requirementsNote).toBeNull();
  });

  it("drops a profile group that shares any member id with a requirement chip on the same side", () => {
    const checklist = assessed(
      buildSkillChecklist(
        detail({
          mustHaveCoverage: skill("Match", [group("C#, programmeringsspråk", ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"])]),
          niceToHaveCoverage: skill("Vacuous"),
          skillOverlap: skill("Match", [
            group("C#", ["rPUY_2rX_2yN", "C#"], ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"]),
          ]),
        }),
      ),
    );
    expect(checklist.groups.map((g) => g.kind)).toEqual(["must"]);
    expect(checklist.counts).toEqual({ matched: 1, missing: 0 });
    expect(checklist.requirementsNote).toBe("niceToHave");
  });

  it("shows the group display verbatim: no member words, taxonomy qualifiers kept (Klas 2026-10-03)", () => {
    const checklist = assessed(
      buildSkillChecklist(
        detail({
          skillOverlap: skill(
            "Partial",
            [group("C#", ["rPUY_2rX_2yN", "C#"], ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"])],
            [
              group("Swift (datorprogrammering)", ["swift_esco", "Swift (datorprogrammering)"]),
              group("Scala, programmeringsspråk", ["scala_af", "Scala, programmeringsspråk"]),
            ],
          ),
        }),
      ),
    );
    expect(checklist.groups.flatMap((g) => g.chips.map((chip) => chip.label))).toEqual([
      "C#",
      "Swift (datorprogrammering)",
      "Scala, programmeringsspråk",
    ]);
  });

  it("keeps every legacy display and drops the counter when the API predates concept identity", () => {
    // An API older than #1864/#1872 sends no conceptEvidence; the strict schema still admits it.
    const legacy = (verdict: MatchVerdict, matched: string[], missing: string[]): MatchSkillDimensionDetail =>
      ({ verdict, matched, missing });
    const checklist = assessed(
      buildSkillChecklist(
        detail({
          mustHaveCoverage: legacy("Match", ["boka"], []),
          niceToHaveCoverage: legacy("Vacuous", [], []),
          skillOverlap: legacy("Partial", ["boka", "boka"], ["datateknik"]),
        }),
      ),
    );
    expect(checklist.groups.map((g) => [g.kind, g.chips.map((chip) => chip.label)])).toEqual([
      ["must", ["boka"]],
      ["profileMatched", ["boka", "boka"]],
      ["profileMissing", ["datateknik"]],
    ]);
    expect(checklist.counts).toBeNull();
  });

  it("counts collapsed chips too", () => {
    const missing = Array.from({ length: 20 }, (_, i) => group(`Kompetens ${i}`, [`k_${i}`, `Kompetens ${i}`]));
    const checklist = assessed(
      buildSkillChecklist(detail({ skillOverlap: skill("NoMatch", [], missing) })),
    );
    expect(checklist.counts).toEqual({ matched: 0, missing: 20 });
    expect(checklist.requirementsNote).toBe("both");
  });

  it.each([
    [skill("Vacuous"), skill("Vacuous"), "both"],
    [skill("Vacuous"), skill("NoMatch", [], [group("WMS-system", ["wms_1", "WMS-system"])]), "mustHave"],
    [skill("NoMatch", [], [group("Truckkort A och B", ["truck_1", "Truckkort A och B"])]), skill("Vacuous"), "niceToHave"],
  ] as const)("names which extracted requirement partitions are empty", (mustHaveCoverage, niceToHaveCoverage, note) => {
    const checklist = assessed(
      buildSkillChecklist(
        detail({
          mustHaveCoverage,
          niceToHaveCoverage,
          skillOverlap: skill("Partial", [group("Java", ["java_1", "Java"])], [group("AWS", ["aws_1", "AWS"])]),
        }),
      ),
    );
    expect(checklist.requirementsNote).toBe(note);
  });

  it("leaves an ad without extracted skill terms with no chips and no counter", () => {
    const checklist = assessed(buildSkillChecklist(detail()));
    expect(checklist).toEqual({ state: "assessed", groups: [], counts: null, requirementsNote: "both" });
  });
});

describe("overflowStart", () => {
  it.each([
    [6, null],
    [7, null],
    [8, 6],
    [20, 6],
  ])("for %i missing profile chips starts the collapse at %s", (count, start) => {
    expect(overflowStart(count)).toBe(start);
  });
});
