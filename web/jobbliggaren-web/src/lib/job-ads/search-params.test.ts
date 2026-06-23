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
