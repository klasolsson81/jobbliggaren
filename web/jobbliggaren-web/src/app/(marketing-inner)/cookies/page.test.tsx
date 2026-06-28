import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import CookiesPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await CookiesPage();
  return render(element);
}

describe("/cookies page (#262)", () => {
  it("renderar h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Cookiepolicy" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Nödvändiga cookies" })
    ).toBeInTheDocument();
  });

  it("relaterade länkar pekar på /integritet och /villkor", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Integritetspolicy" })
    ).toHaveAttribute("href", "/integritet");
    expect(
      screen.getByRole("link", { name: "Användarvillkor" })
    ).toHaveAttribute("href", "/villkor");
  });
});
