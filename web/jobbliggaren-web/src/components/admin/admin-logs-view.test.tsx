import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { AdminLogsView, type AdminLogView } from "./admin-logs-view";

function renderView(view: AdminLogView) {
  render(<AdminLogsView view={view} />);
}

const COLUMNS: Record<AdminLogView, string[]> = {
  security: ["Tid", "Händelse", "Konto", "IP (maskerad)", "Antal", "Detalj"],
  errors: ["Senast", "Nivå", "Källa", "Meddelande", "24 tim"],
  imports: [
    "Körning",
    "Typ",
    "Start",
    "Längd",
    "Hämtade",
    "Nya",
    "Uppdaterade",
    "Avslutade",
    "Status",
  ],
};

const CAPTION: Record<AdminLogView, string> = {
  security: "Säkerhetshändelser",
  errors: "Applikationsfel",
  imports: "Platsbanken-importer",
};

const CURRENT: Record<AdminLogView, string> = {
  security: "Säkerhet",
  errors: "Applikationsfel",
  imports: "Platsbanken-import",
};

describe("AdminLogsView — the three log views before #1980 (ADR 0150 D2/D8)", () => {
  it.each(Object.keys(COLUMNS) as AdminLogView[])(
    "%s: is headed Loggar, marks its own view and keeps its columns with one Kommer snart row",
    (view) => {
      renderView(view);

      expect(screen.getByRole("heading", { level: 1, name: "Loggar" })).toBeInTheDocument();

      const subnav = screen.getByRole("navigation", { name: "Loggvyer" });
      const links = within(subnav).getAllByRole("link");
      expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
        ["Säkerhet", "/admin/loggar"],
        ["Applikationsfel", "/admin/loggar/applikationsfel"],
        ["Platsbanken-import", "/admin/loggar/platsbanken-import"],
      ]);
      const current = links.filter((link) => link.getAttribute("aria-current") === "page");
      expect(current.map((link) => link.textContent)).toEqual([CURRENT[view]]);
      expect(subnav.textContent ?? "").not.toMatch(/\d/);

      const table = screen.getByRole("table", { name: CAPTION[view] });
      expect(within(table).getAllByRole("columnheader").map((th) => th.textContent)).toEqual(
        COLUMNS[view],
      );
      const bodyRows = within(table).getAllByRole("row").slice(1);
      expect(bodyRows).toHaveLength(1);
      expect(bodyRows[0]?.textContent).toBe("Kommer snart");
    },
  );
});

describe("AdminLogsView with rows (#1980's shapes)", () => {
  const counts = { security: 2, errors: 1, imports: 1 } as const;

  it("shows each view's count in its label and the security events as rows", () => {
    render(
      <AdminLogsView
        view="security"
        counts={counts}
        data={{
          view: "security",
          region: {
            kind: "loaded",
            data: [
              { id: "s1", occurredAt: "2026-10-04T05:31:00Z", kind: "loginFailed", account: "k•••@example.test", ip: "192.0.2.0", count: 3, detail: "Fel kod" },
              { id: "s2", occurredAt: "2026-10-04T04:02:00Z", kind: "rateLimited", account: "m•••@example.test", ip: "198.51.100.0", count: 12, detail: "För många kodförsök" },
            ],
          },
        }}
      />,
    );

    const links = within(screen.getByRole("navigation", { name: "Loggvyer" })).getAllByRole("link");
    expect(links.map((link) => link.textContent)).toEqual([
      "Säkerhet (2)",
      "Applikationsfel (1)",
      "Platsbanken-import (1)",
    ]);
    const rows = within(screen.getByRole("table", { name: "Säkerhetshändelser" })).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell").map((cell) => cell.textContent))).toEqual([
      ["2026-10-04 07:31", "Misslyckad inloggning", "k•••@example.test", "192.0.2.0", "3", "Fel kod"],
      ["2026-10-04 06:02", "Begränsning", "m•••@example.test", "198.51.100.0", "12", "För många kodförsök"],
    ]);
  });

  it("shows an error row with its level and source, and an import row with its figures", () => {
    const { unmount } = render(
      <AdminLogsView
        view="errors"
        data={{
          view: "errors",
          region: {
            kind: "loaded",
            data: [
              { id: "e1", lastSeenAt: "2026-10-04T00:04:00Z", level: "error", source: "SyncPlatsbankenSnapshotWorker", message: "Platsbanken svarade inte inom tidsgränsen.", count24h: 3 },
            ],
          },
        }}
      />,
    );
    expect(within(screen.getByRole("table", { name: "Applikationsfel" })).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "2026-10-04 02:04",
      "Fel",
      "SyncPlatsbankenSnapshotWorker",
      "Platsbanken svarade inte inom tidsgränsen.",
      "3",
    ]);
    unmount();

    render(
      <AdminLogsView
        view="imports"
        data={{
          view: "imports",
          region: {
            kind: "loaded",
            data: [
              { id: "i1", run: "#18201", kind: "full", startedAt: "2026-10-03T00:00:00Z", durationSeconds: 1212, fetched: 46120, added: 512, updated: 45390, closed: 218, status: "succeeded" },
            ],
          },
        }}
      />,
    );
    expect(within(screen.getByRole("table", { name: "Platsbanken-importer" })).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "#18201",
      "Full",
      "2026-10-03 02:00",
      "20 min 12 s",
      "46 120",
      "512",
      "45 390",
      "218",
      "Klar",
    ]);
  });

  it.each([
    ["empty", "Inga säkerhetshändelser under perioden."],
    ["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar uppgifter"],
  ] as const)("in the %s state holds one line across the columns", (kind, line) => {
    render(<AdminLogsView view="security" data={{ view: "security", region: { kind } }} />);

    const rows = within(screen.getByRole("table", { name: "Säkerhetshändelser" })).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(1);
    expect(rows[0]).toHaveTextContent(line);
  });
});
