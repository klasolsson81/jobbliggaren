import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminAccountRow } from "@/lib/admin/view-models";
import { AdminAccountsTable } from "./admin-accounts-table";

const ROWS: ReadonlyArray<AdminAccountRow> = [
  { id: "a", email: "konto.a@example.test", role: "user", status: "active", registeredAt: "2026-09-28T12:02:00Z", applicationCount: 4, deletionEarliest: null },
  { id: "b", email: "konto.b@example.test", role: "admin", status: "pendingDeletion", registeredAt: null, applicationCount: null, deletionEarliest: "2026-11-03T08:00:00Z" },
];

function bodyRows() {
  return within(screen.getByRole("table", { name: "Konton" })).getAllByRole("row").slice(1);
}

describe("AdminAccountsTable (ADR 0150 D2/D3)", () => {
  it("names each account by its address and reads last login and activity as unknown", () => {
    render(<AdminAccountsTable region={{ kind: "loaded", data: ROWS }} />);

    const [first, second] = bodyRows();
    expect(first).toHaveTextContent("konto.a@example.test");
    expect(within(first!).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "konto.a@example.test",
      "Användare",
      "Aktiv",
      "2026-09-28 14:02",
      "–",
      "–",
      "4",
    ]);
    expect(second).toHaveTextContent("Admin");
    expect(second).toHaveTextContent("Raderas tidigast 3 nov. 2026");
    expect(within(second!).getAllByRole("cell")[3]).toHaveTextContent("–");
  });

  it("opens an account through a button in its row, never through the row", async () => {
    const onOpen = vi.fn();
    render(<AdminAccountsTable region={{ kind: "loaded", data: ROWS }} onOpen={onOpen} />);

    const open = screen.getByRole("button", { name: "konto.b@example.test" });
    expect(open).toHaveAttribute("aria-haspopup", "dialog");
    await userEvent.click(open);
    expect(onOpen).toHaveBeenCalledWith("b");
  });

  it("marks the sorted column and sorts through its header button", async () => {
    const onSort = vi.fn();
    render(
      <AdminAccountsTable
        region={{ kind: "loaded", data: ROWS }}
        sort={{ key: "registeredAt", direction: "descending" }}
        onSort={onSort}
      />,
    );

    expect(screen.getByRole("columnheader", { name: "Registrerad" })).toHaveAttribute("aria-sort", "descending");
    expect(screen.getByRole("columnheader", { name: "Konto" })).not.toHaveAttribute("aria-sort");
    await userEvent.click(screen.getByRole("button", { name: "Konto" }));
    expect(onSort).toHaveBeenCalledWith("email");
  });

  it.each([
    ["unavailable", "Kommer snart"],
    ["empty", "Inga konton matchar sökningen."],
    ["failed", "Kontona kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar konton"],
  ] as const)("in the %s state shows one line and no account", (kind, line) => {
    render(<AdminAccountsTable region={{ kind }} soonId="soon" />);

    const rows = bodyRows();
    expect(rows).toHaveLength(1);
    expect(rows[0]).toHaveTextContent(line);
    expect(screen.queryByRole("button", { name: /@example\.test/ })).toBeNull();
  });

  it("disables sorting while unbuilt and points it to the Kommer snart line", () => {
    render(<AdminAccountsTable region={{ kind: "unavailable" }} soonId="soon" />);

    const sort = screen.getByRole("button", { name: "Registrerad" });
    expect(sort).toBeDisabled();
    expect(sort).toHaveAttribute("aria-describedby", "soon");
    expect(document.getElementById("soon")).toHaveTextContent("Kommer snart");
  });

  it("reports a failed load as an alert and a loading one as a status", () => {
    const { unmount } = render(<AdminAccountsTable region={{ kind: "failed" }} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Kontona kunde inte hämtas");
    unmount();
    render(<AdminAccountsTable region={{ kind: "loading" }} />);
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar konton");
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
  });
});
