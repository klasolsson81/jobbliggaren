import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { hostFixture, overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import type { HostObservationDto } from "@/lib/dto/admin-host";
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

  describe("the Server card from the host observation (#1982)", () => {
    const server = () => card("Server");
    const withHost = (patch: Partial<HostObservationDto>, extra: { refreshFailed?: boolean; sampledAt?: string } = {}) => {
      const snapshot = overviewSnapshotFixture();
      if (snapshot.host.kind !== "loaded") throw new Error("Expected an observed host");
      return { ...snapshot, host: { ...snapshot.host, ...extra, data: { ...snapshot.host.data, ...patch } } };
    };

    it("shows the three readings with the time the host sampled them, not the time of the read", () => {
      render(<AdminOverview observations={overviewSnapshotFixture()} now={NOW} />);

      expect(server()).toHaveTextContent("CPU5,0 %Snitt över 30 s");
      expect(server()).toHaveTextContent("Minne29,4 %2,3 av 7,8 GiB");
      expect(server()).toHaveTextContent("Disk6,1 %225,6 GiB ledigt av 240,4 GiB");
      expect(server()).toHaveTextContent("Mätt 2026-10-08 11:59");
      expect(server().querySelector("time")).toHaveAttribute("datetime", "2026-10-08T09:59:30Z");
      expect(server()).not.toHaveTextContent("Kommer snart");
      expect(server()).not.toHaveTextContent("äldre än");
    });

    it("shows the first CPU window as being measured while the other readings stand", () => {
      render(<AdminOverview observations={withHost({ cpu: { state: "Collecting", sampledAt: null, value: null } })} now={NOW} />);

      expect(server()).toHaveTextContent("CPU–Uppgift saknasMäter…");
      expect(server()).toHaveTextContent("Minne29,4 %");
      expect(server()).toHaveTextContent("Disk6,1 %");
    });

    it("keeps the last reading when a refresh failed, marks it at once, and ages it by the API's own limit", () => {
      render(<AdminOverview observations={withHost({}, { refreshFailed: true })} now={NOW + 150_000} />);

      expect(server()).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
      expect(server()).toHaveTextContent("Mätningen är äldre än 2 min.");
      expect(server()).toHaveTextContent("CPU5,0 %");
    });

    it("does not call a reading old before the API's limit of two minutes", () => {
      render(<AdminOverview observations={overviewSnapshotFixture()} now={Date.parse("2026-10-08T09:59:30Z") + 120_000} />);

      expect(server()).not.toHaveTextContent("äldre än");
    });

    it("says a failed source failed without a number, and a source still loading as loading", () => {
      const { unmount } = render(<AdminOverview observations={{ ...overviewSnapshotFixture(), host: { kind: "failed" } }} now={NOW} />);
      expect(server()).toHaveTextContent("Uppgifterna kunde inte hämtas");
      expect(within(server()).queryByText(/\d,\d %/)).toBeNull();
      expect(within(server()).getAllByText("–")).toHaveLength(3);
      unmount();

      render(<AdminOverview observations={{ ...overviewSnapshotFixture(), host: { kind: "loading" } }} now={0} />);
      expect(server()).toHaveTextContent("Hämtar uppgifter");
    });

    it("leaves every other card as it was", () => {
      render(<AdminOverview observations={{ ...overviewSnapshotFixture(), host: { kind: "failed" } }} now={NOW} />);

      expect(within(card("Användare totalt")).getByRole("link", { name: "12" })).toBeInTheDocument();
      expect(card("Backup")).toHaveTextContent("2026-10-08 02:19");
    });

    it("hands the card only what the fixture's host carries", () => {
      expect(Object.keys(hostFixture()).sort()).toEqual(["cpu", "disk", "memory", "readAt", "staleAfterSeconds"]);
    });
  });
});
