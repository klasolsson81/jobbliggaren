import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import ForUtvecklarePage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await ForUtvecklarePage();
  return render(element);
}

describe("/for-utvecklare page (#263)", () => {
  it("renderar en h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "För utvecklare" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Källtillgänglig kod" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Så är Jobbliggaren byggt" })
    ).toBeInTheDocument();
  });

  it("länkar till det publika kodförrådet på GitHub", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "github.com/klasolsson81/jobbliggaren" })
    ).toHaveAttribute("href", "https://github.com/klasolsson81/jobbliggaren");
  });

  it("relaterade länkar pekar på /om och /kontakt", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Om Jobbliggaren" })
    ).toHaveAttribute("href", "/om");
    expect(
      screen.getByRole("link", { name: "Kontakta oss" })
    ).toHaveAttribute("href", "/kontakt");
  });
});
