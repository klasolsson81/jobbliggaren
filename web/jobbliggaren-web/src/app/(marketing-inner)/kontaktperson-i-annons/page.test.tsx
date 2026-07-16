import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import KontaktpersonIAnnonsPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await KontaktpersonIAnnonsPage();
  return render(element);
}

describe("/kontaktperson-i-annons page (#842 Tier A, Art. 14(5)(b))", () => {
  it("renderar h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: "Är du kontaktperson i en annons?",
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Vad vi behandlar och varifrån uppgifterna kommer",
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Dina rättigheter" })
    ).toBeInTheDocument();
  });

  it("beskriver invändningsrätten utan intresseavvägning och raderingsvägen", async () => {
    await renderPage();

    // Art. 21: the objection is honoured in full — the notice must say so
    // (the LIA's balance rests on it), and Art. 17 must describe whole-ad
    // removal, never a per-field promise no detector can keep.
    expect(
      screen.getByText(/tillmötesgår invändningen fullt ut/)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/raderar vi då hela vår kopia av annonsen/)
    ).toBeInTheDocument();
  });

  it("relaterad länk pekar på /integritet", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Integritetspolicy" })
    ).toHaveAttribute("href", "/integritet");
  });
});
