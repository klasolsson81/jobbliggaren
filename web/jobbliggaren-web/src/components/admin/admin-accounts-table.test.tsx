import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ACCOUNT_ACTIVITY_RECORDED_FROM } from "@/lib/admin/account-activity";
import type { AdminAccountRow } from "@/lib/admin/view-models";
import { AdminAccountsTable } from "./admin-accounts-table";

// The retired password registration (ADR 0142) left addresses unconfirmed; the current writer never does
// (AdminAccountsDirectoryTests.The_current_writer_creates_every_account_confirmed), and
// AdminAccountsDirectoryTests.Email_confirmed_reads_the_column pins that the directory reports the flag.
// The activity minutes take the shapes AdminAccountsActivityTests reads from the writers: both after a login, the
// active minute alone under a session older than the release, neither where nothing ran.
const ROWS: ReadonlyArray<AdminAccountRow> = [
  { id: "a", email: "konto.a@example.test", role: "user", status: "active", emailConfirmed: true, isSuspended: false, registeredAt: "2026-09-28T12:02:00Z", applicationCount: 4, deletionEarliest: null, lastLoginAt: "2026-10-17T19:05:00Z", lastActiveAt: "2026-10-17T19:40:00Z" },
  { id: "b", email: "konto.b@example.test", role: "admin", status: "pendingDeletion", emailConfirmed: false, isSuspended: false, registeredAt: "2026-08-14T07:30:00Z", applicationCount: null, deletionEarliest: "2026-11-03", lastLoginAt: "2026-10-17T18:52:00Z", lastActiveAt: "2026-10-17T19:07:00Z" },
  { id: "c", email: "konto.c@example.test", role: "user", status: "profileMissing", emailConfirmed: true, isSuspended: false, registeredAt: null, applicationCount: null, deletionEarliest: null, lastLoginAt: null, lastActiveAt: null },
  { id: "d", email: "konto.d@example.test", role: "user", status: "active", emailConfirmed: true, isSuspended: false, registeredAt: "2026-09-02T08:00:00Z", applicationCount: 1, deletionEarliest: null, lastLoginAt: null, lastActiveAt: "2026-10-17T20:15:00Z" },
];

/** The box's start of recording, as the live page passes it (ADR 0172 D1). */
const START = ACCOUNT_ACTIVITY_RECORDED_FROM;

