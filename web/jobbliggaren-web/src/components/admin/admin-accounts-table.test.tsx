import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminAccountRow } from "@/lib/admin/view-models";
import { AdminAccountsTable } from "./admin-accounts-table";

// The retired password registration (ADR 0142) left addresses unconfirmed; the current writer never does
// (AdminAccountsDirectoryTests.The_current_writer_creates_every_account_confirmed), and
// AdminAccountsDirectoryTests.Email_confirmed_reads_the_column pins that the directory reports the flag.
const ROWS: ReadonlyArray<AdminAccountRow> = [
  { id: "a", email: "konto.a@example.test", role: "user", status: "active", emailConfirmed: true, registeredAt: "2026-09-28T12:02:00Z", applicationCount: 4, deletionEarliest: null },
  { id: "b", email: "konto.b@example.test", role: "admin", status: "pendingDeletion", emailConfirmed: false, registeredAt: "2026-08-14T07:30:00Z", applicationCount: null, deletionEarliest: "2026-11-03" },
  { id: "c", email: "konto.c@example.test", role: "user", status: "profileMissing", emailConfirmed: true, registeredAt: null, applicationCount: null, deletionEarliest: null },
];

/** What a screen reader reads in a cell: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";

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
      UNKNOWN,
      UNKNOWN,
      "4",
    ]);
    expect(second).toHaveTextContent("Admin");
    expect(within(second!).getAllByRole("cell")[6]).toHaveTextContent(UNKNOWN);
  });

  it("shows the state as a dot and the facts that go with it as lines below", () => {
    render(<AdminAccountsTable region={{ kind: "loaded", data: ROWS }} />);

    const [first, second, third] = bodyRows();
    const status = (row: HTMLElement | undefined) => within(row!).getAllByRole("cell")[2];
    expect(status(first)).toHaveTextContent(/^Aktiv$/);
    expect(status(second)).toHaveTextContent("Under radering");
    expect(status(second)).toHaveTextContent("Slutgiltigt tidigast 2026-11-03");
    expect(status(second)?.querySelector(".jp-adminusers__date")).toHaveTextContent(/^2026-11-03$/);
    expect(status(second)).toHaveTextContent("E-post ej bekräftad");
    expect(status(third)).toHaveTextContent(/^Ofullständig$/);
    expect(within(third!).getAllByRole("cell")[3]).toHaveTextContent(UNKNOWN);
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
    ["empty", "Inga konton matchar sökningen eller filtret."],
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

  it("keeps its rows while newer ones load, and says so", () => {
    render(<AdminAccountsTable region={{ kind: "loaded", data: ROWS }} busy />);
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
    expect(bodyRows()).toHaveLength(3);
  });

  it("puts the caller's action under a failed read's sentence", () => {
    render(<AdminAccountsTable region={{ kind: "failed" }} failedAction={<button type="button">Försök igen</button>} />);

    const [row] = bodyRows();
    expect(within(row!).getByRole("alert")).toBeInTheDocument();
    expect(within(row!).getByRole("button", { name: "Försök igen" })).toBeInTheDocument();
  });

  it("words a failure the way its caller knows it", () => {
    render(<AdminAccountsTable region={{ kind: "failed" }} failedMessage="Försök igen om 6 sekunder." />);
    expect(screen.getByRole("alert")).toHaveTextContent("Försök igen om 6 sekunder.");
  });

  it("reports a failed load as an alert and a loading one as a status", () => {
    const { unmount } = render(<AdminAccountsTable region={{ kind: "failed" }} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Kontona kunde inte hämtas");
    unmount();
    render(<AdminAccountsTable region={{ kind: "loading" }} />);
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar konton");
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
  });

  it("degrades a loaded region with no rows, which listRegion never builds, to the empty line", () => {
    render(<AdminAccountsTable region={{ kind: "loaded", data: [] }} />);
    expect(bodyRows()).toHaveLength(1);
    expect(bodyRows()[0]).toHaveTextContent("Inga konton matchar sökningen eller filtret.");
  });
});
