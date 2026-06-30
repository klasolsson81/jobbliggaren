import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import TillganglighetPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await TillganglighetPage();
  return render(element);
}

describe("/tillganglighet page (#263)", () => {
  it("renderar en h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Tillgänglighetsredogörelse" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Kända brister" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Om du stöter på ett hinder" })
    ).toBeInTheDocument();
  });

  it("relaterade länkar pekar på /kontakt och /integritet", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Kontakta oss" })
    ).toHaveAttribute("href", "/kontakt");
    expect(
      screen.getByRole("link", { name: "Integritetspolicy" })
    ).toHaveAttribute("href", "/integritet");
  });
});
