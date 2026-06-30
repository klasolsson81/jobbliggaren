import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import HjalpcenterPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await HjalpcenterPage();
  return render(element);
}

describe("/hjalpcenter page (#262)", () => {
  it("renderar en h1 och de två grupprubrikerna ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Hjälpcenter" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Guider och svar" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Mer hjälp" })
    ).toBeInTheDocument();
  });

  it("länkar vidare till de befintliga hjälpsidorna (hubb, inte duplikat)", async () => {
    await renderPage();

    const expected: ReadonlyArray<readonly [string, string]> = [
      ["Vanliga frågor", "/vanliga-fragor"],
      ["Så fungerar matchningen", "/matchning"],
      ["Så granskar vi ditt CV", "/cv-granskning"],
      ["Tips för jobbsökande", "/tips"],
      ["Kontakta oss", "/kontakt"],
      ["Tillgänglighet", "/tillganglighet"],
    ];

    for (const [name, href] of expected) {
      expect(screen.getByRole("link", { name })).toHaveAttribute("href", href);
    }
  });
});
