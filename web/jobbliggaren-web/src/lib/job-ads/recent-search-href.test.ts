import { describe, it, expect } from "vitest";
import { buildRecentSearchHref } from "./recent-search-href";
import { DEFAULT_SORT_BY } from "./search-params";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";

function makeRecent(
  overrides: Partial<RecentJobSearchDto> = {},
): RecentJobSearchDto {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    q: "backend",
    occupationGroupList: [],
    municipalityList: [],
    regionList: [],
    employmentTypeList: [],
    worktimeExtentList: [],
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: DEFAULT_SORT_BY,
    label: queryLabel("Backend Stockholm"),
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-06-27T10:00:00Z",
    ...overrides,
  };
}

describe("buildRecentSearchHref (#294 — shared replay href)", () => {
  it("builds a /jobb URL from the search criteria (q)", () => {
    const href = buildRecentSearchHref(makeRecent());
    expect(href).toMatch(/^\/jobb\?/);
    expect(href).toContain("q=backend");
  });

  it("carries Klass 2 (employmentType + worktimeExtent) so the replay keeps the filter", () => {
    const href = buildRecentSearchHref(
      makeRecent({
        employmentTypeList: ["gro4_cWF_6D7"],
        worktimeExtentList: ["6YE1_gAC_R2G"],
      }),
    );
    expect(href).toContain("employmentType=gro4_cWF_6D7");
    expect(href).toContain("worktimeExtent=6YE1_gAC_R2G");
  });

  it("carries the distans axis so the replay reproduces what the count counted (#1407)", () => {
    const href = buildRecentSearchHref(makeRecent({ remote: true }));
    expect(href).toContain("distans=on");
  });

  it("omits distans when the captured search did not have it", () => {
    // Both polarities, because a hardcoded `remote: true` would pass the test that
    // asserts the axis IS carried.
    const href = buildRecentSearchHref(makeRecent({ remote: false }));
    expect(href).not.toContain("distans");
  });

  it("carries the employer axis so the replay reproduces what the count counted (#1471)", () => {
    // The whole list, in the one-param-per-axis form every other list axis uses: the count was
    // computed over every employer the search filtered on, so the click must be too.
    const href = buildRecentSearchHref(
      makeRecent({ employerList: ["5566010101", "5560125790"] }),
    );
    expect(href).toContain("employer=5566010101.5560125790");
  });

  it("omits employer when the captured search did not have it", () => {
    const href = buildRecentSearchHref(makeRecent({ employerList: [] }));
    expect(href).not.toContain("employer");
  });

  // The third leg of count == replay. The two backend legs are fail-closed (a JobAdFilterCriteria
  // axis with no DTO property fails ListRecentSearchesCountReplayParityTests; a RecentJobSearch
  // dimension with no DTO property fails RecentJobSearchProjectionParityTests); this is the DTO →
  // URL leg. `Record<keyof RecentJobSearchDto, …>` makes the map exhaustive at compile time, so a
  // new DTO key with no row here fails `tsc`, and every axis row is exercised with a value below.
  const REPLAY_PARAM: Record<keyof RecentJobSearchDto, string | null> = {
    id: null,
    q: "q",
    occupationGroupList: "occupationGroup",
    municipalityList: "municipality",
    regionList: "region",
    employmentTypeList: "employmentType",
    worktimeExtentList: "worktimeExtent",
    employerList: "employer",
    remote: "distans",
    occupationGroupLabels: null,
    municipalityLabels: null,
    regionLabels: null,
    sortBy: "sortBy",
    label: null,
    currentCount: null,
    newCount: null,
    lastViewedAt: null,
  };
  const AXIS_SAMPLE: Partial<RecentJobSearchDto> = {
    q: "backend",
    occupationGroupList: ["MVqp_eS8_kDZ"],
    municipalityList: ["zHxw_uJZ_NNh"],
    regionList: ["CifL_Rzy_Mku"],
    employmentTypeList: ["gro4_cWF_6D7"],
    worktimeExtentList: ["6YE1_gAC_R2G"],
    employerList: ["5566010101"],
    remote: true,
    // Non-default, since the default sort is omitted from the URL by design.
    sortBy: "ExpiresAtAsc",
  };

  it("carries every axis on the DTO into the /jobb URL — an axis with no row above fails", () => {
    const axes = (Object.keys(REPLAY_PARAM) as (keyof RecentJobSearchDto)[]).filter(
      (key) => REPLAY_PARAM[key] !== null,
    );
    // Floor: an inclusion check over an empty axis set passes green on nothing.
    expect(axes.length).toBeGreaterThan(0);

    for (const key of axes) {
      const sample = AXIS_SAMPLE[key];
      expect(sample, `AXIS_SAMPLE needs a value for ${key}`).not.toBeUndefined();
      const href = buildRecentSearchHref(
        makeRecent({ q: null, remote: false, [key]: sample } as Partial<RecentJobSearchDto>),
      );
      expect(href, `axis ${key} → ?${REPLAY_PARAM[key]}=`).toContain(`${REPLAY_PARAM[key]}=`);
    }
  });

  it("never carries a grade filter (matchGrades is runtime view-state, not a saved-search concern)", () => {
    const href = buildRecentSearchHref(makeRecent());
    expect(href).not.toContain("matchGrades");
  });
});
