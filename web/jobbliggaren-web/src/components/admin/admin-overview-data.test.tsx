import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import { AdminOverview } from "./admin-overview";

const NOW = Date.parse(OVERVIEW_TIME);
const card = (name: string) => screen.getByRole("region", { name });

describe("admin overview observed data", () => {
  it("shows exact linked counts and source time while login and activity data stay unknown", () => {
    render(<AdminOverview observations={overviewSnapshotFixture()} now={NOW} />);
    expect(within(card("Användare totalt")).getByRole("link", { name: "12" })).toHaveAttribute("href", "/admin/anvandare");
    expect(within(card("Användare totalt")).getByRole("link", { name: "2 väntar på radering" })).toHaveAttribute("href", "/admin/anvandare?status=PendingDeletion");
    expect(card("Nya användare")).toHaveTextContent("slutförd registrering");
    expect(card("Nya användare")).toHaveTextContent("Uppgift från");
    expect(card("Aktiva användare")).toHaveTextContent("Kommer snart");
    expect(card("Inloggningar")).toHaveTextContent("Kommer snart");
    expect(card("Nya användare och inloggningar").querySelector("polyline")).toBeNull();
    expect(card("Nya användare och inloggningar")).toHaveTextContent("6 nya konton");
    expect(card("Nya användare och inloggningar")).not.toHaveTextContent("inga inloggningar");
  });
  it("keeps successful job data usable when the account source fails", () => {
    render(<AdminOverview observations={{ ...overviewSnapshotFixture(), accounts: { kind: "failed" } }} now={NOW} />);
    expect(card("Nya användare")).toHaveTextContent("Uppgifterna kunde inte hämtas");
    expect(within(card("Nya användare")).queryByText("Kommer snart")).toBeNull();
    expect(within(card("Kräver uppmärksamhet")).getByRole("link", { name: "3 bakgrundsjobb har misslyckats" })).toHaveAttribute("href", "/admin/jobb#failed-jobs");
    expect(card("Senaste händelser")).toHaveTextContent("AccountSuspendedEvent");
  });
  it("uses limited zero statements and names the unassessed email source", () => {
    const snapshot = overviewSnapshotFixture();
    if (snapshot.accounts.kind !== "loaded") throw new Error("Expected observed accounts");
    render(<AdminOverview observations={{ ...snapshot,
      accounts: { ...snapshot.accounts, data: { ...snapshot.accounts.data, counts: { ...snapshot.accounts.data.counts, pendingDeletion: 0, active: 8 } } },
      jobs: { kind: "loaded", data: { totalCount: 0 }, sampledAt: OVERVIEW_TIME, refreshFailed: false },
      audit: { kind: "empty", data: [], sampledAt: OVERVIEW_TIME, refreshFailed: false },
    }} now={NOW} />);
    expect(card("Kräver uppmärksamhet")).toHaveAttribute("data-state", "unknown");
    expect(card("Kräver uppmärksamhet")).toHaveTextContent("Inga misslyckade bakgrundsjobb.");
    expect(card("Kräver uppmärksamhet")).toHaveTextContent("E-postfel kan ännu inte bedömas.");
    expect(card("Kräver uppmärksamhet")).not.toHaveTextContent("Inget kräver uppmärksamhet");
    expect(card("Senaste händelser")).toHaveTextContent("Inga händelser än.");
  });
  it("marks retained observations immediately on failure and after five minutes", () => {
    const snapshot = overviewSnapshotFixture();
    if (snapshot.accounts.kind !== "loaded") throw new Error("Expected observed accounts");
    render(<AdminOverview observations={{ ...snapshot, accounts: { ...snapshot.accounts, refreshFailed: true } }} now={NOW + 300_001} />);
    expect(card("Användare totalt")).toHaveTextContent("Uppdateringen misslyckades");
    expect(card("Användare totalt")).toHaveTextContent("äldre än fem minuter");
    expect(within(card("Användare totalt")).getByRole("link", { name: "12" })).toBeInTheDocument();
  });
});