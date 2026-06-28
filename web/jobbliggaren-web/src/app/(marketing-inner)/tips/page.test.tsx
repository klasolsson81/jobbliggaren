import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svTips from "../../../../messages/sv/content-tips.json";
import TipsPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-tips") =>
    createTranslator({
      locale: "sv",
      messages: { "content-tips": svTips },
      namespace,
    }),
}));

async function renderPage() {
  const element = await TipsPage();
  return render(element);
}

describe("/tips page (#261)", () => {
  it("renderar en h1 och sektionerna från katalogen", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Tips för jobbsökande" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Anpassa ditt CV" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Håll koll på sista ansökningsdag",
      })
    ).toBeInTheDocument();
  });

  it("har interna länkar till /jobb och /cv", async () => {
    await renderPage();

    expect(screen.getByRole("link", { name: "Sök jobb" })).toHaveAttribute(
      "href",
      "/jobb"
    );
    expect(screen.getByRole("link", { name: "Gå till CV" })).toHaveAttribute(
      "href",
      "/cv"
    );
  });
});
