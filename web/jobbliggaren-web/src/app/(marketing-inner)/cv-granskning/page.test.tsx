import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svCv from "../../../../messages/sv/content-cv-granskning.json";
import { isProtectedPath } from "@/lib/auth/protected-routes";
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

  it("visar de fyra omdömena, förbättrings-exemplet och vägen till ett konto", async () => {
    await renderPage();

    for (const verdict of ["Godkänt", "Delvis", "Underkänt", "Ej bedömt"]) {
      expect(screen.getByText(verdict)).toBeInTheDocument();
    }
    // The improvement example renders the cited "before" line verbatim.
    expect(
      screen.getByText("Ansvarig för olika uppgifter inom marknadsföring.")
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Skapa konto" })
    ).toHaveAttribute("href", "/logga-in");
  });

  // An anonymous visitor following a link into the (app) group lands on the sign-in wall (#2178).
  it("länkar aldrig in i en inloggad vy", async () => {
    await renderPage();

    const hrefs = screen.getAllByRole("link").map((link) => link.getAttribute("href") ?? "");
    expect(hrefs.length).toBeGreaterThan(0);
    expect(hrefs.filter(isProtectedPath)).toEqual([]);
  });
});
