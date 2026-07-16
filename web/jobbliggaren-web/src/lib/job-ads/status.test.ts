import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  JOB_AD_STATUS_BADGE_VARIANT,
  JOB_AD_SORT_KEYS,
  jobAdStatusLabel,
  jobSourceLabel,
  jobAdSortLabel,
} from "./status";
import svJobAds from "../../../messages/sv/jobads.json";

// Real next-intl translator scoped to the `enums` namespace from the Swedish
// catalog (the source of truth). In production it comes from
// `useTranslations("jobads.enums")`.
const t = createTranslator({
  locale: "sv",
  messages: { jobads: svJobAds },
  namespace: "jobads.enums",
});

describe("jobAdStatusLabel", () => {
  it("has labels for Active, Archived (cross-ref backend SmartEnum; Expired retired by #886)", () => {
    expect(jobAdStatusLabel(t, "Active")).toBe("Aktiv");
    expect(jobAdStatusLabel(t, "Archived")).toBe("Arkiverad");
  });
});

describe("JOB_AD_STATUS_BADGE_VARIANT", () => {
  it("maps statuses to civic-utility variants (no AI-cliché colors)", () => {
    expect(JOB_AD_STATUS_BADGE_VARIANT.Active).toBe("Success");
    expect(JOB_AD_STATUS_BADGE_VARIANT.Archived).toBe("Neutral");
  });

  it("does not resurrect the retired Expired status (#886)", () => {
    // Regression lock: Expired was declared, rendered and UNREACHABLE for the
    // product's entire history. The Record<JobAdStatus, …> type already blocks
    // it at compile time; this locks the runtime shape too.
    expect("Expired" in JOB_AD_STATUS_BADGE_VARIANT).toBe(false);
  });
});

describe("jobSourceLabel", () => {
  it("has Swedish labels for known sources", () => {
    expect(jobSourceLabel(t, "Manual")).toBe("Egen");
    expect(jobSourceLabel(t, "Platsbanken")).toBe("Platsbanken");
    expect(jobSourceLabel(t, "LinkedIn")).toBe("LinkedIn");
    expect(jobSourceLabel(t, "Eures")).toBe("EURES");
  });
});

describe("jobAdSortLabel", () => {
  it("has Swedish labels for the date/relevance sort options", () => {
    expect(jobAdSortLabel(t, "PublishedAtDesc")).toBe("Nyast först");
    expect(jobAdSortLabel(t, "PublishedAtAsc")).toBe("Äldst först");
    expect(jobAdSortLabel(t, "ExpiresAtDesc")).toBe("Stänger senare");
    expect(jobAdSortLabel(t, "ExpiresAtAsc")).toBe("Stänger snart");
  });

  it("translates Relevance and the match-sort label", () => {
    expect(jobAdSortLabel(t, "Relevance")).toBe("Mest relevant");
    expect(jobAdSortLabel(t, "MatchDesc")).toBe("Sortera efter matchning");
  });

  it("JOB_AD_SORT_KEYS preserves the declaration order", () => {
    expect([...JOB_AD_SORT_KEYS]).toEqual([
      "PublishedAtDesc",
      "PublishedAtAsc",
      "ExpiresAtDesc",
      "ExpiresAtAsc",
      "Relevance",
      "MatchDesc",
    ]);
  });
});
