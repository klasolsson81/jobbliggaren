import { describe, it, expect } from "vitest";
import {
  compareApplications,
  type SortDir,
  type TableSortKey,
} from "./table-sort";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";

// Injicerad FIXED referenstidpunkt (date-flake-fri, #336 / reference_oversikt_
// test_dayofmonth_flake) — komparatorn tar `now` som parameter, aldrig
// wall-clock.
const NOW = new Date("2026-07-10T12:00:00Z");

let seq = 0;
function makeApp(overrides: Partial<ApplicationDto> = {}): ApplicationDto {
  seq += 1;
  return {
    id: `id-${seq}`,
    jobSeekerId: "seeker-1",
    jobAdId: null,
    status: "Submitted",
    createdAt: "2026-05-01T00:00:00Z",
    updatedAt: "2026-05-01T00:00:00Z",
    jobAd: null,
    ...overrides,
  };
}

function withTitle(title: string): ApplicationDto {
  return makeApp({
    jobAdId: "ad-1",
    jobAd: {
      jobAdId: "ad-1",
      title,
      company: "Företaget",
      url: null,
      source: "Manual",
      publishedAt: null,
      expiresAt: null,
    },
  });
}

function sortIds(
  rows: ApplicationDto[],
  key: TableSortKey,
  dir: SortDir,
): string[] {
  return [...rows]
    .sort((a, b) => compareApplications(a, b, key, dir, NOW))
    .map((r) => r.id);
}

describe("compareApplications — role (Roll & företag), svensk collation", () => {
  const ae = withTitle("Ärendehandläggare"); // Ä sorteras EFTER Z i svenska
  const u = withTitle("Utvecklare");
  const empty = makeApp({ jobAdId: null, jobAd: null }); // roleKey → ""

  it("sorterar med svensk collation: 'Utvecklare' före 'Ärendehandläggare' (Ä efter Z)", () => {
    // Bevisar sv-collation: i root/en-collation behandlas Ä ~ A och skulle
    // sortera FÖRE 'U' — i svenska ligger Ä sist, så U kommer först.
    expect(compareApplications(u, ae, "role", "asc", NOW)).toBeLessThan(0);
    // Dokumenterar skillnaden mot en icke-svensk collation (annars omvänd ordning).
    expect("Ärendehandläggare".localeCompare("Utvecklare", "sv")).toBeGreaterThan(0);
    expect("Ärendehandläggare".localeCompare("Utvecklare", "en")).toBeLessThan(0);
  });

  it("desc vänder role-ordningen", () => {
    expect(compareApplications(u, ae, "role", "desc", NOW)).toBeGreaterThan(0);
  });

  it("en rad utan kopplad annons (jobAd null) sorteras som tom sträng → först i stigande", () => {
    const ids = sortIds([ae, u, empty], "role", "asc");
    expect(ids[0]).toBe(empty.id);
    expect(ids).toEqual([empty.id, u.id, ae.id]);
  });
});

describe("compareApplications — status (PIPELINE_ORDER)", () => {
  const draft = makeApp({ status: "Draft" });
  const submitted = makeApp({ status: "Submitted" });
  const ghosted = makeApp({ status: "Ghosted" });

  it("följer pipeline-ordningen stigande (Draft < Submitted < ... < Ghosted)", () => {
    expect(compareApplications(draft, submitted, "status", "asc", NOW)).toBeLessThan(0);
    expect(compareApplications(submitted, ghosted, "status", "asc", NOW)).toBeLessThan(0);

    const order: ApplicationStatus[] = [
      "Ghosted",
      "Draft",
      "Interviewing",
      "Submitted",
      "Accepted",
    ];
    const rows = order.map((status) => makeApp({ status }));
    const sortedStatuses = [...rows]
      .sort((a, b) => compareApplications(a, b, "status", "asc", NOW))
      .map((r) => r.status);
    expect(sortedStatuses).toEqual([
      "Draft",
      "Submitted",
      "Interviewing",
      "Accepted",
      "Ghosted",
    ]);
  });

  it("desc vänder pipeline-ordningen", () => {
    expect(compareApplications(draft, ghosted, "status", "desc", NOW)).toBeGreaterThan(0);
  });
});

describe("compareApplications — days (I steget), null sorteras ALLTID sist", () => {
  // NOW = 2026-07-10. daysInStatus = hela UTC-dagar sedan lastStatusChangeAt.
  const oldest = makeApp({ lastStatusChangeAt: "2026-07-01T00:00:00Z" }); // 9 dgr
  const middle = makeApp({ lastStatusChangeAt: "2026-07-05T00:00:00Z" }); // 5 dgr
  const newest = makeApp({ lastStatusChangeAt: "2026-07-10T00:00:00Z" }); // 0 dgr
  const noData = makeApp({ lastStatusChangeAt: undefined }); // null → sist

  it("sorterar på faktiska dagar mot injicerat now (desc = längst väntan överst)", () => {
    expect(sortIds([newest, oldest, middle], "days", "desc")).toEqual([
      oldest.id,
      middle.id,
      newest.id,
    ]);
    expect(sortIds([newest, oldest, middle], "days", "asc")).toEqual([
      newest.id,
      middle.id,
      oldest.id,
    ]);
  });

  it("en rad utan lastStatusChangeAt sorteras SIST i BÅDA riktningar (§5 no-fabrication)", () => {
    expect(sortIds([noData, oldest, newest], "days", "desc").at(-1)).toBe(
      noData.id,
    );
    expect(sortIds([noData, oldest, newest], "days", "asc").at(-1)).toBe(
      noData.id,
    );
  });

  it("två null-dagar är inbördes lika (return 0)", () => {
    const a = makeApp({ lastStatusChangeAt: undefined });
    const b = makeApp({ lastStatusChangeAt: undefined });
    expect(compareApplications(a, b, "days", "asc", NOW)).toBe(0);
    expect(compareApplications(a, b, "days", "desc", NOW)).toBe(0);
  });
});

describe("compareApplications — renhet", () => {
  it("[...rows].sort(...) muterar inte källarrayen", () => {
    const rows = [
      makeApp({ status: "Ghosted" }),
      makeApp({ status: "Draft" }),
      makeApp({ status: "Submitted" }),
    ];
    const originalOrder = rows.map((r) => r.id);

    const sorted = [...rows].sort((a, b) =>
      compareApplications(a, b, "status", "asc", NOW),
    );

    // Kopian är sorterad ...
    expect(sorted.map((r) => r.id)).not.toEqual(originalOrder);
    // ... men källarrayen är oförändrad.
    expect(rows.map((r) => r.id)).toEqual(originalOrder);
  });
});
