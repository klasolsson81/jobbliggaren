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
