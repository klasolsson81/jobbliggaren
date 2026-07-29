/**
 * Swedish civil-calendar facts. The frontend mirror of
 * `Jobbliggaren.Infrastructure/Time/SwedishCalendar.cs`, and named to match it.
 *
 * **This folder is calendar FACTS, not locale PRESENTATION.** Formatting a date
 * for a reader belongs in `lib/i18n/`, where next-intl's configuration owns the
 * zone (`src/i18n/request.ts` pins Europe/Stockholm globally). What lives here is
 * the opposite: the Swedish civil month stays Europe/Stockholm even when the
 * user's locale is `en`, which is why the parsing below is deliberately
 * locale-INDEPENDENT. Do not "fix" it by routing it through the formatter.
 *
 * The backend answers the same question behind `ISwedishCalendar.MonthOf`, and
 * the activity report's month window is the Swedish civil month (Klas-direktiv
 * 2026-07-28, ADR 0064 Amendment II). The picker on `/aktivitetsrapport` builds
 * its twelve options from the clock, so it has to ask the same question —
 * otherwise, for the first one to two hours of every Swedish month, the backend
 * resolves to a month the picker's list does not contain.
 *
 * `getUTCMonth()` is the wrong answer there, and `getMonth()` is the wrong answer
 * everywhere but a Swedish host: RSC code runs on the server, whose zone is UTC
 * in the container and whatever the developer set locally.
 */
export type SwedishMonth = { year: number; month: number };

/**
 * The product's home time zone — the counterpart to `SwedishCalendar.ZoneId`, and
 * the home for NEW call sites: import it rather than repeating the literal.
 *
 * It is not yet the only occurrence, and the doc should not claim otherwise.
 * Measured over `web/jobbliggaren-web/src/`: ten occurrences, of which this
 * declaration is one. Of the other NINE, exactly **two are production code** —
 * `admin/granskning/audit-log-table.tsx` (a raw `Intl.DateTimeFormat`, exactly
 * the case this constant exists for, swept in the follow-up PR) and
 * `src/i18n/request.ts` (the primary declaration of the global next-intl pin;
 * making the i18n configuration depend on `lib/` is a layering decision of its
 * own). The remaining seven are `.test.ts(x)` files plus the `test/render-intl`
 * harness.
 *
 * The population is spelled out because "three raw literals remain" was the first
 * phrasing here, and it was a count that was true of one population and read as a
 * claim about another — the defect class this PR spent three review rounds on.
 */
export const SWEDISH_TIME_ZONE = "Europe/Stockholm";

const SWEDISH_YEAR_MONTH = new Intl.DateTimeFormat("en-CA", {
  timeZone: SWEDISH_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
});

export function swedishMonthOf(instant: Date): SwedishMonth {
  // Read the PARTS by name, never the formatted string by shape. Splitting
  // "YYYY-MM" on "-" happens to work today, but it rests on a CLDR pattern
  // rather than on anything named: a future ICU changing the separator or the
  // field order would yield Number(undefined) → NaN, silently. formatToParts
  // cannot fail that way.
  const parts = SWEDISH_YEAR_MONTH.formatToParts(instant);
  const year = Number(parts.find((p) => p.type === "year")?.value);
  const month = Number(parts.find((p) => p.type === "month")?.value);
  return { year, month };
}

/**
 * Steps a civil month back, rolling the year. The mirror of the backend's
 * `CivilMonth.Previous()`, and here for the same reason: it is the only month
 * arithmetic that is safe, so it lives in one place rather than inline in a loop.
 *
 * It never constructs a `Date` — integer arithmetic on the label cannot drift
 * with DST, and cannot hit `setMonth`'s day OVERFLOW. Measured: 2026-03-31 minus
 * one month is **2026-03-03**, because February has no 31st and JS rolls the
 * excess FORWARD. Note the direction: .NET's `AddMonths` clamps BACK to the last
 * valid day, which is why the same arithmetic loses days on the C# side and gains
 * them here. An earlier draft of this comment said "day-clamping" and used
 * 31 January as the example — 31 January minus one month is 2025-12-31, plainly
 * December, so the sentence contradicted the very date it named. Borrowing the
 * clamping vocabulary in the mirror module would re-introduce the confusion four
 * review rounds went into removing.
 */
export function previousSwedishMonth({ year, month }: SwedishMonth): SwedishMonth {
  return month === 1 ? { year: year - 1, month: 12 } : { year, month: month - 1 };
}

/**
 * The last twelve Swedish civil months, newest first, ending with the month
 * `now` falls in.
 *
 * **The instant is an ARGUMENT, not read from the clock inside**, and that is
 * what makes the picker testable at all. It was ambient in `buildMonthOptions`
 * first, and `test-writer` measured the consequence: reverting the anchor to
 * `getUTCMonth()` survived the entire suite — backend, vitest, tsc, eslint and
 * build — because the only thing covered was the primitive underneath. It is
 * also the standard this PR argues for on the C# side, where the calculator
 * takes its instant as a parameter rather than sourcing it.
 */
export function lastTwelveSwedishMonths(now: Date): SwedishMonth[] {
  const months: SwedishMonth[] = [];
  let cursor = swedishMonthOf(now);
  for (let i = 0; i < 12; i++) {
    months.push(cursor);
    cursor = previousSwedishMonth(cursor);
  }
  return months;
}

/**
 * Guarantees the picker's value matches one of its options, by adding the
 * selected month when the rolling window does not already contain it — someone
 * arriving on `?month=2020-01` from a bookmark, say.
 *
 * A `<select>` whose `value` matches no `<option>` renders EMPTY, so this
 * fallback is load-bearing and must not be "cleaned up". What it must NOT do any
 * more is fire for the CURRENT month: while the window was anchored on UTC, the
 * selected month was absent for the first one to two hours of every Swedish
 * month and got appended as a thirteenth entry. That is the defect this module
 * exists to remove, so the deliberate case and the accidental one are separated
 * here rather than left to look alike.
 */
export function withSelectedMonth(
  months: SwedishMonth[],
  selected: SwedishMonth,
): SwedishMonth[] {
  if (months.some((m) => m.year === selected.year && m.month === selected.month)) {
    return months;
  }
  return [...months, selected].sort((a, b) => b.year - a.year || b.month - a.month);
}