/** What a screen reader reads in a cell: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";

function bodyRows() {
  return within(screen.getByRole("table", { name: "Konton" })).getAllByRole("row").slice(1);
}

function cells(row: HTMLElement | undefined) {
  return within(row!).getAllByRole("cell").map((cell) => cell.textContent);
}

describe("AdminAccountsTable (ADR 0150 D2/D3)", () => {
  it("names each account by its address and shows its last login and last active minute", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} />);

    const [first, second] = bodyRows();
    expect(first).toHaveTextContent("konto.a@example.test");
    expect(cells(first)).toEqual([
      "konto.a@example.test",
      "Användare",
      "Aktiv",
      "2026-09-28 14:02",
      "2026-10-17 21:05",
      "2026-10-17 21:40",
      "4",
    ]);
    expect(second).toHaveTextContent("Admin");
    expect(within(second!).getAllByRole("cell")[6]).toHaveTextContent(UNKNOWN);
  });

  it("shows the minutes a pending deletion kept, and the unknown state for each one nothing recorded", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} />);

    const [, pending, missing, older] = bodyRows();
    expect(cells(pending).slice(4, 6)).toEqual(["2026-10-17 20:52", "2026-10-17 21:07"]);
    expect(cells(missing).slice(4, 6)).toEqual([UNKNOWN, UNKNOWN]);
    expect(cells(older).slice(4, 6)).toEqual([UNKNOWN, "2026-10-17 22:15"]);
  });

  it("shows the state as a dot and the facts that go with it as lines below", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} />);

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
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} onOpen={onOpen} />);

    const open = screen.getByRole("button", { name: "konto.b@example.test" });
    expect(open).toHaveAttribute("aria-haspopup", "dialog");
    await userEvent.click(open);
    expect(onOpen).toHaveBeenCalledWith("b");
  });

  it("marks the sorted column and sorts through its header button", async () => {
    const onSort = vi.fn();
    render(
      <AdminAccountsTable
        activityRecordedFrom={START}
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

  it("sorts by Senast aktiv through its header button and marks it when it is the sort", async () => {
    const onSort = vi.fn();
    render(
      <AdminAccountsTable
        activityRecordedFrom={START}
        region={{ kind: "loaded", data: ROWS }}
        sort={{ key: "lastActiveAt", direction: "ascending" }}
        onSort={onSort}
      />,
    );

    expect(screen.getByRole("columnheader", { name: "Senast aktiv" })).toHaveAttribute("aria-sort", "ascending");
    expect(screen.getByRole("columnheader", { name: "Registrerad" })).not.toHaveAttribute("aria-sort");
    await userEvent.click(screen.getByRole("button", { name: "Senast aktiv" }));
    expect(onSort).toHaveBeenCalledWith("lastActiveAt");
  });

  it("explains both activity columns behind one help at Senast inloggad, with the start of recording", async () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} />);

    const help = screen.getAllByRole("button", { name: "Om Senast inloggad och Senast aktiv" });
    expect(help).toHaveLength(1);
    expect(help[0]!.closest("th")).toHaveTextContent(/^Senast inloggad$/);
    const named = document.getElementById(help[0]!.closest("th")!.getAttribute("aria-labelledby") ?? "");
    expect(named).toHaveTextContent(/^Senast inloggad$/);
    await userEvent.click(help[0]!);

    const dialog = screen.getByRole("dialog", { name: "Senast inloggad och Senast aktiv" });
    // The date and the time do not break apart: the formatter joins them with a no-break space.
    expect(dialog).toHaveAccessibleDescription(
      "Tidpunkterna sparas från och med 2026-10-17\u00a020:49. Saknas en tidpunkt har inget sparats sedan dess.",
    );
    expect(dialog).toHaveTextContent(
      "I Senast aktiv ingår också automatiska uppdateringar medan en flik med tjänsten är öppen och syns.",
    );
  });

  it.each([
    ["unavailable", "Kommer snart"],
    ["empty", "Inga konton matchar sökningen eller filtret."],
    ["failed", "Kontona kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar konton"],
  ] as const)("in the %s state shows one line and no account", (kind, line) => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind }} soonId="soon" />);

    const rows = bodyRows();
    expect(rows).toHaveLength(1);
    expect(rows[0]).toHaveTextContent(line);
    expect(screen.queryByRole("button", { name: /@example\.test/ })).toBeNull();
  });

  it("disables sorting while unbuilt and points it to the Kommer snart line", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "unavailable" }} soonId="soon" />);

    for (const name of ["Registrerad", "Senast aktiv"]) {
      const sort = screen.getByRole("button", { name });
      expect(sort).toBeDisabled();
      expect(sort).toHaveAttribute("aria-describedby", "soon");
    }
    expect(document.getElementById("soon")).toHaveTextContent("Kommer snart");
  });

  it("keeps its rows while newer ones load, and says so", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: ROWS }} busy />);
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
    expect(bodyRows()).toHaveLength(ROWS.length);
  });

  it("puts the caller's action under a failed read's sentence", () => {
    render(
      <AdminAccountsTable
        activityRecordedFrom={START}
        region={{ kind: "failed" }}
        failedAction={<button type="button">Försök igen</button>}
      />,
    );

    const [row] = bodyRows();
    expect(within(row!).getByRole("alert")).toBeInTheDocument();
    expect(within(row!).getByRole("button", { name: "Försök igen" })).toBeInTheDocument();
  });

  it("words a failure the way its caller knows it", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "failed" }} failedMessage="Försök igen om 6 sekunder." />);
    expect(screen.getByRole("alert")).toHaveTextContent("Försök igen om 6 sekunder.");
  });

  it("reports a failed load as an alert and a loading one as a status", () => {
    const { unmount } = render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "failed" }} />);
    expect(screen.getByRole("alert")).toHaveTextContent("Kontona kunde inte hämtas");
    unmount();
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loading" }} />);
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar konton");
    expect(screen.getByRole("table", { name: "Konton" })).toHaveAttribute("aria-busy", "true");
  });

  it("degrades a loaded region with no rows, which listRegion never builds, to the empty line", () => {
    render(<AdminAccountsTable activityRecordedFrom={START} region={{ kind: "loaded", data: [] }} />);
    expect(bodyRows()).toHaveLength(1);
    expect(bodyRows()[0]).toHaveTextContent("Inga konton matchar sökningen eller filtret.");
  });
});
