import { describe, it, expect } from "vitest";
import {
  savedJobAdDtoSchema,
  listSavedJobAdsResultSchema,
} from "./saved-job-ads";

const wireBase = {
  id: "11111111-1111-1111-1111-111111111111",
  jobAdId: "22222222-2222-2222-2222-222222222222",
  savedAt: "2026-05-23T15:00:00Z",
  jobAd: {
    jobAdId: "22222222-2222-2222-2222-222222222222",
    title: "Backendutvecklare",
    company: "Acme AB",
    url: "https://example.com/jobs/1",
    source: "Platsbanken" as const,
    publishedAt: "2026-05-20T08:00:00Z",
    expiresAt: "2026-06-20T08:00:00Z",
  },
};

describe("savedJobAdDtoSchema", () => {
  it("parses a complete wire payload with JobAd-summary", () => {
    const parsed = savedJobAdDtoSchema.parse(wireBase);
    expect(parsed.id).toBe(wireBase.id);
    expect(parsed.jobAdId).toBe(wireBase.jobAdId);
    expect(parsed.jobAd).not.toBeNull();
    expect(parsed.jobAd!.title).toBe("Backendutvecklare");
    expect(parsed.jobAd!.source).toBe("Platsbanken");
  });

  it("accepts null jobAd (soft-deletad annons per ADR 0048 Beslut c)", () => {
    const parsed = savedJobAdDtoSchema.parse({ ...wireBase, jobAd: null });
    expect(parsed.jobAd).toBeNull();
  });

  it("accepts nullable jobAd-publishedAt/expiresAt/url", () => {
    const parsed = savedJobAdDtoSchema.parse({
      ...wireBase,
      jobAd: {
        ...wireBase.jobAd,
        url: null,
        publishedAt: null,
        expiresAt: null,
      },
    });
    expect(parsed.jobAd!.url).toBeNull();
    expect(parsed.jobAd!.publishedAt).toBeNull();
    expect(parsed.jobAd!.expiresAt).toBeNull();
  });

  it("rejects okänd source", () => {
    expect(() =>
      savedJobAdDtoSchema.parse({
        ...wireBase,
        jobAd: { ...wireBase.jobAd, source: "Indeed" },
      })
    ).toThrow();
  });

  it("rejects missing jobAdId", () => {
    const { jobAdId: _drop, ...rest } = wireBase;
    expect(() => savedJobAdDtoSchema.parse(rest)).toThrow();
  });
});

describe("listSavedJobAdsResultSchema", () => {
  it("parses an array of saved job ads", () => {
    const parsed = listSavedJobAdsResultSchema.parse([wireBase, wireBase]);
    expect(parsed).toHaveLength(2);
  });

  it("parses an empty list", () => {
    const parsed = listSavedJobAdsResultSchema.parse([]);
    expect(parsed).toEqual([]);
  });
});
