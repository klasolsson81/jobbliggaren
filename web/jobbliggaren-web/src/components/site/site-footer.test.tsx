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
describe("SiteFooter (LP-3, #256; civic-IA #390)", () => {
  it("renders exactly one footer landmark", () => {
    render(<SiteFooter />);
    expect(screen.getAllByRole("contentinfo")).toHaveLength(1);
  });

  it("renders the three named navigation columns and NO Produkt column (#390)", () => {
    render(<SiteFooter />);
    for (const name of ["Kom igång", "Stöd och guider", "Om Jobbliggaren"]) {
      expect(screen.getByRole("navigation", { name })).toBeInTheDocument();
    }
    // The auth-gated product deep-links column was removed (#390).
    expect(screen.queryByRole("navigation", { name: "Produkt" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Sök jobb" })).toBeNull();
    expect(screen.queryByText("Påminnelser")).toBeNull();
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
    // start.register is live per the CTO verdict (forward-compatible /registrera).
    expect(screen.getByRole("link", { name: "Skapa konto" })).toHaveAttribute(
      "href",
      "/registrera",
    );
    expect(screen.getByRole("link", { name: "Utforska som gäst" })).toHaveAttribute(
      "href",
      "/gast/oversikt",
    );
    // about.self resolves to the public /om page (link, distinct from the
    // identically-named column heading).
    expect(screen.getByRole("link", { name: "Om Jobbliggaren" })).toHaveAttribute(
      "href",
      "/om",
    );
  });

  it("renders not-yet-built content links as aria-disabled spans, out of tab order", () => {
    render(<SiteFooter />);
    // Hjälpcenter (support), För utvecklare (about), Tillgänglighet (legal bar)
    // are all gated content routes — disabled spans, never links.
    for (const name of ["Hjälpcenter", "För utvecklare", "Tillgänglighet"]) {
      expect(screen.queryByRole("link", { name })).toBeNull();
      const el = screen.getByText(name);
      expect(el.tagName).toBe("SPAN");
      expect(el).toHaveAttribute("aria-disabled", "true");
    }
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

  it("groups legal/policy links in the thin bottom utility nav, once each (#390)", () => {
    render(<SiteFooter />);
    const legalNav = screen.getByRole("navigation", {
      name: "Juridik och policyer",
    });
    // Live policy links live ONLY here (a single home each — no column dup).
    const links: ReadonlyArray<readonly [string, string]> = [
      ["Användarvillkor", "/villkor"],
      ["Integritetspolicy", "/integritet"],
      ["Cookies", "/cookies"],
    ];
    for (const [name, href] of links) {
      const all = screen.getAllByRole("link", { name });
      expect(all).toHaveLength(1);
      expect(all[0]).toHaveAttribute("href", href);
      expect(within(legalNav).getByRole("link", { name })).toBe(all[0]);
    }
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
