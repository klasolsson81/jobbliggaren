import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import enLegal from "../../../../messages/en/content-legal.json";
import CookiesPage from "./page";

let locale: "sv" | "en" = "sv";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale,
      messages: { "content-legal": locale === "sv" ? svLegal : enLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await CookiesPage();
  return render(element);
}

describe("/cookies page (#262)", () => {
  beforeEach(() => { locale = "sv"; });

  it.each(["sv", "en"] as const)("renders its published date visibly after h1 in the band (%s)", async (language) => {
    locale = language;
    const { container } = await renderPage();
    const catalogue = language === "sv" ? svLegal : enLegal;
    const band = container.querySelector(".jp-pagehero__main");
    const date = screen.getByText(catalogue.cookies.updated);
    expect(band?.querySelectorAll("p.jp-pagehero__lede")).toHaveLength(1);
    expect(date.parentElement).toBe(band);
    expect(date.previousElementSibling?.tagName).toBe("H1");
    expect(date).toBeVisible();
    expect(date.closest('[aria-hidden="true"], .sr-only, details, [role="tooltip"]')).toBeNull();
    expect(screen.getAllByText(catalogue.cookies.updated)).toHaveLength(1);
  });

  it("renderar h1 och sektioner ur content-legal", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Cookiepolicy" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Nödvändiga cookies" })
    ).toBeInTheDocument();
  });

  it("bär upplysningen om hur länge man förblir inloggad", async () => {
    // security-auditor Minor 1 on PR #1493 made this page the carrier of the retention
    // period. Since #1738 the login steps state it too (ADR 0142 D4), and a session is
    // persistent by default: the policy leads with the 30 days that apply in practice,
    // names 180 as the ceiling, and keeps the shared-computer remedy. The catalog
    // assertion covers the second locale, which the render cannot reach.
    await renderPage();

    // Two rows carry the duration (the prose section and the cookie table), so
    // the assertion is on presence, not on a single occurrence.
    expect(screen.getAllByText(/i upp till 180 dagar/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/30 dagar/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/delad dator/).length).toBeGreaterThan(0);
    // The checkbox is gone; a policy that still describes it describes nothing.
    expect(screen.queryByText(/Håll mig inloggad/)).toBeNull();

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
