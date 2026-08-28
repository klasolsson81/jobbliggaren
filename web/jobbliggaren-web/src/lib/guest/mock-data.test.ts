import { describe, expect, it } from "vitest";
import { daysSince } from "@/lib/i18n/relative-time";
import {
  buildGuestPipeline,
  GUEST_MOCK,
  GUEST_MOCK_REF_DATE,
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

// #1516: tidsetiketterna renderas inte längre ur mocken, de HÄRLEDS ur
// `updatedAtIso` genom produktens `formatDaysAgo`. Det flyttar felmöjligheten
// från stavning till datum, och den nya felmöjligheten är tyst: `formatDaysAgo`
// svarar `today` för allt med `days <= 0`, så ett datum efter referensen
// renderar `idag` utan att något går sönder. Dessa assertions är den enda
// grinden mot det.
describe("GUEST_MOCK relativa tider (#1516)", () => {
  const dated = [
    ...GUEST_MOCK.applications.map((a) => ({
      id: a.id,
      iso: a.updatedAtIso,
    })),
    ...GUEST_MOCK.resumes.map((r) => ({ id: r.id, iso: r.updatedAtIso })),
  ];

  it("varje updatedAtIso är ett parsebart datum", () => {
    // `daysSince` sväljer skräp genom att svara 0, så ogiltiga datum måste
    // fångas här och inte via dagsskillnaden.
    for (const { id, iso } of dated) {
      expect(Number.isNaN(new Date(iso).getTime()), id).toBe(false);
    }
  });

  it("inget datum ligger efter mockens frusna referens", () => {
    for (const { id, iso } of dated) {
      expect(daysSince(iso, GUEST_MOCK_REF_DATE), id).toBeGreaterThanOrEqual(0);
    }
  });

  it("spänner alla tre frasarmarna så demot visar idag, igår OCH N dagar sedan", () => {
    // Det är den här spännvidden som gör demot till ett demo: alla tre formerna
    // står i samma kolumn på /gast/ansokningar. Faller den blir demot enformigt
    // utan att något test annars märker det.
    const diffs = dated.map(({ iso }) => daysSince(iso, GUEST_MOCK_REF_DATE));
    expect(diffs).toContain(0);
    expect(diffs).toContain(1);
    expect(diffs.some((d) => d >= 2)).toBe(true);
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
