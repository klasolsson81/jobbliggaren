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
 * the home for every call site that is not on the exemption list below: import it
 * rather than repeating the literal.
 *
 * That is not advice. `no-restricted-syntax` in `eslint.config.mjs` fails the
 * literal written as a value under `src/`, in pre-commit and in CI. The version
 * of this doc before #1148 enumerated the sites that remained and ordered a
 * manual re-measure of the paragraph, so removing a site falsified the doc.
 *
 * @see eslint.config.mjs — the block that subtracts ZONE **is** the exemption
 * list, and it is not restated here: a second copy is a second thing to keep
 * true. Its entries are whole files and one whole DIRECTORY, never single
 * declarations, because ESLint cannot scope an exemption to a line — so a second
 * literal anywhere in an exempt path passes.
 *
 * Test code is exempt as a class, and the reason is prospective rather than a
 * description of the tree: a test OF a module that imports this constant could
 * not catch a mutation of it if it imported the constant too. Most zone literals
 * in tests today are doing something else — pinning the zone so date assertions
 * stay stable in CI. The exemption is blanket because a path glob cannot tell an
 * oracle from a pin, and because the cheapest way to silence such a lint error,
 * importing the constant, is exactly the change that would blind the oracle.
 * Stronger still, where it is possible: assert a hard-coded expected output, as
 * `audit-log-table.test.tsx` does — that carries nothing a later tidy-up can
 * DRY away.
 *
 * The guard is one-directional. It fails an ADDED site; it cannot see an
 * exemption that is no longer needed. Removing the literal from an exempt path
 * means removing that path's entry in the same commit.
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

const SWEDISH_DATE = new Intl.DateTimeFormat("en-CA", {
  timeZone: SWEDISH_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

/**
 * The Swedish civil-calendar DATE as a canonical `YYYY-MM-DD` label — the day
 * member this module lacked. The backend mirror carries `StartOfDay` beside
 * `MonthOf`; what is restored here is that a day member EXISTS, not its
 * signature. `StartOfDay` returns an instant and this returns a label, which is
 * why it is deliberately not named `swedishDayOf`.
 *
 * `toISOString().slice(0, 10)` is the wrong answer, for the reason this module
 * already gives for `getUTCMonth()`: it is the UTC date, and RSC code runs on the
 * server, whose zone is UTC in the container. A day derived that way rolls over
 * at 01:00 (CET) / 02:00 (CEST) Swedish, so a reader who acts just after their
 * own midnight stays in the previous day for another one to two hours.
 *
 * Read the PARTS by name, never the formatted string by shape — the same reason
 * `swedishMonthOf` gives, with one consequence particular to a string return:
 * splitting "2026-08-01" on "-" rests on a CLDR pattern, and an ICU separator or
 * field-order change would interpolate `undefined` into the slug. Because that
 * would be STABLE rather than intermittent, every dismissal would become
 * permanent instead of daily. The zero-padding comes from the `2-digit` options
 * above, never from arithmetic here.
 *
 * The result is an identity token, not copy: it suffixes the notice ids the
 * overview stores in localStorage, so a notice marked read returns at the
 * READER's midnight. It carries no locale — which is why this reaches for
 * `en-CA` rather than the app formatter — and no human ever reads it.
 */
export function swedishDateSlug(instant: Date): string {
  const parts = SWEDISH_DATE.formatToParts(instant);
  const year = parts.find((p) => p.type === "year")?.value;
  const month = parts.find((p) => p.type === "month")?.value;
  const day = parts.find((p) => p.type === "day")?.value;
  return `${year}-${month}-${day}`;
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
