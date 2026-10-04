import { beforeEach, describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { AccountSearchResponse } from "@/lib/dto/admin-accounts";
import AdminUsersPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

const searchAccounts = vi.hoisted(() => vi.fn<() => Promise<ApiResult<AccountSearchResponse>>>());
vi.mock("@/lib/api/admin-accounts", () => ({ searchAccounts }));

const RESPONSE: AccountSearchResponse = {
  accounts: {
    items: [
      {
        id: "00000000-0000-4000-8000-000000000001",
        email: "konto.a@example.test",
        role: "Admin",
        status: "Active",
        emailConfirmed: true,
        registeredAt: "2026-09-28T12:02:00Z",
        deletionEarliest: null,
        applicationCount: 4,
      },
      {
        id: "00000000-0000-4000-8000-000000000002",
        email: "konto.b@example.test",
        role: "User",
        status: "ProfileMissing",
        emailConfirmed: true,
        registeredAt: null,
        deletionEarliest: null,
        applicationCount: null,
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 25,
    totalPages: 1,
  },
  counts: { total: 2, active: 1, pendingDeletion: 0, profileMissing: 1 },
};

async function renderPage() {
  render(await AdminUsersPage());
}

beforeEach(() => {
  searchAccounts.mockReset();
});

describe("/admin/anvandare — every account, searchable by address (#1974, ADR 0151)", () => {
  it("reads the first page and the counts on the server, newest registration first, with no term", async () => {
    searchAccounts.mockResolvedValue({ kind: "ok", data: RESPONSE });
    await renderPage();

    expect(searchAccounts).toHaveBeenCalledWith({ sort: "RegisteredNewest", page: 1, pageSize: 25 });
    expect(screen.getByRole("heading", { level: 1, name: "Användare" })).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Konton" });
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0]?.textContent)).toEqual([
      "konto.a@example.test",
      "konto.b@example.test",
    ]);
    expect(rows[1]).toHaveTextContent("Ofullständig");
    expect(screen.getByRole("status")).toHaveTextContent("2 av 2 konton");
  });

  it("counts each filter, a known zero included, and searches by address", async () => {
    searchAccounts.mockResolvedValue({ kind: "ok", data: RESPONSE });
    await renderPage();

    const group = screen.getByRole("radiogroup", { name: "Visa konton" });
    expect(within(group).getAllByRole("radio").map((option) => option.textContent)).toEqual([
      "Alla (2)",
      "Aktiva (1)",
      "Under radering (0)",
      "Ofullständiga (1)",
    ]);
    expect(screen.getByRole("searchbox", { name: "Sök på e-postadress" })).toBeEnabled();
  });

  it("keeps the ledger's columns and opens an account through its row's button", async () => {
    searchAccounts.mockResolvedValue({ kind: "ok", data: RESPONSE });
    await renderPage();

    const table = screen.getByRole("table", { name: "Konton" });
    expect(within(table).getAllByRole("columnheader").map((th) => th.textContent)).toEqual([
      "Konto",
      "Roll",
      "Status",
      "Registrerad",
      "Senast inloggad",
      "Senast aktiv",
      "Ansökningar",
    ]);
    expect(screen.getByRole("button", { name: "konto.a@example.test" })).toHaveAttribute("aria-haspopup", "dialog");
    expect(screen.getByRole("region", { name: "Konton" })).toHaveAttribute("tabindex", "0");
  });

  it("shows a failed read in place of the rows, with no count, no summary and no pages", async () => {
    searchAccounts.mockResolvedValue({ kind: "error" });
    await renderPage();

    expect(screen.getByRole("alert")).toHaveTextContent("Kontona kunde inte hämtas. Försök igen om en stund.");
    expect(screen.getByRole("radiogroup", { name: "Visa konton" }).textContent ?? "").not.toMatch(/\d/);
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("navigation")).toBeNull();
  });

  it.each([
    [{ kind: "rateLimited", retryAfterSeconds: 6 } as const, "För många förfrågningar. Försök igen om 6 sekunder."],
    [{ kind: "forbidden" } as const, "Din session saknar Admin-rollen."],
    [{ kind: "unauthorized" } as const, "Du är inte inloggad längre. Logga in och försök igen."],
  ])("words a refused read by its cause (%o)", async (result, sentence) => {
    searchAccounts.mockResolvedValue(result);
    await renderPage();

    expect(screen.getByRole("alert")).toHaveTextContent(sentence);
  });
});
