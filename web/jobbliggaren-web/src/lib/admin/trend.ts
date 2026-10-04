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
  /** The one day that reached the count; null while the count is zero or several days share it. */
  readonly date: string | null;
}

export interface AdminTrendSummary {
  readonly days: number;
  readonly newAccounts: number;
  readonly logins: number;
  readonly newAccountsPeak: AdminTrendPeak;
  readonly loginsPeak: AdminTrendPeak;
}

function peakOf(days: ReadonlyArray<AdminTrendDay>, value: (day: AdminTrendDay) => number): AdminTrendPeak {
  let count = 0;
  let date: string | null = null;
  let shared = false;
  for (const day of days) {
    const current = value(day);
    if (current > count) {
      count = current;
      date = day.date;
      shared = false;
    } else if (current === count && count > 0) {
      shared = true;
    }
  }
  return { count, date: shared ? null : date };
}

/** The totals and the busiest day of each series, for the sentence that carries the chart. */
export function summarizeTrend(days: ReadonlyArray<AdminTrendDay>): AdminTrendSummary {
  return {
    days: days.length,
    newAccounts: days.reduce((sum, day) => sum + day.newAccounts, 0),
    logins: days.reduce((sum, day) => sum + day.logins, 0),
    newAccountsPeak: peakOf(days, (day) => day.newAccounts),
    loginsPeak: peakOf(days, (day) => day.logins),
  };
}
