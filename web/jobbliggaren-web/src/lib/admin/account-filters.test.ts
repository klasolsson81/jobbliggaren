import { describe, expect, it } from "vitest";
import { parseAccountFilters, parseRegistrationBounds } from "./account-filters";
import { accountsHref, retainOverview } from "./overview";
import { overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";

describe("overview drilldown and observations", () => {
  it("round-trips the backend's exact half-open bounds and status through the URL", () => {
    const snapshot = overviewSnapshotFixture();
    if (snapshot.accounts.kind !== "loaded") throw new Error("Expected account observation");
    const period = snapshot.accounts.data.newAccounts.last7Days;
    const link = new URL(accountsHref("/admin", period, "Suspended"), "https://example.test");
    expect(parseAccountFilters(Object.fromEntries(link.searchParams))).toEqual({ status: "Suspended", registeredFrom: period.from, registeredBefore: period.before });
    expect(link.search).not.toMatch(/address|userId|email/);
  });
  it.each([
    { registeredFrom: OVERVIEW_TIME },
    { registeredFrom: "invalid", registeredBefore: OVERVIEW_TIME },
    { registeredFrom: OVERVIEW_TIME, registeredBefore: "2026-10-07T10:00:00Z" },
    { status: "unknown" },
    { status: ["Active", "Suspended"] },
  ])("refuses malformed filters %j", (input) => { expect(parseAccountFilters(input)).toBeNull(); });
  it("admits today's empty interval at exact Swedish midnight", () => {
    expect(parseRegistrationBounds(OVERVIEW_TIME, OVERVIEW_TIME)).toEqual({ registeredFrom: OVERVIEW_TIME, registeredBefore: OVERVIEW_TIME });
  });
  it("retains last good data and its original observation time only for ordinary source failure", () => {
    const previous = overviewSnapshotFixture();
    const refreshed = retainOverview(previous, { ...previous, accounts: { kind: "failed" } });
    expect(refreshed.accounts).toEqual({ ...previous.accounts, refreshFailed: true });
    expect(refreshed.jobs).toEqual(previous.jobs);
  });
});