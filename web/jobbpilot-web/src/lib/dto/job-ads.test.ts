import { describe, it, expect } from "vitest";
import {
  jobAdDtoSchema,
  jobAdFiltersSchema,
  jobAdSortBySchema,
  jobAdStatusSchema,
  jobSourceSchema,
  listJobAdsResultSchema,
} from "./job-ads";

const baseJobAd = {
  id: "11111111-1111-1111-1111-111111111111",
  title: "Senior Backend Developer",
  companyName: "Acme AB",
  description: "Vi söker en .NET-utvecklare.",
  url: "https://example.com/jobb/123",
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-05-13T08:00:00Z",
  expiresAt: "2026-06-13T08:00:00Z",
  createdAt: "2026-05-13T08:01:00Z",
};

describe("jobAdStatusSchema", () => {
  it("accepts Active, Expired, Archived", () => {
    for (const s of ["Active", "Expired", "Archived"]) {
      expect(jobAdStatusSchema.safeParse(s).success).toBe(true);
    }
  });

  it("rejects unknown status", () => {
    expect(jobAdStatusSchema.safeParse("Pending").success).toBe(false);
  });
});

describe("jobSourceSchema", () => {
  it("accepts Manual, Platsbanken, LinkedIn, Eures", () => {
    for (const s of ["Manual", "Platsbanken", "LinkedIn", "Eures"]) {
      expect(jobSourceSchema.safeParse(s).success).toBe(true);
    }
  });

  it("rejects unknown source", () => {
    expect(jobSourceSchema.safeParse("Indeed").success).toBe(false);
  });
});

describe("jobAdSortBySchema", () => {
  it("accepts the four sort-by values", () => {
    for (const v of [
      "PublishedAtDesc",
      "PublishedAtAsc",
      "ExpiresAtDesc",
      "ExpiresAtAsc",
    ]) {
      expect(jobAdSortBySchema.safeParse(v).success).toBe(true);
    }
  });

  it("rejects unknown sort-by", () => {
    expect(jobAdSortBySchema.safeParse("UpdatedAt").success).toBe(false);
  });
});

describe("jobAdDtoSchema", () => {
  it("accepts a valid job ad", () => {
    expect(jobAdDtoSchema.safeParse(baseJobAd).success).toBe(true);
  });

  it("accepts null expiresAt (annons utan slut-datum)", () => {
    expect(
      jobAdDtoSchema.safeParse({ ...baseJobAd, expiresAt: null }).success
    ).toBe(true);
  });

  it("rejects unknown status value", () => {
    expect(
      jobAdDtoSchema.safeParse({ ...baseJobAd, status: "Bogus" }).success
    ).toBe(false);
  });

  it("rejects when title missing", () => {
    const partial: Partial<typeof baseJobAd> = { ...baseJobAd };
    delete partial.title;
    expect(jobAdDtoSchema.safeParse(partial).success).toBe(false);
  });

  describe("URL scheme (defense-in-depth XSS-skydd, security-auditor F2-P10)", () => {
    it("accepts https URL", () => {
      expect(jobAdDtoSchema.safeParse(baseJobAd).success).toBe(true);
    });

    it("accepts http URL", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "http://example.com/jobb/123",
        }).success
      ).toBe(true);
    });

    it("accepts empty URL (Manual-källa kan ha tomt fält)", () => {
      expect(
        jobAdDtoSchema.safeParse({ ...baseJobAd, url: "" }).success
      ).toBe(true);
    });

    it("rejects javascript: scheme (XSS-vektor)", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "javascript:alert(document.cookie)",
        }).success
      ).toBe(false);
    });

    it("rejects data: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "data:text/html,<script>alert(1)</script>",
        }).success
      ).toBe(false);
    });

    it("rejects vbscript: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "vbscript:msgbox(1)",
        }).success
      ).toBe(false);
    });

    it("rejects file: scheme", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "file:///etc/passwd",
        }).success
      ).toBe(false);
    });

    it("is case-insensitive (rejects uppercase JAVASCRIPT:)", () => {
      expect(
        jobAdDtoSchema.safeParse({
          ...baseJobAd,
          url: "JAVASCRIPT:alert(1)",
        }).success
      ).toBe(false);
    });
  });
});

describe("listJobAdsResultSchema", () => {
  it("accepts a paged result", () => {
    const result = {
      items: [baseJobAd],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(true);
  });

  it("accepts empty items array (legitimt tomt sökresultat)", () => {
    const result = { items: [], totalCount: 0, page: 1, pageSize: 20 };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(true);
  });

  it("rejects when item shape invalid", () => {
    const result = {
      items: [{ ...baseJobAd, source: "Indeed" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
    expect(listJobAdsResultSchema.safeParse(result).success).toBe(false);
  });
});

describe("jobAdFiltersSchema", () => {
  const valid = { ssyk: "", region: "", q: "", sortBy: "PublishedAtDesc" };

  it("accepts all-empty filter (default state)", () => {
    expect(jobAdFiltersSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts JobTech-style concept-id (1-32 chars)", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, ssyk: "MVqp_eS8_kDZ" }).success
    ).toBe(true);
  });

  it("rejects ssyk with invalid characters", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, ssyk: "ssyk!hack" }).success
    ).toBe(false);
  });

  it("rejects ssyk longer than 32 chars", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, ssyk: "a".repeat(33) }).success
    ).toBe(false);
  });

  it("rejects q shorter than 2 chars (matches backend validator)", () => {
    expect(jobAdFiltersSchema.safeParse({ ...valid, q: "a" }).success).toBe(
      false
    );
  });

  it("rejects q longer than 100 chars", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, q: "a".repeat(101) }).success
    ).toBe(false);
  });

  it("accepts q at boundary (2 chars and 100 chars)", () => {
    expect(jobAdFiltersSchema.safeParse({ ...valid, q: "ab" }).success).toBe(
      true
    );
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, q: "a".repeat(100) }).success
    ).toBe(true);
  });

  it("rejects unknown sortBy", () => {
    expect(
      jobAdFiltersSchema.safeParse({ ...valid, sortBy: "Bogus" }).success
    ).toBe(false);
  });
});
