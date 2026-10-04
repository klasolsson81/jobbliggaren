import { describe, expect, it } from "vitest";
import type { AdminTrendDay } from "./view-models";
import { summarizeTrend, trendWindow } from "./trend";

const day = (date: string, newAccounts: number, logins: number): AdminTrendDay => ({ date, newAccounts, logins });

const SERIES: ReadonlyArray<AdminTrendDay> = Array.from({ length: 90 }, (_, index) =>
  day(`d${String(index).padStart(2, "0")}`, index % 3, 10 + (index % 7)),
);

describe("trendWindow", () => {
  it("shows the period's tail, oldest first", () => {
    expect(trendWindow(SERIES, "d7").map((entry) => entry.date)).toEqual(
      ["d83", "d84", "d85", "d86", "d87", "d88", "d89"],
    );
    expect(trendWindow(SERIES, "d30")).toHaveLength(30);
    expect(trendWindow(SERIES, "d90")).toHaveLength(90);
  });

  it("shows what there is when the series is shorter than the period", () => {
    expect(trendWindow(SERIES.slice(0, 5), "d30")).toHaveLength(5);
  });
});

describe("summarizeTrend", () => {
  it("adds up each series and names the first day that reached its peak", () => {
    const summary = summarizeTrend([day("a", 1, 5), day("b", 3, 9), day("c", 3, 2), day("d", 0, 9)]);

    expect(summary).toEqual({
      days: 4,
      newAccounts: 7,
      logins: 25,
      newAccountsPeak: { count: 3, date: "b" },
      loginsPeak: { count: 9, date: "b" },
    });
  });

  it("gives a series of zeros no peak day", () => {
    const summary = summarizeTrend([day("a", 0, 0), day("b", 0, 0)]);

    expect(summary.newAccountsPeak).toEqual({ count: 0, date: null });
    expect(summary.loginsPeak).toEqual({ count: 0, date: null });
  });
});
