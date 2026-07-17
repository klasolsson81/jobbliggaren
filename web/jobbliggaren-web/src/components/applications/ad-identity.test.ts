import { describe, it, expect } from "vitest";
import { adIdentityOf } from "./ad-identity";
import type { JobAdSummaryDto } from "@/lib/dto/applications";

const base: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Systemutvecklare",
  company: "Bolaget AB",
  url: "https://example.se/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
  status: "Active",
};

// #892 (CTO R1/R5): renderingen är STRUKTURELL — status + identitets-närvaro.
// "[raderad]"-sentinelen når aldrig wiren, så ingen gren får literal-matcha den.
describe("adIdentityOf (#892)", () => {
  it("null jobAd → ingen identitet, ingen markör (manuell/enbart brev)", () => {
    expect(adIdentityOf(null)).toEqual({
      adRemoved: false,
      title: null,
      company: null,
    });
    expect(adIdentityOf(undefined)).toEqual({
      adRemoved: false,
      title: null,
      company: null,
    });
  });

  it("levande annons → identitet rakt igenom, ingen markör", () => {
    expect(adIdentityOf(base)).toEqual({
      adRemoved: false,
      title: "Systemutvecklare",
      company: "Bolaget AB",
    });
  });

  it("arkiverad annons → identitet UTAN markör (Erased-exakt, aldrig != Active)", () => {
    expect(adIdentityOf({ ...base, status: "Archived" })).toEqual({
      adRemoved: false,
      title: "Systemutvecklare",
      company: "Bolaget AB",
    });
  });

  it("raderad med bevarad snapshot-identitet → identitet + markör", () => {
    expect(
      adIdentityOf({ ...base, status: "Erased", title: "Bevarad roll" }),
    ).toEqual({
      adRemoved: true,
      title: "Bevarad roll",
      company: "Bolaget AB",
    });
  });

  it("raderad med TOM identitet (pre-#315, R5) → null-identitet + markör", () => {
    expect(
      adIdentityOf({ ...base, status: "Erased", title: "", company: "" }),
    ).toEqual({ adRemoved: true, title: null, company: null });
  });

  it("okänd/ saknad status → default-deny på markören, identiteten orörd", () => {
    expect(adIdentityOf({ ...base, status: null }).adRemoved).toBe(false);
    expect(adIdentityOf({ ...base, status: undefined }).adRemoved).toBe(false);
  });
});
