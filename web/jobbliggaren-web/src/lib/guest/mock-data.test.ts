import { describe, expect, it } from "vitest";
import {
  buildGuestPipeline,
  GUEST_MOCK,
  OVERSIKT_MOCK,
  type GuestApplicationStatus,
} from "./mock-data";

describe("GUEST_MOCK", () => {
  it("summary.applicationsTotal matchar applications.length (single source of truth)", () => {
    expect(GUEST_MOCK.summary.applicationsTotal).toBe(
      GUEST_MOCK.applications.length
    );
  });

  it("summary.applicationsByStatus summerar till totalt", () => {
    const total = (Object.values(GUEST_MOCK.summary.applicationsByStatus) as number[]).reduce(
      (sum, n) => sum + n,
      0
    );
    expect(total).toBe(GUEST_MOCK.summary.applicationsTotal);
  });

  it("summary.resumesTotal matchar resumes.length", () => {
    expect(GUEST_MOCK.summary.resumesTotal).toBe(GUEST_MOCK.resumes.length);
  });

  it("har minst en ansökan i varje statusläge så pipeline-grupperna inte är tomma vid demo", () => {
    const statuses: GuestApplicationStatus[] = [
      "Draft",
      "Submitted",
      "Interview",
      "Offer",
      "Rejected",
    ];
    for (const status of statuses) {
      expect(
        GUEST_MOCK.summary.applicationsByStatus[status]
      ).toBeGreaterThanOrEqual(1);
    }
  });

  it("har en primär CV-variant", () => {
    const primaries = GUEST_MOCK.resumes.filter((r) => r.isPrimary);
    expect(primaries).toHaveLength(1);
  });

  it("har realistisk activeJobAdsTotal (mock-värde av dev-korpus-storlek)", () => {
    expect(GUEST_MOCK.activeJobAdsTotal).toBeGreaterThan(10_000);
  });
});

describe("OVERSIKT_MOCK re-export", () => {
  // code-reviewer m4 2026-05-24: skydda mot framtida refactor som tar bort
  // re-exporten — Klas-direktiv §E "synkad mockdata" kräver att guest-tree
  // konsumerar samma single-source-objekt som /oversikt.
  it("re-exporteras från guest/mock-data så konsumenter slipper dubbel-import", () => {
    expect(OVERSIKT_MOCK).toBeDefined();
    expect(OVERSIKT_MOCK.matchCountThisWeek).toBeGreaterThan(0);
    expect(OVERSIKT_MOCK.matchSegmentLabel).toBeTypeOf("string");
  });
});

describe("buildGuestPipeline()", () => {
  it("returnerar 5 grupper i statusordningen Draft→Submitted→Interview→Offer→Rejected", () => {
    const groups = buildGuestPipeline();
    expect(groups.map((g) => g.status)).toEqual([
      "Draft",
      "Submitted",
      "Interview",
      "Offer",
      "Rejected",
    ]);
  });

  it("pipeline-gruppernas summa = applications totalt (synk-disciplin per Klas §E)", () => {
    const groups = buildGuestPipeline();
    const sum = groups.reduce((acc, g) => acc + g.count, 0);
    expect(sum).toBe(GUEST_MOCK.summary.applicationsTotal);
  });

  it("varje grupps `applications` har samma status som gruppen", () => {
    const groups = buildGuestPipeline();
    for (const group of groups) {
      for (const app of group.applications) {
        expect(app.status).toBe(group.status);
      }
    }
  });
});
