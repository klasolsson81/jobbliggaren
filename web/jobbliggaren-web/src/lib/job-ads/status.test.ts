import { describe, it, expect } from "vitest";
import {
  JOB_AD_STATUS_LABELS,
  JOB_AD_STATUS_BADGE_VARIANT,
  JOB_SOURCE_LABELS,
  JOB_AD_SORT_LABELS,
  getJobAdStatusLabel,
  getJobSourceLabel,
  getJobAdSortLabel,
} from "./status";

describe("JOB_AD_STATUS_LABELS", () => {
  it("has labels for Active, Expired, Archived (cross-ref backend SmartEnum)", () => {
    expect(JOB_AD_STATUS_LABELS.Active).toBe("Aktiv");
    expect(JOB_AD_STATUS_LABELS.Expired).toBe("Utgången");
    expect(JOB_AD_STATUS_LABELS.Archived).toBe("Arkiverad");
  });
});

describe("JOB_AD_STATUS_BADGE_VARIANT", () => {
  it("maps statuses to civic-utility variants (no AI-cliché colors)", () => {
    expect(JOB_AD_STATUS_BADGE_VARIANT.Active).toBe("Success");
    expect(JOB_AD_STATUS_BADGE_VARIANT.Expired).toBe("Warning");
    expect(JOB_AD_STATUS_BADGE_VARIANT.Archived).toBe("Neutral");
  });
});

describe("JOB_SOURCE_LABELS", () => {
  it("has Swedish labels for known sources", () => {
    expect(JOB_SOURCE_LABELS.Manual).toBe("Egen");
    expect(JOB_SOURCE_LABELS.Platsbanken).toBe("Platsbanken");
    expect(JOB_SOURCE_LABELS.LinkedIn).toBe("LinkedIn");
    expect(JOB_SOURCE_LABELS.Eures).toBe("EURES");
  });
});

describe("JOB_AD_SORT_LABELS", () => {
  it("has Swedish labels for the four sort options", () => {
    expect(JOB_AD_SORT_LABELS.PublishedAtDesc).toBe("Nyast först");
    expect(JOB_AD_SORT_LABELS.PublishedAtAsc).toBe("Äldst först");
    expect(JOB_AD_SORT_LABELS.ExpiresAtDesc).toBe("Stänger senare");
    expect(JOB_AD_SORT_LABELS.ExpiresAtAsc).toBe("Stänger snart");
  });
});

describe("getter helpers", () => {
  it("getJobAdStatusLabel returns label", () => {
    expect(getJobAdStatusLabel("Active")).toBe("Aktiv");
  });

  it("getJobSourceLabel returns label", () => {
    expect(getJobSourceLabel("Platsbanken")).toBe("Platsbanken");
  });

  it("getJobAdSortLabel returns label", () => {
    expect(getJobAdSortLabel("PublishedAtDesc")).toBe("Nyast först");
  });
});
