import { describe, expect, it } from "vitest";
import {
  lastTwelveSwedishMonths,
  previousSwedishMonth,
  swedishDateSlug,
  swedishMonthOf,
  withSelectedMonth,
} from "./swedish-calendar";

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

  it("returns numbers, never NaN, for every field it reports", () => {
    // The guard on reading formatToParts BY NAME rather than splitting the
    // formatted string on "-". A pattern change in a future ICU would make the
    // split yield Number(undefined) — NaN propagates silently into the option
    // values and renders as "NaN-NaN".
    const m = swedishMonthOf(new Date("2026-07-31T22:30:00Z"));

    expect(Number.isInteger(m.year)).toBe(true);
    expect(Number.isInteger(m.month)).toBe(true);
  });
});

describe("swedishDateSlug", () => {
  it("reads the Swedish wall clock, not the UTC instant, in summer", () => {
    // 2026-07-31T22:30:00Z is 2026-08-01 00:30 in Sweden (CEST, +02:00). The
    // reader's day has turned; UTC's has not. This is the whole defect: a notice
    // dismissed before midnight stayed dismissed into the reader's next day.
    // Kills toISOString().slice(0, 10), getUTCDate(), and a hardcoded +1 h offset
    // (which would read 23:30 on 31 July and answer "2026-07-31").
    expect(swedishDateSlug(new Date("2026-07-31T22:30:00Z"))).toBe("2026-08-01");
  });

  it("stays in the old day one second before the Swedish boundary, in winter", () => {
    // 2025-12-31T22:59:59Z is 2025-12-31 23:59:59 Swedish (CET, +01:00) — still
    // December. A hardcoded +2 h offset would read 00:59:59 on 1 January and
    // answer "2026-01-01". Together with the summer case above, NO fixed offset
    // survives: +1 h dies there, +2 h dies here. Neither test establishes that
    // alone, which is why both are here.
    expect(swedishDateSlug(new Date("2025-12-31T22:59:59Z"))).toBe("2025-12-31");
  });

  it("is exact on the boundary instant, and on the instant before it", () => {
    // For a DAY slug the boundary is the defect itself, so both sides are pinned
    // rather than one: an off-by-one in either direction (> vs >=, a <= 22:00
    // guard) moves exactly one of these two and leaves the other green.
    expect(swedishDateSlug(new Date("2026-07-31T22:00:00Z"))).toBe("2026-08-01");
    expect(swedishDateSlug(new Date("2026-07-31T21:59:59Z"))).toBe("2026-07-31");
  });

  it("crosses the year, not only the day, on New Year's Eve", () => {
    // 2025-12-31T23:30:00Z is 2026-01-01 00:30 Swedish. The YEAR differs too,
    // which a day-and-month-only fix would miss. Kills getUTCFullYear().
    expect(swedishDateSlug(new Date("2025-12-31T23:30:00Z"))).toBe("2026-01-01");
  });

  it("rolls the day in winter without touching the year", () => {
    // 2026-01-14T23:30:00Z is 2026-01-15 00:30 Swedish (CET). The New Year case
    // above cannot separate a correct implementation from one that special-cases
    // 31 December, nor from one that only shifts under CEST. This one can.
    expect(swedishDateSlug(new Date("2026-01-14T23:30:00Z"))).toBe("2026-01-15");
  });

  it("agrees with the UTC date mid-day, in both DST polarities", () => {
    // Kills an over-correction that always adds a day. These two also carry the
    // compatibility argument: for 22-23 hours of every day the new slug is
    // byte-identical to the one the old code produced, which is why no migration
    // of the stored ids is owed.
    expect(swedishDateSlug(new Date("2026-06-15T09:00:00Z"))).toBe("2026-06-15");
    expect(swedishDateSlug(new Date("2026-01-15T09:00:00Z"))).toBe("2026-01-15");
  });

  it("returns the padded YYYY-MM-DD shape, never an interpolated undefined", () => {
    // Prospective, exactly like swedishMonthOf's Number.isInteger guard above:
    // against today's ICU the exact-string tests already cover this, and it adds
    // no coverage they do not have. What it names is the failure mode a future
    // pattern change would produce — "2026-08-undefined" or an unpadded
    // "2026-8-1" — which for an identity token is worse than wrong, because a
    // stable wrong slug makes every dismissal permanent instead of daily.
    expect(swedishDateSlug(new Date("2026-07-31T22:30:00Z"))).toMatch(
      /^\d{4}-\d{2}-\d{2}$/,
    );
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
    // The exact walk lastTwelveSwedishMonths does. An off-by-one lands on the
    // wrong year, which is the only way this can be wrong and still look
    // plausible.
    let month = { year: 2026, month: 1 };
    for (let i = 0; i < 11; i++) month = previousSwedishMonth(month);

    expect(month).toEqual({ year: 2025, month: 2 });
  });
});

