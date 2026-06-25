import { createFormatter } from "next-intl";
import { describe, expect, it } from "vitest";
import { formatDate, formatNumber, formatTime } from "./format";

// Real next-intl formatters per locale (the same object useFormatter() /
// getFormatter() return). timeZone is pinned to Europe/Stockholm to match the
// global request.ts config and keep the date assertions TZ-stable in CI.
const sv = createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" });
const en = createFormatter({ locale: "en", timeZone: "Europe/Stockholm" });

// U+00A0 (non-breaking space) is the sv grouping separator (CLAUDE.md §10).
const NBSP = " ";

describe("formatNumber", () => {
  it("groups thousands with a non-breaking space in sv (CLAUDE.md §10)", () => {
    expect(formatNumber(sv, 1234)).toBe(`1${NBSP}234`);
    expect(formatNumber(sv, 45580)).toBe(`45${NBSP}580`);
    // never a regular ASCII space:
    expect(formatNumber(sv, 1234)).not.toContain(" ");
  });

  it("groups thousands with a comma in en (locale-aware, #190)", () => {
    expect(formatNumber(en, 1234)).toBe("1,234");
  });

  it("formats the same number differently per locale (locale-aware)", () => {
    expect(formatNumber(sv, 1234)).not.toBe(formatNumber(en, 1234));
  });

  it("leaves sub-thousand integers ungrouped", () => {
    expect(formatNumber(sv, 0)).toBe("0");
    expect(formatNumber(sv, 999)).toBe("999");
  });
});

describe("formatDate", () => {
  it("renders a short locale date in sv", () => {
    expect(formatDate(sv, "2026-05-18T12:00:00Z")).toBe("18 maj 2026");
  });

  it("renders a short locale date in en (#190)", () => {
    expect(formatDate(en, "2026-05-18T12:00:00Z")).toBe("May 18, 2026");
  });

  it("formats the same date differently per locale (locale-aware)", () => {
    const iso = "2026-05-18T12:00:00Z";
    expect(formatDate(sv, iso)).not.toBe(formatDate(en, iso));
  });

  it("returns null for missing input so callers can omit the row", () => {
    expect(formatDate(sv, null)).toBeNull();
    expect(formatDate(sv, undefined)).toBeNull();
    expect(formatDate(sv, "")).toBeNull();
  });

  it("returns null for an unparseable date string", () => {
    expect(formatDate(sv, "not-a-date")).toBeNull();
  });
});

describe("formatTime", () => {
  // 12:00Z in May is CEST (UTC+2) → 14:00 local in Europe/Stockholm.
  const noonZulu = new Date("2026-05-18T12:00:00Z");
  // 20:30Z → 22:30 local; crosses the 12h boundary to prove there is no AM/PM.
  const eveningZulu = new Date("2026-05-18T20:30:00Z");

  it("renders a 24h HH:MM time in sv", () => {
    expect(formatTime(sv, noonZulu)).toBe("14:00");
    expect(formatTime(sv, eveningZulu)).toBe("22:30");
  });

  it("pins the 24h clock in en too (no AM/PM — CLAUDE.md §10)", () => {
    expect(formatTime(en, eveningZulu)).toBe("22:30");
    expect(formatTime(en, eveningZulu)).not.toMatch(/[AP]M/i);
  });

  it("uses the same clock style across locales (24h is civic-stable)", () => {
    expect(formatTime(sv, eveningZulu)).toBe(formatTime(en, eveningZulu));
  });
});
