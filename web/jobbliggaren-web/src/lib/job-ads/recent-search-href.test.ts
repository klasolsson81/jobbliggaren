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

  it("never carries a grade filter (matchGrades is runtime view-state, not a saved-search concern)", () => {
    const href = buildRecentSearchHref(makeRecent());
    expect(href).not.toContain("matchGrades");
  });
});
