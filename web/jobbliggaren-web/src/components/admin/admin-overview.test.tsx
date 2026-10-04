import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  ADMIN_OVERVIEW_UNAVAILABLE,
  type AdminOverviewRegions,
  type AdminTrendDay,
} from "@/lib/admin/view-models";
import { AdminOverview } from "./admin-overview";

const TREND: ReadonlyArray<AdminTrendDay> = Array.from({ length: 90 }, (_, index) => ({
  date: new Date(Date.UTC(2026, 6, 7 + index)).toISOString().slice(0, 10),
  newAccounts: index === 80 ? 4 : index % 10 === 0 ? 1 : 0,
  logins: 5 + (index % 4),
}));

const LOADED: AdminOverviewRegions = {
  newAccounts: { kind: "loaded", data: { today: 1, yesterday: 2, last7Days: 5, last30Days: 9 } },
  totals: { kind: "loaded", data: { total: 1234, suspended: 1, pendingDeletion: 2 } },
  active: { kind: "loaded", data: { last30Days: 9, today: 4, last7Days: 7 } },
  logins: { kind: "loaded", data: { today: 8, yesterday: 7, failed: 2, locked: 0 } },
  trend: { kind: "loaded", data: TREND },
  services: {
    kind: "loaded",
    data: [
      { id: "database", label: "Databas", state: "ok", detail: "4 ms" },
      { id: "platsbanken", label: "Platsbanken", state: "warning", detail: "Senaste hämtningen misslyckades" },
    ],
  },
  server: { kind: "loaded", data: { cpu: 23, memory: 61, disk: 38 } },
  backup: {
    kind: "loaded",
    data: {
      latestAt: "2026-10-04T01:30:00Z",
      offsiteAt: "2026-10-04T01:45:00Z",
      nextAt: "2026-10-05T01:30:00Z",
      retentionDays: 30,
    },
  },
  email: {
    kind: "loaded",
    data: { sent: 57, failed: 2, noRecipient: 0, topTypes: [{ type: "login-challenge", sent: 31 }] },
  },
  attention: { kind: "loaded", data: [{ kind: "failedJobs", count: 1 }, { kind: "pendingDeletions", count: 2 }] },
  events: {
    kind: "loaded",
    data: [{ id: "e1", occurredAt: "2026-10-04T04:58:00Z", kind: "accountCreated", subject: "konto.i@example.test" }],
  },
};

const card = (name: string) => screen.getByRole("region", { name });

describe("AdminOverview with its regions loaded (ADR 0150 D1/D2)", () => {
  it("shows each count with its unit and the counts behind it", () => {
    render(<AdminOverview regions={LOADED} />);

    expect(card("Nya användare")).toHaveTextContent("1idag2 igår · 5 senaste 7 dygnen · 9 senaste 30 dygnen");
    expect(card("Användare totalt")).toHaveTextContent("1 234konton1 suspenderat · 2 under radering");
    expect(card("Aktiva användare")).toHaveTextContent("9senaste 30 dygnen4 idag · 7 senaste 7 dygnen");
    expect(card("Inloggningar")).toHaveTextContent("8idag7 igår · 2 misslyckade försök · 0 låsta konton");
  });

  it("lists the services with their state, and reads each server reading as text", () => {
    render(<AdminOverview regions={LOADED} />);

    const services = within(card("Tjänster")).getAllByRole("listitem");
    expect(services.map((item) => item.textContent)).toEqual([
      "DatabasFungerar4 ms",
      "PlatsbankenVarningSenaste hämtningen misslyckades",
    ]);
    const server = card("Server");
    expect(within(server).getByText("CPU").nextElementSibling).toHaveTextContent("23 %");
    expect(within(server).queryByText("Kommer snart")).toBeNull();
    expect(card("Backup")).toHaveTextContent("Behålls30 dygn");
  });

  it("marks a failed email in the danger colour and lists the most sent types", () => {
    render(<AdminOverview regions={LOADED} />);

    const email = card("E-post");
    expect(within(email).getByText("2 misslyckade")).toHaveClass("jp-admin-danger");
    expect(within(email).getByRole("list", { name: "Mest skickade mejltyper" })).toHaveTextContent(
      "login-challenge31",
    );
  });

  it("raises the attention edge only while something needs attention, and links each item to its page", () => {
    const { unmount } = render(<AdminOverview regions={LOADED} basePath="/admin/forhandsvisning" />);
    const attention = card("Kräver uppmärksamhet");
    expect(attention).toHaveAttribute("data-state", "raised");
    expect(within(attention).getByRole("link", { name: "1 bakgrundsjobb har misslyckats" })).toHaveAttribute(
      "href",
      "/admin/forhandsvisning/jobb",
    );
    expect(within(attention).getByRole("link", { name: "2 konton väntar på radering" })).toHaveAttribute(
      "href",
      "/admin/forhandsvisning/anvandare",
    );
    unmount();

    render(<AdminOverview regions={{ ...LOADED, attention: { kind: "empty" } }} />);
    expect(card("Kräver uppmärksamhet")).toHaveAttribute("data-state", "clear");
    expect(card("Kräver uppmärksamhet")).toHaveTextContent("Inget kräver uppmärksamhet just nu.");
  });

  it("lists recent events with a label for their kind", () => {
    render(<AdminOverview regions={LOADED} />);

    expect(within(card("Senaste händelser")).getByRole("listitem")).toHaveTextContent(
      "2026-10-04 06:58Nytt kontokonto.i@example.test",
    );
  });

  it("carries the trend in a sentence, on each series' own scale, and switches its period", async () => {
    render(<AdminOverview regions={LOADED} />);

    const trend = card("Nya användare och inloggningar");
    expect(within(trend).getByText(/de senaste 30 dygnen/)).toHaveTextContent(
      "6 nya användare och 193 inloggningar de senaste 30 dygnen. Flest nya användare på en dag: 4, den 25 september. Flest inloggningar på en dag: 8, den 8 september.",
    );
    expect(trend.querySelector("svg")).toHaveAttribute("aria-hidden", "true");

    await userEvent.click(within(trend).getByRole("radio", { name: "7 dygn" }));
    expect(within(trend).getByText(/de senaste 7 dygnen/)).toBeInTheDocument();
  });
});

describe("AdminOverview in its other states", () => {
  it("reports a failure in each card as an alert and keeps every number unknown", () => {
    const failed = Object.fromEntries(
      Object.keys(ADMIN_OVERVIEW_UNAVAILABLE).map((key) => [key, { kind: "failed" }]),
    ) as unknown as AdminOverviewRegions;
    render(<AdminOverview regions={failed} />);

    expect(screen.getAllByRole("alert").length).toBe(11);
    expect(within(card("Nya användare")).getByText("–")).toBeInTheDocument();
    expect(card("Kräver uppmärksamhet")).toHaveAttribute("data-state", "unknown");
  });

  it("reports loading in each card as a status", () => {
    const loading = Object.fromEntries(
      Object.keys(ADMIN_OVERVIEW_UNAVAILABLE).map((key) => [key, { kind: "loading" }]),
    ) as unknown as AdminOverviewRegions;
    render(<AdminOverview regions={loading} />);

    expect(screen.getAllByRole("status").every((line) => line.textContent === "Hämtar uppgifter")).toBe(true);
    expect(screen.getAllByRole("status")).toHaveLength(11);
  });
});
