import { describe, it, expect } from "vitest";
import {
  daysSince,
  formatDaysAgo,
  type RelativeTimeTranslator,
} from "./relative-time";

// Mock translator echoes the key (+ count) so the test asserts BUCKET selection,
// not the Swedish copy — the real ICU strings are covered by next-intl in
// oversikt/aggregations.test.ts. The helper is translator-agnostic by design.
const t: RelativeTimeTranslator = (key, values) =>
  values?.count != null ? `${key}:${values.count}` : key;

describe("daysSince", () => {
  it("counts whole UTC calendar days, 0 for today, negative for the future", () => {
    const now = new Date("2026-05-24T12:00:00Z");
    expect(daysSince("2026-05-24T00:00:00Z", now)).toBe(0);
    expect(daysSince("2026-05-22T00:00:00Z", now)).toBe(2);
    expect(daysSince("2026-05-26T00:00:00Z", now)).toBe(-2);
  });

  it("returns 0 for an unparseable date", () => {
    expect(daysSince("not-a-date", new Date())).toBe(0);
  });
});

describe("formatDaysAgo", () => {
  const now = new Date("2026-05-24T12:00:00Z");

  it("selects today / yesterday / daysAgo buckets", () => {
    expect(formatDaysAgo(t, "2026-05-24T00:00:00Z", now)).toBe("today");
    expect(formatDaysAgo(t, "2026-05-23T00:00:00Z", now)).toBe("yesterday");
    expect(formatDaysAgo(t, "2026-05-19T00:00:00Z", now)).toBe("daysAgo:5");
  });

  it("future dates fall back to today", () => {
    expect(formatDaysAgo(t, "2026-05-30T00:00:00Z", now)).toBe("today");
  });
});
