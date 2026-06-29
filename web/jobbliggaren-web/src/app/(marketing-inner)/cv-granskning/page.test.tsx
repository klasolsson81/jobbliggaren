import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svCv from "../../../../messages/sv/content-cv-granskning.json";
import CvGranskningPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-cv-granskning") =>
    createTranslator({
      locale: "sv",
      messages: { "content-cv-granskning": svCv },
      namespace,
    }),
}));

async function renderPage() {
  const element = await CvGranskningPage();
  return render(element);
}

describe("/cv-granskning page (#368)", () => {
  it("renderar h1 och nyckelsektioner", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Så granskar vi ditt CV" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Fyra omdömen, och alltid med bevis",
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Så ger vi förbättringsförslag",
      })
    ).toBeInTheDocument();
  });

  it("visar de fyra omdömena, förbättrings-exemplet och länken till /cv", async () => {
    await renderPage();

    for (const verdict of ["Godkänt", "Delvis", "Underkänt", "Ej bedömt"]) {
      expect(screen.getByText(verdict)).toBeInTheDocument();
    }
    // The improvement example renders the cited "before" line verbatim.
    expect(
      screen.getByText("Ansvarig för olika uppgifter inom marknadsföring.")
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Till CV" })
    ).toHaveAttribute("href", "/cv");
  });
});