describe("lastTwelveSwedishMonths", () => {
  it("anchors on the SWEDISH month, not the UTC one", () => {
    // THE test of this module. 2026-07-31T22:30:00Z is 00:30 on 1 August in
    // Sweden while UTC still says July, so the newest option must be August.
    //
    // This is the case that was unmeasurable before: the anchor was read
    // ambiently inside buildMonthOptions, so reverting it to getUTCMonth()
    // survived the entire suite — backend, vitest, tsc, eslint and build.
    // `test-writer` named that surviving mutation; the instant is an argument
    // now so it can die.
    const months = lastTwelveSwedishMonths(new Date("2026-07-31T22:30:00Z"));

    expect(months).toHaveLength(12);
    expect(months[0]).toEqual({ year: 2026, month: 8 });
    expect(months[11]).toEqual({ year: 2025, month: 9 });
  });

  it("crosses the year at the New Year boundary", () => {
    // The winter twin, and the one where a UTC anchor is wrong about the YEAR.
    const months = lastTwelveSwedishMonths(new Date("2025-12-31T23:30:00Z"));

    expect(months[0]).toEqual({ year: 2026, month: 1 });
    expect(months[11]).toEqual({ year: 2025, month: 2 });
  });

  it("is contiguous, newest first, with no repeats", () => {
    const months = lastTwelveSwedishMonths(new Date("2026-03-15T09:00:00Z"));

    expect(new Set(months.map((m) => `${m.year}-${m.month}`)).size).toBe(12);
    for (let i = 1; i < months.length; i++) {
      // Indexed rather than sliced: `noUncheckedIndexedAccess` types the element
      // as possibly undefined, and the length assertion above already rules it
      // out — asserting it again per element would only hide a real hole.
      expect(months[i]).toEqual(previousSwedishMonth(months[i - 1]!));
    }
  });
});

describe("withSelectedMonth", () => {
  it("adds nothing when the selected month is already in the window", () => {
    // The regression this module exists for: while the window was UTC-anchored,
    // the CURRENT Swedish month was absent for the first 1-2 h of every month
    // and got appended as a thirteenth entry. Twelve is the invariant.
    const now = new Date("2026-07-31T22:30:00Z");
    const months = lastTwelveSwedishMonths(now);

    const result = withSelectedMonth(months, swedishMonthOf(now));

    expect(result).toHaveLength(12);
    expect(result).toBe(months);
  });

  it("adds the selected month when a deep link points outside the window", () => {
    // The fallback is load-bearing and must not be "cleaned up": a <select>
    // whose value matches no <option> renders EMPTY.
    const months = lastTwelveSwedishMonths(new Date("2026-07-31T22:30:00Z"));

    const result = withSelectedMonth(months, { year: 2020, month: 1 });

    expect(result).toHaveLength(13);
    expect(result[12]).toEqual({ year: 2020, month: 1 });
  });

  it("keeps the list newest-first after inserting", () => {
    const months = lastTwelveSwedishMonths(new Date("2026-07-31T22:30:00Z"));

    const result = withSelectedMonth(months, { year: 2026, month: 12 });

    expect(result[0]).toEqual({ year: 2026, month: 12 });
    expect(result[1]).toEqual({ year: 2026, month: 8 });
  });
});
