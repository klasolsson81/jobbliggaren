/**
 * The Swedish civil month an instant falls in.
 *
 * The backend answers the same question behind `ISwedishCalendar.MonthOf`, and
 * the activity report's month window is now the Swedish civil month
 * (Klas-direktiv 2026-07-28, ADR 0064 Amendment). The picker on
 * `/aktivitetsrapport` builds its twelve options from the clock, so it has to
 * ask the same question — otherwise, for the first one to two hours of every
 * Swedish month, the backend resolves to a month the picker's list does not
 * contain.
 *
 * `getUTCMonth()` is the wrong answer there, and `getMonth()` is the wrong
 * answer everywhere but a Swedish host: RSC code runs on the server, whose zone
 * is UTC in the container and whatever the developer set locally. The zone is
 * named explicitly for the same reason the backend names it once, as a constant:
 * the product's home country does not vary by environment.
 */
export type SwedishMonth = { year: number; month: number };

const SWEDISH_YEAR_MONTH = new Intl.DateTimeFormat("en-CA", {
  timeZone: "Europe/Stockholm",
  year: "numeric",
  month: "2-digit",
});

export function swedishMonthOf(instant: Date): SwedishMonth {
  // en-CA gives an unambiguous YYYY-MM-DD, so this does not depend on the
  // runtime's locale the way a sv-SE or default-locale format string would.
  const [year, month] = SWEDISH_YEAR_MONTH.format(instant).split("-");
  return { year: Number(year), month: Number(month) };
}

/**
 * Steps a civil month, rolling the year. The mirror of the backend's
 * `CivilMonth.Previous()`, and here for the same reason: it is the only month
 * arithmetic that is safe, so it lives in one place rather than inline in a
 * loop.
 */
export function previousSwedishMonth({ year, month }: SwedishMonth): SwedishMonth {
  return month === 1 ? { year: year - 1, month: 12 } : { year, month: month - 1 };
}
