import { describe, it, expect } from "vitest";
import {
  buildJobbHref,
  withCommitFlag,
  COMMIT_PARAM,
  COMMIT_VALUE,
  type JobbUrlState,
} from "./search-params";

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  sortBy: "PublishedAtDesc",
};

describe("withCommitFlag (E2j commit-intent-signal)", () => {
  it("adderar ?commit=true på en href utan query", () => {
    // Värdet är "true", inte "1" — ASP.NET bool-binding tar inte "1".
    expect(withCommitFlag("/jobb")).toBe(`/jobb?${COMMIT_PARAM}=${COMMIT_VALUE}`);
    expect(COMMIT_VALUE).toBe("true");
  });

  it("adderar &commit=true på en href som redan har query", () => {
    expect(withCommitFlag("/jobb?q=volvo")).toBe(
      `/jobb?q=volvo&${COMMIT_PARAM}=${COMMIT_VALUE}`,
    );
  });

  it("commit-flaggan ingår ALDRIG i buildJobbHref (utanför JobbUrlState)", () => {
    // Invariant (CTO VAL 5 väg 2): commit är en transient signal, inte ett
    // tillstånd — buildJobbHref emitterar den aldrig.
    expect(buildJobbHref({ ...empty, q: "volvo" })).toBe("/jobb?q=volvo");
    expect(buildJobbHref(empty)).toBe("/jobb");
  });
});

describe("buildJobbHref Klass 2 (employmentType + worktimeExtent)", () => {
  it("appendar employmentType som upprepade params", () => {
    expect(
      buildJobbHref({ ...empty, employmentType: ["et1", "et2"] }),
    ).toBe("/jobb?employmentType=et1&employmentType=et2");
  });

  it("appendar worktimeExtent (radio → 0–1 element)", () => {
    expect(buildJobbHref({ ...empty, worktimeExtent: ["heltid"] })).toBe(
      "/jobb?worktimeExtent=heltid",
    );
  });

  it("ordning: dimensioner → employmentType → worktimeExtent → q", () => {
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        occupationGroup: ["og1"],
        region: ["r1"],
        employmentType: ["et1"],
        worktimeExtent: ["wt1"],
      }),
    ).toBe(
      "/jobb?occupationGroup=og1&region=r1&employmentType=et1&worktimeExtent=wt1&q=volvo",
    );
  });

  it("tomma Klass-2-arrayer ger inga params", () => {
    expect(buildJobbHref(empty)).toBe("/jobb");
  });
});

describe("buildJobbHref STEG 5 (matchGrades — grade-filter)", () => {
  it("appendar matchGrades som upprepade params (enum-namn)", () => {
    expect(
      buildJobbHref({ ...empty, matchGrades: ["Strong", "Good"] }),
    ).toBe("/jobb?matchGrades=Strong&matchGrades=Good");
  });

  it("tom matchGrades-lista ger inget param (Av = noll grader)", () => {
    expect(buildJobbHref({ ...empty, matchGrades: [] })).toBe("/jobb");
  });

  it("ordning: Klass-2-dimensioner → matchGrades → q (stabil URL-form)", () => {
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        occupationGroup: ["og1"],
        region: ["r1"],
        employmentType: ["et1"],
        worktimeExtent: ["wt1"],
        matchGrades: ["Basic", "Good", "Strong"],
      }),
    ).toBe(
      "/jobb?occupationGroup=og1&region=r1&employmentType=et1&worktimeExtent=wt1&matchGrades=Basic&matchGrades=Good&matchGrades=Strong&q=volvo",
    );
  });

  it("round-trip: buildJobbHref → URLSearchParams.getAll bevarar grad-listan", () => {
    // Wire-kontraktets round-trip: graderna överlever serialisering→parse i
    // samma form som page.tsx läser dem (params.getAll-ekvivalent).
    const href = buildJobbHref({
      ...empty,
      matchGrades: ["Strong", "Basic"],
    });
    const qs = href.slice(href.indexOf("?") + 1);
    expect(new URLSearchParams(qs).getAll("matchGrades")).toEqual([
      "Strong",
      "Basic",
    ]);
  });
});

describe("buildJobbHref issue #292 (matchning huvudbrytare)", () => {
  it("matchningOff=true emitterar ?matchning=off", () => {
    expect(buildJobbHref({ ...empty, matchningOff: true })).toBe(
      "/jobb?matchning=off",
    );
  });

  it("matchningOff=false (PÅ) emitterar INTET param (default PÅ = frånvaro)", () => {
    expect(buildJobbHref({ ...empty, matchningOff: false })).toBe("/jobb");
  });

  it("matchningOff utelämnad (undefined) emitterar INTET param", () => {
    // `empty` saknar matchningOff helt → samma som PÅ (frånvaro).
    expect(buildJobbHref(empty)).toBe("/jobb");
  });

  it("ordning: matchGrades → matchning → q (stabil URL-form)", () => {
    // matchningOff är distinkt från matchGrades (CTO-bind: ingen off-sentinel i
    // matchGrades). Båda kan samexistera i URL:en endast i PÅ-läget — i av-läget
    // tömmer toolbaren matchGrades, men buildJobbHref serialiserar oavsett.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        matchGrades: ["Strong"],
        matchningOff: true,
      }),
    ).toBe("/jobb?matchGrades=Strong&matchning=off&q=volvo");
  });
});

describe("buildJobbHref #300 PR-5 (relaterade — Visa relaterade också)", () => {
  it("includeRelated=true emitterar ?relaterade=on", () => {
    expect(buildJobbHref({ ...empty, includeRelated: true })).toBe(
      "/jobb?relaterade=on",
    );
  });

  it("includeRelated=false (AV) emitterar INTET param (default AV = frånvaro)", () => {
    expect(buildJobbHref({ ...empty, includeRelated: false })).toBe("/jobb");
  });

  it("includeRelated utelämnad (undefined) emitterar INTET param", () => {
    // `empty` saknar includeRelated helt → samma som AV (frånvaro, ren URL).
    expect(buildJobbHref(empty)).toBe("/jobb");
  });

  it("ordning: matchGrades → matchning → relaterade → q (stabil URL-form)", () => {
    // relaterade placeras intill matchnings-axelns övriga params (efter matchning,
    // före q) så delningsbara URL:er får stabil form.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        matchGrades: ["Related", "Strong"],
        matchningOff: true,
        includeRelated: true,
      }),
    ).toBe(
      "/jobb?matchGrades=Related&matchGrades=Strong&matchning=off&relaterade=on&q=volvo",
    );
  });
});

describe("buildJobbHref #383 → förenklat (Dölj ansökta)", () => {
  it("hideApplied=true emitterar ?doljAnsokta=on", () => {
    expect(buildJobbHref({ ...empty, hideApplied: true })).toBe(
      "/jobb?doljAnsokta=on",
    );
  });

  it("hideApplied falsk/utelämnad ger inget param (ren URL)", () => {
    expect(buildJobbHref(empty)).toBe("/jobb");
    expect(buildJobbHref({ ...empty, hideApplied: false })).toBe("/jobb");
  });

  it("ordning: relaterade → dölj ansökta → q (stabil URL-form)", () => {
    // "Dölj ansökta" placeras efter matchnings-axelns params, före q.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        includeRelated: true,
        hideApplied: true,
      }),
    ).toBe("/jobb?relaterade=on&doljAnsokta=on&q=volvo");
  });
});
