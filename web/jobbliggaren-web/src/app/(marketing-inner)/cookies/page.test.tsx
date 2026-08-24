import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import enLegal from "../../../../messages/en/content-legal.json";
import CookiesPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await CookiesPage();
  return render(element);
}

describe("/cookies page (#262)", () => {
  it("renderar h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Cookiepolicy" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Nödvändiga cookies" })
    ).toBeInTheDocument();
  });

  it("bär 180-dagarsupplysningen som annars inte finns någonstans", async () => {
    // security-auditor Minor 1 on PR #1493: that PR removed the duplicate of
    // this statement from beside the login checkbox, so this page became the
    // sole carrier of the retention period a user consents to. The catalog
    // assertion covers the second locale, which the render cannot reach.
    await renderPage();

    // Two rows carry the duration (the prose section and the cookie table), so
    // the assertion is on presence, not on a single occurrence.
    expect(screen.getAllByText(/i upp till 180 dagar/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/delade datorer/).length).toBeGreaterThan(0);

    const en = JSON.stringify(enLegal);
    expect(en).toContain("180 days");
  });

  it("relaterade länkar pekar på /integritet och /villkor", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Integritetspolicy" })
    ).toHaveAttribute("href", "/integritet");
    expect(
      screen.getByRole("link", { name: "Användarvillkor" })
    ).toHaveAttribute("href", "/villkor");
  });
});
