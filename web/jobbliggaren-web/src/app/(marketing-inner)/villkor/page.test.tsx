import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import VillkorPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await VillkorPage();
  return render(element);
}

describe("/villkor page (#262)", () => {
  it("renderar h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Användarvillkor" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Vad Jobbliggaren är" })
    ).toBeInTheDocument();
  });

  it("relaterade länkar pekar på /integritet och /cookies", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Integritetspolicy" })
    ).toHaveAttribute("href", "/integritet");
    expect(
      screen.getByRole("link", { name: "Cookiepolicy" })
    ).toHaveAttribute("href", "/cookies");
  });
});
