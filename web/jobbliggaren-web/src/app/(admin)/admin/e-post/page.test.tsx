import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import AdminEmailDeliveryPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

async function renderPage() {
  render(await AdminEmailDeliveryPage());
}

describe("/admin/e-post — email delivery before #1981 (ADR 0150 D2)", () => {
  it("is headed E-postleverans and names no provider it has no outcome from", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "E-postleverans" })).toBeInTheDocument();
    expect(document.body.textContent ?? "").not.toMatch(/smtp|strato|scaleway/i);
  });

  it("disables the period group and describes it", async () => {
    await renderPage();

    const group = screen.getByRole("group", { name: "Period" });
    for (const button of within(group).getAllByRole("button")) {
      expect(button).toBeDisabled();
      const describedBy = button.getAttribute("aria-describedby") ?? "";
      expect(document.getElementById(describedBy)?.textContent).toBe("Kommer snart");
    }
  });

  it("shows the three totals as unknown, never as zero", async () => {
    await renderPage();

    const terms = screen.getAllByRole("term").map((term) => term.textContent);
    const values = screen.getAllByRole("definition").map((value) => value.textContent);
    expect(terms).toEqual(["Skickade", "Misslyckade", "Utan mottagare"]);
    expect(values).toEqual(["–", "–", "–"]);
  });

  it("keeps the per-type table with one Kommer snart row and the failures list as one line", async () => {
    await renderPage();

    const table = screen.getByRole("table", { name: "Utskick per mejltyp" });
    expect(within(table).getAllByRole("columnheader").map((th) => th.textContent)).toEqual([
      "Mejltyp",
      "Skickade",
      "Fel",
      "Senaste fel",
    ]);
    const bodyRows = within(table).getAllByRole("row").slice(1);
    expect(bodyRows).toHaveLength(1);
    expect(bodyRows[0]?.textContent).toBe("Kommer snart");

    const failures = screen.getByRole("region", { name: "Senaste misslyckade utskick" });
    expect(within(failures).getByText("Kommer snart")).toBeInTheDocument();
  });
});
