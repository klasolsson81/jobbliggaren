import { describe, it, expect } from "vitest";
import type { CompanyWatch, WatchFilter } from "@/lib/dto/company-follows";
import { buildCompanyJobsHref } from "@/lib/job-ads/company-jobs-href";
import { summariseWatches } from "./watch-summary";

// Rows shaped as ListCompanyWatchesQueryHandler projects them: `matchingAdCount` null = the SSYK
// gate is closed (no stated occupation), and that gate is set ONCE per request — hence fixtures
// never mix null and numbers except in the test that measures the `some` discriminator itself.
function watch(overrides: Partial<CompanyWatch> = {}): CompanyWatch {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    organizationNumber: "5566524301",
    isProtectedIdentity: false,
    companyName: "Friday Väst AB",
    followedAt: "2026-07-01T10:00:00Z",
    activeAdCount: 0,
    matchingAdCount: 0,
    filter: null,
    ...overrides,
  };
}

const FILTER: WatchFilter = {
  municipalities: [],
  regions: [],
  onlyMatched: true,
  remote: false,
};

describe("summariseWatches", () => {
  it("sums active and matching ads over every watch and counts the watches", () => {
    const s = summariseWatches(
      [
        watch({ id: "a", organizationNumber: "5566524301", activeAdCount: 136, matchingAdCount: 7 }),
        watch({ id: "b", organizationNumber: "5592804784", activeAdCount: 4, matchingAdCount: 2 }),
      ],
      true,
    );
    expect(s.count).toBe(2);
    expect(s.activeAds).toBe(140);
    expect(s.matchingAds).toBe(9);
  });

  it("one unassessed watch silences the matching sum — never a partial number", () => {
    const s = summariseWatches(
      [
        watch({ id: "a", organizationNumber: "5566524301", matchingAdCount: 7 }),
        watch({ id: "b", organizationNumber: "5592804784", matchingAdCount: null }),
      ],
      true,
    );
    expect(s.matchingAds).toBeNull();
    expect(s.matchingAdsHref).toBeNull();
  });

  it("a counted zero stays a zero, and a zero carries no link", () => {
    const s = summariseWatches([watch({ activeAdCount: 0, matchingAdCount: 0 })], true);
    expect(s.matchingAds).toBe(0);
    expect(s.activeAdsHref).toBeNull();
    expect(s.matchingAdsHref).toBeNull();
    expect(s.explainMissingLinks).toBe(false);
  });

  it("links both sums to /jobb filtered to every employer when every watch is linkable", () => {
    const s = summariseWatches(
      [
        watch({ id: "a", organizationNumber: "5566524301", activeAdCount: 3, matchingAdCount: 1 }),
        watch({ id: "b", organizationNumber: "5592804784", activeAdCount: 5, matchingAdCount: 2 }),
      ],
      true,
    );
    expect(s.activeAdsHref).toBe(buildCompanyJobsHref(["5566524301", "5592804784"], "all"));
    expect(s.matchingAdsHref).toBe(
      buildCompanyJobsHref(["5566524301", "5592804784"], "matching"),
    );
    expect(s.explainMissingLinks).toBe(false);
  });

  it.each([
    ["masked sole-prop", { organizationNumber: null, isProtectedIdentity: true }],
    ["brand group (no org.nr)", { organizationNumber: null }],
    ["short org.nr", { organizationNumber: "12345" }],
  ] as const)("one %s watch removes BOTH links and owes the explanation", (_name, overrides) => {
    const s = summariseWatches(
      [
        watch({ id: "a", organizationNumber: "5566524301", activeAdCount: 3, matchingAdCount: 1 }),
        watch({ id: "b", activeAdCount: 2, matchingAdCount: 1, ...overrides }),
      ],
      true,
    );
    expect(s.activeAds).toBe(5);
    expect(s.activeAdsHref).toBeNull();
    expect(s.matchingAdsHref).toBeNull();
    expect(s.explainMissingLinks).toBe(true);
  });

  it("an unlinkable watch with no ads anywhere owes no explanation", () => {
    const s = summariseWatches(
      [watch({ organizationNumber: null, isProtectedIdentity: true, activeAdCount: 0 })],
      true,
    );
    expect(s.explainMissingLinks).toBe(false);
  });

  it("a surface that cannot link renders no hrefs and owes no explanation", () => {
    const s = summariseWatches(
      [
        watch({ id: "a", organizationNumber: "5566524301", activeAdCount: 3, matchingAdCount: 1 }),
        watch({ id: "b", organizationNumber: null, isProtectedIdentity: true, activeAdCount: 2 }),
      ],
      false,
    );
    expect(s.activeAds).toBe(5);
    expect(s.activeAdsHref).toBeNull();
    expect(s.matchingAdsHref).toBeNull();
    expect(s.explainMissingLinks).toBe(false);
  });

  it("counts the watches that carry a notification filter", () => {
    const s = summariseWatches(
      [watch({ id: "a", filter: FILTER }), watch({ id: "b" }), watch({ id: "c", filter: FILTER })],
      true,
    );
    expect(s.filteredWatches).toBe(2);
  });

  it("an empty list sums to zero and links nothing", () => {
    expect(summariseWatches([], true)).toEqual({
      count: 0,
      activeAds: 0,
      matchingAds: 0,
      filteredWatches: 0,
      activeAdsHref: null,
      matchingAdsHref: null,
      explainMissingLinks: false,
    });
  });
});
