import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svFaq from "../../../../messages/sv/content-faq.json";
import VanligaFragorPage from "./page";

// The async server page resolves copy via `getTranslations("content-faq")`.
// next-intl's server entry is unavailable in jsdom → mock to a real translator
// over the Swedish catalog (source of truth).
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-faq") =>
    createTranslator({
      locale: "sv",
      messages: { "content-faq": svFaq },
      namespace,
    }),
}));

async function renderPage() {
  const element = await VanligaFragorPage();
  return render(element);
}

describe("/vanliga-fragor page (#261)", () => {
  it("renderar en h1 och frågorna/svaren från katalogen", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Vanliga frågor" })
    ).toBeInTheDocument();
    expect(screen.getByText("Kostar Jobbliggaren något?")).toBeInTheDocument();
    expect(
      screen.getByText(/Jobbliggaren är gratis att använda/)
    ).toBeInTheDocument();
    expect(
      screen.getByText("Använder ni AI för att läsa mitt CV?")
    ).toBeInTheDocument();
  });

  it("har giltig FAQPage JSON-LD byggd från samma källa som synliga frågor", async () => {
    const { container } = await renderPage();

    const script = container.querySelector(
      'script[type="application/ld+json"]'
    );
    expect(script).not.toBeNull();

    const parsed = JSON.parse(script?.textContent ?? "{}");
    expect(parsed["@type"]).toBe("FAQPage");
    expect(Array.isArray(parsed.mainEntity)).toBe(true);
    // En post per FAQ-nyckel; varje fråga måste finnas synligt på sidan
    // (JSON-LD får inte drifta från innehållet).
    expect(parsed.mainEntity).toHaveLength(8);
    for (const entry of parsed.mainEntity) {
      expect(entry["@type"]).toBe("Question");
      expect(screen.getByText(entry.name)).toBeInTheDocument();
      expect(entry.acceptedAnswer["@type"]).toBe("Answer");
      expect(typeof entry.acceptedAnswer.text).toBe("string");
      expect(entry.acceptedAnswer.text.length).toBeGreaterThan(0);
    }
  });
});
