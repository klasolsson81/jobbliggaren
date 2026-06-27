import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { SiteFooter } from "./site-footer";

// LanguageSwitcher (the footer-variant toggle island) reads useRouter; next/link
// resolves navigation hooks too. Mock the navigation surface so the RSC footer
// renders in jsdom (mirrors landing-page.test.tsx).
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));

// setLocaleAction reaches next/headers cookies (server-only) — stub it.
vi.mock("@/i18n/set-locale-action", () => ({
  setLocaleAction: vi.fn().mockResolvedValue(undefined),
}));

// The global test provider renders with locale "sv", so labels are Swedish.
describe("SiteFooter (LP-3, #256)", () => {
  it("renders exactly one footer landmark", () => {
    render(<SiteFooter />);
    expect(screen.getAllByRole("contentinfo")).toHaveLength(1);
  });

  it("renders the four named navigation columns", () => {
    render(<SiteFooter />);
    for (const name of ["Produkt", "Kom igång", "Stöd och guider", "Om och juridik"]) {
      expect(screen.getByRole("navigation", { name })).toBeInTheDocument();
    }
  });

  it("renders the single free line exactly once (K5)", () => {
    render(<SiteFooter />);
    expect(
      screen.getAllByText("Jobbliggaren är gratis att använda."),
    ).toHaveLength(1);
  });

  it("renders the copyright exactly once", () => {
    render(<SiteFooter />);
    expect(screen.getAllByText("© 2026 Jobbliggaren")).toHaveLength(1);
  });

  it("links live routes (no dead hrefs)", () => {
    render(<SiteFooter />);
    expect(screen.getByRole("link", { name: "Sök jobb" })).toHaveAttribute(
      "href",
      "/jobb",
    );
    // start.register is live per the CTO verdict (forward-compatible /registrera).
    expect(screen.getByRole("link", { name: "Skapa konto" })).toHaveAttribute(
      "href",
      "/registrera",
    );
    expect(screen.getByRole("link", { name: "Utforska som gäst" })).toHaveAttribute(
      "href",
      "/gast/oversikt",
    );
  });

  it("renders not-yet-built content links as aria-disabled spans, out of tab order", () => {
    render(<SiteFooter />);
    // Paminnelser is explicitly not built.
    const reminders = screen.getByText("Påminnelser");
    expect(reminders.tagName).toBe("SPAN");
    expect(reminders).toHaveAttribute("aria-disabled", "true");
    // A gated content route (Hjälpcenter) is also disabled, never a link.
    expect(screen.queryByRole("link", { name: "Hjälpcenter" })).toBeNull();
    const help = screen.getByText("Hjälpcenter");
    expect(help.tagName).toBe("SPAN");
    expect(help).toHaveAttribute("aria-disabled", "true");
  });

  it("renders aria-disabled social text placeholders (no accounts yet)", () => {
    render(<SiteFooter />);
    for (const name of ["LinkedIn", "Facebook", "YouTube", "Instagram"]) {
      const el = screen.getByText(name);
      expect(el.tagName).toBe("SPAN");
      expect(el).toHaveAttribute("aria-disabled", "true");
      // Out of tab order: not a link.
      expect(screen.queryByRole("link", { name })).toBeNull();
    }
  });

  it("does not duplicate legal links (K3 dedupe): each appears once, only in the column", () => {
    render(<SiteFooter />);
    // Villkor + Cookies live ONLY in the legal column, once each, as links.
    const terms = screen.getAllByRole("link", { name: "Användarvillkor" });
    expect(terms).toHaveLength(1);
    expect(terms[0]).toHaveAttribute("href", "/villkor");
    const cookies = screen.getAllByRole("link", { name: "Cookies" });
    expect(cookies).toHaveLength(1);
    expect(cookies[0]).toHaveAttribute("href", "/cookies");
  });

  it("reuses the real language toggle in the footer variant (.jp-foot__lang)", () => {
    const { container } = render(<SiteFooter />);
    const group = screen.getByRole("group", { name: "Språk" });
    expect(group).toHaveClass("jp-foot__lang");
    expect(container.querySelector(".jp-foot__lang-btn")).not.toBeNull();
    // Functional toggle, not a disabled stub: the active locale is pressed.
    expect(
      within(group).getByRole("button", { name: "Svenska" }),
    ).toHaveAttribute("aria-pressed", "true");
  });
});
