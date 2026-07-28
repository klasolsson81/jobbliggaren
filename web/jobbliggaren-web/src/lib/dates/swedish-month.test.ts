import { describe, expect, it } from "vitest";
import { previousSwedishMonth, swedishMonthOf } from "./swedish-month";

describe("swedishMonthOf", () => {
  it("reads the Swedish wall clock, not the UTC instant, in summer", () => {
    // 2026-07-31T22:30:00Z is 2026-08-01 00:30 in Sweden (CEST, +02:00). UTC
    // still says July; the picker must offer August, because the backend's
    // default month resolves to August.
    expect(swedishMonthOf(new Date("2026-07-31T22:30:00Z"))).toEqual({
      year: 2026,
      month: 8,
    });
  });

  it("crosses the year, not only the month, on New Year's Eve", () => {
    // 2025-12-31T23:30:00Z is 2026-01-01 00:30 in Sweden (CET, +01:00). The
    // YEAR differs too, which is the case a month-only fix would miss.
    expect(swedishMonthOf(new Date("2025-12-31T23:30:00Z"))).toEqual({
      year: 2026,
      month: 1,
    });
  });

  it("stays in the old month one second before the Swedish boundary", () => {
    // 2025-12-31T22:59:59Z is 2025-12-31 23:59:59 Swedish — still December.
    // A hardcoded +2 h offset would wrongly report January here, and the summer
    // case above fails under a hardcoded +1 h: between them no fixed offset
    // stands.
    expect(swedishMonthOf(new Date("2025-12-31T22:59:59Z"))).toEqual({
      year: 2025,
      month: 12,
    });
  });

  it("is exact on the boundary instant itself", () => {
    // 2026-07-31T22:00:00Z is 2026-08-01 00:00:00 Swedish, exactly.
    expect(swedishMonthOf(new Date("2026-07-31T22:00:00Z"))).toEqual({
      year: 2026,
      month: 8,
    });
  });

  it("agrees with UTC mid-month, in both DST polarities", () => {
    expect(swedishMonthOf(new Date("2026-06-15T09:00:00Z"))).toEqual({
      year: 2026,
      month: 6,
    });
    expect(swedishMonthOf(new Date("2026-01-15T09:00:00Z"))).toEqual({
      year: 2026,
      month: 1,
    });
  });
});

describe("previousSwedishMonth", () => {
  it("rolls the year back at January", () => {
    expect(previousSwedishMonth({ year: 2026, month: 1 })).toEqual({
      year: 2025,
      month: 12,
    });
  });

  it("steps within a year", () => {
    expect(previousSwedishMonth({ year: 2026, month: 8 })).toEqual({
      year: 2026,
      month: 7,
    });
  });

  it("walks eleven steps back to the oldest month of a twelve-month picker", () => {
    // The exact walk buildMonthOptions does. An off-by-one lands on the wrong
    // year, which is the only way this can be wrong and still look plausible.
    let month = { year: 2026, month: 1 };
    for (let i = 0; i < 11; i++) month = previousSwedishMonth(month);

    expect(month).toEqual({ year: 2025, month: 2 });
  });
});
