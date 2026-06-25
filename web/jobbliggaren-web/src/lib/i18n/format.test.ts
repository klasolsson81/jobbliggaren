import { createFormatter } from "next-intl";
import { describe, expect, it } from "vitest";
import { formatDate, formatNumber } from "./format";

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
