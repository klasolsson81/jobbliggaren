import { describe, expect, it } from "vitest";
import { recentSearchCaptureBody } from "./recent-search-capture-body";
import {
  buildJobbHref,
  DISTANS_ON_VALUE,
  JUNIOR_ON_VALUE,
  parseEmployerParam,
  parseQParam,
  toStringList,
  type JobbUrlState,
} from "./search-params";

/**
 * #2285 (dotnet-architect, #2285 plan read) — the list and the capture read one state through two mappers: the list
 * through the URL (`buildJobbHref`, then the page's parse), the capture through `recentSearchCaptureBody`. They must
 * agree on every axis the capture gate reads, `junior` included: a capture without it would store the broader search
 * and bump its row (#2261, senior-cto-advisor D1a).
 */
function listQueryFor(state: JobbUrlState) {
  const params = new URL(buildJobbHref(state), "https://jobbliggaren.test").searchParams;
  const one = (key: string) => params.get(key) ?? undefined;
  return {
    q: parseQParam(one("q")) ?? null,
    occupationGroup: toStringList(one("occupationGroup")),
    occupationName: toStringList(one("occupationName")),
    region: toStringList(one("region")),
    municipality: toStringList(one("municipality")),
    employmentType: toStringList(one("employmentType")),
    worktimeExtent: toStringList(one("worktimeExtent")),
    employer: [...parseEmployerParam(one("employer"))],
    remote: one("distans") === DISTANS_ON_VALUE,
    juniorIt: one("junior") === JUNIOR_ON_VALUE,
    sort: one("sortBy") ?? "PublishedAtDesc",
  };
}

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  occupationName: [],
  region: [],
  municipality: [],
  remote: false,
  junior: false,
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  employer: [],
  sortBy: "PublishedAtDesc",
};

const full: JobbUrlState = {
  q: "data ingenjör",
  occupationGroup: ["MVqp_eS8_kDZ", "Q5DF_juj_8do"],
  occupationName: ["z5AM_ayf_WcL"],
  region: ["CifL_Rzy_Mku"],
  municipality: ["PVZL_BQT_XtL"],
  remote: true,
  junior: true,
  employmentType: ["gro4_cWF_6D7"],
  worktimeExtent: ["6YE1_gAC_R2G"],
  matchGrades: ["Strong"],
  employer: ["5566010101"],
  sortBy: "Relevance",
};

describe("recentSearchCaptureBody", () => {
  it.each([
    ["every axis set, junior and distans on", full],
    ["no axis at all", empty],
    ["a q below the minimum, which the list drops", { ...empty, q: "a", municipality: ["PVZL_BQT_XtL"] }],
    ["a match sort", { ...empty, q: "backend", sortBy: "MatchDesc" as const }],
  ])("agrees with the list's query on %s", (_, state) => {
    expect(recentSearchCaptureBody(state)).toEqual(listQueryFor(state));
  });

  it("carries the search's identity and nothing of the view beside it", () => {
    const withView: JobbUrlState = { ...full, matchningOff: true, hideApplied: true };
    expect(Object.keys(recentSearchCaptureBody(withView)).sort()).toEqual([
      "employer",
      "employmentType",
      "juniorIt",
      "municipality",
      "occupationGroup",
      "occupationName",
      "q",
      "region",
      "remote",
      "sort",
      "worktimeExtent",
    ]);
  });
});
