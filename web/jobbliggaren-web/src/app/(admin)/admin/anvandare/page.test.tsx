import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import AdminUsersPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

async function renderPage() {
  render(await AdminUsersPage());
}

function expectDescribedByComingSoon(element: HTMLElement) {
  const describedBy = element.getAttribute("aria-describedby");
  expect(describedBy).not.toBeNull();
  expect(document.getElementById(describedBy ?? "")?.textContent).toBe("Kommer snart");
}

describe("/admin/anvandare — the account list before #1974 (ADR 0150 D2/D3)", () => {
  it("is headed Användare", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Användare" })).toBeInTheDocument();
  });

  it("searches by email address only, and the search is disabled until the list exists", async () => {
    await renderPage();

    const search = screen.getByRole("searchbox", { name: "Sök på e-postadress" });
    expect(search).toBeDisabled();
    expectDescribedByComingSoon(search);
  });

  it("shows the status filters without counts, disabled", async () => {
    await renderPage();

    const group = screen.getByRole("radiogroup", { name: "Visa konton" });
    const options = within(group).getAllByRole("radio");
    expect(options.map((option) => option.textContent)).toEqual([
      "Alla",
      "Aktiva",
      "Suspenderade",
      "Ej verifierade",
      "Under radering",
    ]);
    for (const option of options) expect(option).toBeDisabled();
    expectDescribedByComingSoon(group);
    expect(group.textContent ?? "").not.toMatch(/\d/);
  });

  it("keeps the table's columns, identifies an account by its address, and shows no row", async () => {
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
    expect(within(table).queryByText(/namn/i)).toBeNull();

    const bodyRows = within(table).getAllByRole("row").slice(1);
    expect(bodyRows).toHaveLength(1);
    expect(bodyRows[0]?.textContent).toBe("Kommer snart");
  });

  it("disables the two sort controls and describes them", async () => {
    await renderPage();

    for (const name of ["Konto", "Registrerad"]) {
      const sort = screen.getByRole("button", { name });
      expect(sort).toBeDisabled();
      expectDescribedByComingSoon(sort);
    }
  });

  it("offers no pagination and no count while the list is unbuilt", async () => {
    await renderPage();

    expect(screen.queryByRole("navigation")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("keeps the table in a focusable scroll region named by its caption", async () => {
    await renderPage();

    const region = screen.getByRole("region", { name: "Konton" });
    expect(region).toHaveAttribute("tabindex", "0");
  });
});
