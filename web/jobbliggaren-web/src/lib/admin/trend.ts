import type { AdminTrendDay } from "./view-models";

export type AdminTrendPeriod = "d7" | "d30" | "d90";

export const ADMIN_TREND_PERIODS: ReadonlyArray<AdminTrendPeriod> = ["d7", "d30", "d90"];

const DAYS: Readonly<Record<AdminTrendPeriod, number>> = { d7: 7, d30: 30, d90: 90 };

/** The period's tail of the series, oldest first. */
export function trendWindow(
  days: ReadonlyArray<AdminTrendDay>,
  period: AdminTrendPeriod,
): ReadonlyArray<AdminTrendDay> {
  return days.slice(-DAYS[period]);
}

export interface AdminTrendPeak {
  readonly count: number;
  /** The first day that reached the count; null while the count is zero. */
  readonly date: string | null;
}

export interface AdminTrendSummary {
  readonly days: number;
  readonly newAccounts: number;
  readonly logins: number;
  readonly newAccountsPeak: AdminTrendPeak;
  readonly loginsPeak: AdminTrendPeak;
}

/** The totals and the busiest day of each series, for the sentence that carries the chart. */
export function summarizeTrend(days: ReadonlyArray<AdminTrendDay>): AdminTrendSummary {
  let newAccounts = 0;
  let logins = 0;
  let newAccountsPeak: AdminTrendPeak = { count: 0, date: null };
  let loginsPeak: AdminTrendPeak = { count: 0, date: null };
  for (const day of days) {
    newAccounts += day.newAccounts;
    logins += day.logins;
    if (day.newAccounts > newAccountsPeak.count) newAccountsPeak = { count: day.newAccounts, date: day.date };
    if (day.logins > loginsPeak.count) loginsPeak = { count: day.logins, date: day.date };
  }
  return { days: days.length, newAccounts, logins, newAccountsPeak, loginsPeak };
}
