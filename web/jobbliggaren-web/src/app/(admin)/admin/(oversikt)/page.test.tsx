import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import AdminOverviewPage from "./page";

// The async page resolves its copy via getTranslations (next-intl/server, unavailable in jsdom):
// a real translator over the Swedish catalog. ComingSoon reads its copy from the test render's
// own provider.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

async function renderPage() {
  render(await AdminOverviewPage());
}

const CARDS = [
  "Nya användare",
  "Användare totalt",
  "Aktiva användare",
  "Inloggningar",
  "Nya användare och inloggningar",
  "Tjänster",
  "Server",
  "Backup",
  "E-post",
  "Kräver uppmärksamhet",
  "Senaste händelser",
];

describe("/admin — the overview before its sources exist (ADR 0150 D1/D2)", () => {
  it("is headed Översikt and renders every designed card as a labelled region", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Översikt" })).toBeInTheDocument();
    for (const card of CARDS) {
      expect(screen.getByRole("region", { name: card })).toBeInTheDocument();
    }
  });

  it("shows no number at all: every value is an en-dash with no unit", async () => {
    await renderPage();

    // The trend card's only digits are its period labels ("7 dygn"), checked below.
    for (const card of CARDS.filter((name) => name !== "Nya användare och inloggningar")) {
      expect(screen.getByRole("region", { name: card }).textContent ?? "").not.toMatch(/\d/);
    }
    for (const card of ["Nya användare", "Användare totalt", "Aktiva användare", "Inloggningar", "E-post"]) {
      const region = screen.getByRole("region", { name: card });
      expect(within(region).getByText("–")).toBeInTheDocument();
      expect(within(region).getByText("Kommer snart")).toBeInTheDocument();
    }
  });

  it("says Kommer snart in every unbuilt list instead of placeholder rows", async () => {
    await renderPage();

    for (const card of ["Tjänster", "Kräver uppmärksamhet", "Senaste händelser"]) {
      const region = screen.getByRole("region", { name: card });
      expect(within(region).getByText("Kommer snart")).toBeInTheDocument();
      expect(within(region).queryByRole("listitem")).toBeNull();
    }
  });

  it("keeps the attention edge neutral while the state is unknown", async () => {
    await renderPage();

    expect(screen.getByRole("region", { name: "Kräver uppmärksamhet" })).toHaveAttribute(
      "data-state",
      "unknown",
    );
  });

  it("disables the trend period group and describes it with the region's Kommer snart line", async () => {
    await renderPage();

    const group = screen.getByRole("group", { name: "Period" });
    const buttons = within(group).getAllByRole("button");
    expect(buttons.map((button) => button.textContent)).toEqual(["7 dygn", "30 dygn", "90 dygn"]);
    for (const button of buttons) {
      expect(button).toBeDisabled();
      const describedBy = button.getAttribute("aria-describedby");
      expect(describedBy).not.toBeNull();
      expect(document.getElementById(describedBy ?? "")?.textContent).toBe("Kommer snart");
    }
  });

  it("links each card to the page that holds its subject", async () => {
    await renderPage();

    expect(
      within(screen.getByRole("region", { name: "Tjänster" })).getByRole("link", { name: "Loggar" }),
    ).toHaveAttribute("href", "/admin/loggar");
    expect(
      within(screen.getByRole("region", { name: "E-post" })).getByRole("link", { name: "E-postleverans" }),
    ).toHaveAttribute("href", "/admin/e-post");
    expect(
      within(screen.getByRole("region", { name: "Senaste händelser" })).getByRole("link", {
        name: "Granskning",
      }),
    ).toHaveAttribute("href", "/admin/granskning");
  });
});
