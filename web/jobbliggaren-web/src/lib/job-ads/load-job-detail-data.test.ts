import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { JobAdDetailDto } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import type { CompanyFollowState } from "@/lib/dto/company-follows";

/**
 * `loadJobDetailData` is the shared ad-detail loader behind the full page and
 * the intercepting modal (#596). These tests pin its DATA CONTRACT only — the
 * `ok` bundle assembly, match-gated granularity, positive-only prior count, and
 * non-`ok` passthrough. They deliberately assert NO call-count or ordering: the
 * fan-out concurrency changes when #742 parallelizes the loader, but the values
 * it returns do not, so the same suite must stay green across that change.
 */
const {
  getJobAdMock,
  isJobAdSavedMock,
  hasAppliedJobAdMock,
  getCompanyWatchStatusMock,
  getJobAdMatchDetailMock,
  getEmployerApplicationCountsMock,
  getTaxonomyTreeMock,
} = vi.hoisted(() => ({
  getJobAdMock: vi.fn(),
  isJobAdSavedMock: vi.fn(),
  hasAppliedJobAdMock: vi.fn(),
  getCompanyWatchStatusMock: vi.fn(),
  getJobAdMatchDetailMock: vi.fn(),
  getEmployerApplicationCountsMock: vi.fn(),
  getTaxonomyTreeMock: vi.fn(),
}));

// Every fetcher is server-only (Bearer + backend URL); drive each branch through a stub.
vi.mock("@/lib/api/job-ads", () => ({ getJobAd: getJobAdMock }));
vi.mock("@/lib/api/saved-job-ads", () => ({ isJobAdSaved: isJobAdSavedMock }));
vi.mock("@/lib/api/job-ad-status", () => ({ hasAppliedJobAd: hasAppliedJobAdMock }));
vi.mock("@/lib/api/company-follows", () => ({
  getCompanyWatchStatus: getCompanyWatchStatusMock,
}));
vi.mock("@/lib/api/job-ad-match", () => ({ getJobAdMatchDetail: getJobAdMatchDetailMock }));
vi.mock("@/lib/api/employer-application-counts", () => ({
  getEmployerApplicationCounts: getEmployerApplicationCountsMock,
}));
vi.mock("@/lib/api/taxonomy", () => ({ getTaxonomyTree: getTaxonomyTreeMock }));

import { loadJobDetailData } from "./load-job-detail-data";

const AD_ID = "11111111-1111-1111-1111-111111111111";

// The loader treats the ad DTO and the match opaquely (pass-through), so minimal
// typed fixtures suffice — identity is what the assertions check.
const jobAd = {
  id: AD_ID,
  title: "Systemutvecklare",
  companyName: "Acme",
  contacts: [],
} as unknown as JobAdDetailDto;
const followState: CompanyFollowState = { companyWatchId: null, followable: true };
const match = { grade: "Strong" } as unknown as JobAdMatchDetail;

/** Wire an `ok` ad with a satisfied fan-out (match left to each test). */
function primeOkAd(): void {
  getJobAdMock.mockResolvedValue({
    kind: "ok",
    data: jobAd,
  } satisfies ApiResult<JobAdDetailDto>);
  isJobAdSavedMock.mockResolvedValue(true);
  hasAppliedJobAdMock.mockResolvedValue(false);
  getCompanyWatchStatusMock.mockResolvedValue(followState);
  getEmployerApplicationCountsMock.mockResolvedValue({
    countsByJobAdId: { [AD_ID]: 2 },
  });
  // Taxonomy error ⇒ buildOrtGranularityMap(null) ⇒ {} (still defined) — enough
  // to assert the match-gating without a brittle TaxonomyTree fixture.
  getTaxonomyTreeMock.mockResolvedValue({ kind: "error" });
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("loadJobDetailData", () => {
  it("assembles the ok bundle and threads every field through", async () => {
    primeOkAd();
    getJobAdMatchDetailMock.mockResolvedValue(match);

    const result = await loadJobDetailData(AD_ID, false);

    expect(result.kind).toBe("ok");
    if (result.kind !== "ok") return;
    expect(result.jobAd).toBe(jobAd);
    expect(result.initialSaved).toBe(true);
    expect(result.initialApplied).toBe(false);
    expect(result.followState).toBe(followState);
    expect(result.match).toBe(match);
    expect(result.previousApplicationCount).toBe(2);
    // A match ⇒ the granularity map is built (a Record, never undefined).
    expect(result.ortGranularityByLabel).toBeDefined();
  });

  it("leaves ortGranularityByLabel undefined when there is no match (match-gated)", async () => {
    primeOkAd();
    getJobAdMatchDetailMock.mockResolvedValue(null);

    const result = await loadJobDetailData(AD_ID, false);

    expect(result.kind).toBe("ok");
    if (result.kind !== "ok") return;
    expect(result.match).toBeNull();
    expect(result.ortGranularityByLabel).toBeUndefined();
  });

  it("returns undefined previousApplicationCount when the employer has no prior application", async () => {
    primeOkAd();
    getJobAdMatchDetailMock.mockResolvedValue(null);
    getEmployerApplicationCountsMock.mockResolvedValue({ countsByJobAdId: {} });

    const result = await loadJobDetailData(AD_ID, false);

    expect(result.kind).toBe("ok");
    if (result.kind !== "ok") return;
    expect(result.previousApplicationCount).toBeUndefined();
  });

  it("passes a non-ok getJobAd result straight through (notFound)", async () => {
    getJobAdMock.mockResolvedValue({ kind: "notFound" } satisfies ApiResult<JobAdDetailDto>);

    const result = await loadJobDetailData(AD_ID, false);

    expect(result).toEqual({ kind: "notFound" });
  });

  it("passes a rateLimited getJobAd result through with its retry window", async () => {
    getJobAdMock.mockResolvedValue({
      kind: "rateLimited",
      retryAfterSeconds: 42,
    } satisfies ApiResult<JobAdDetailDto>);

    const result = await loadJobDetailData(AD_ID, false);

    expect(result).toEqual({ kind: "rateLimited", retryAfterSeconds: 42 });
  });

  it("threads includeRelated to the match fetcher", async () => {
    primeOkAd();
    getJobAdMatchDetailMock.mockResolvedValue(null);

    await loadJobDetailData(AD_ID, true);

    expect(getJobAdMatchDetailMock).toHaveBeenCalledWith(AD_ID, true);
  });
});
