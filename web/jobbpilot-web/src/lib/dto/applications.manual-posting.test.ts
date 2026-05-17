import { describe, it, expect } from "vitest";
// RÖD svit (TDD). Spec: architect-design §6. Dessa exporter finns inte än
// (jobAdSummaryDtoSchema) / fältet jobAd saknas i applicationDtoSchema —
// importen/parse failar tills Zod-tillägget gjorts (steg 14 i batch-ordningen).
import {
  applicationDtoSchema,
  applicationDetailDtoSchema,
  jobAdSummaryDtoSchema,
} from "./applications";

const baseApplication = {
  id: "11111111-1111-1111-1111-111111111111",
  jobSeekerId: "22222222-2222-2222-2222-222222222222",
  jobAdId: null,
  status: "Submitted",
  createdAt: "2026-05-11T10:00:00Z",
  updatedAt: "2026-05-11T10:00:00Z",
};

const jobAdSummary = {
  jobAdId: "33333333-3333-3333-3333-333333333333",
  title: "Backend-utvecklare",
  company: "Klarna",
  url: "https://example.com/jobb",
  source: "Platsbanken",
  publishedAt: "2026-04-01T00:00:00Z",
  expiresAt: "2026-06-01T00:00:00Z",
};

describe("jobAdSummaryDtoSchema", () => {
  it("accepts a fully populated JobAd-linked summary", () => {
    expect(jobAdSummaryDtoSchema.safeParse(jobAdSummary).success).toBe(true);
  });

  it("accepts a manual posting summary (jobAdId null, publishedAt null)", () => {
    const manual = {
      jobAdId: null,
      title: "Manuell titel",
      company: "Manuellt företag",
      url: null,
      source: "Manual",
      publishedAt: null,
      expiresAt: null,
    };
    expect(jobAdSummaryDtoSchema.safeParse(manual).success).toBe(true);
  });

  it("accepts null publishedAt (J1 — manuell saknar publiceringstid)", () => {
    expect(
      jobAdSummaryDtoSchema.safeParse({ ...jobAdSummary, publishedAt: null })
        .success
    ).toBe(true);
  });

  it("rejects when title missing", () => {
    const broken: Partial<typeof jobAdSummary> = { ...jobAdSummary };
    delete broken.title;
    expect(jobAdSummaryDtoSchema.safeParse(broken).success).toBe(false);
  });
});

describe("applicationDtoSchema with nullable jobAd", () => {
  it("accepts application with a jobAd object", () => {
    expect(
      applicationDtoSchema.safeParse({
        ...baseApplication,
        jobAd: jobAdSummary,
      }).success
    ).toBe(true);
  });

  it("accepts application with jobAd: null", () => {
    expect(
      applicationDtoSchema.safeParse({ ...baseApplication, jobAd: null })
        .success
    ).toBe(true);
  });

  it("strips unknown jobAd field when absent (deploy-skew safe — z.object non-strict)", () => {
    // Deployad backend (3a) kan svara FÖRE frontend (3b). Äldre svar utan
    // jobAd får ej krascha — z.object utan .strict strippar/tolererar.
    // Bekräftar att parse lyckas (jobAd optional/strippad), ingen exception.
    const result = applicationDtoSchema.safeParse(baseApplication);
    expect(result.success).toBe(true);
  });
});

describe("applicationDetailDtoSchema with nullable jobAd", () => {
  const validDetail = {
    ...baseApplication,
    coverLetter: null,
    followUps: [],
    notes: [],
  };

  it("accepts detail with jobAd object", () => {
    expect(
      applicationDetailDtoSchema.safeParse({
        ...validDetail,
        jobAd: jobAdSummary,
      }).success
    ).toBe(true);
  });

  it("accepts detail with jobAd: null", () => {
    expect(
      applicationDetailDtoSchema.safeParse({ ...validDetail, jobAd: null })
        .success
    ).toBe(true);
  });
});
