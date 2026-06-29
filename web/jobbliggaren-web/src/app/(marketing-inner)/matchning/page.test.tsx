import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svMatchning from "../../../../messages/sv/content-matchning.json";
import MatchningPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-matchning") =>
    createTranslator({
      locale: "sv",
      messages: { "content-matchning": svMatchning },
      namespace,
    }),
}));

// MatchChip pulls its label from the jobads client-i18n context (useTranslations).
// Stub it so the page test stays a synchronous render without a client provider;
// the stub exposes the grade so we can assert the page passes the right grades.
vi.mock("@/components/job-ads/match-chip", () => ({
  MatchChip: ({ grade }: { grade: string }) => (
    <span data-grade={grade}>{grade}</span>
  ),
}));

async function renderPage() {
  const element = await MatchningPage();
  return render(element);
}

describe("/matchning page (#365)", () => {
  it("renderar h1 och sektioner ur content-matchning", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: "Så fungerar matchningen",
      })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Matchningsgraderna" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Vad vi medvetet inte bedömer",
      })
    ).toBeInTheDocument();
  });

  it("visar de fyra graderna + relaterad och CTA till /registrera", async () => {
    await renderPage();

    for (const grade of ["Top", "Strong", "Good", "Basic", "Related"]) {
      expect(screen.getByText(grade)).toBeInTheDocument();
    }
    expect(
      screen.getByRole("link", { name: "Skapa konto" })
    ).toHaveAttribute("href", "/registrera");
  });
});
