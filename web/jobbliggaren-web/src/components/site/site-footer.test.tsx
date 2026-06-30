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
describe("SiteFooter (LP-3, #256; civic-IA #390 → #393)", () => {
  it("renders exactly one footer landmark", () => {
    render(<SiteFooter />);
    expect(screen.getAllByRole("contentinfo")).toHaveLength(1);
  });

  it("renders the four even navigation columns and NO Produkt column (#390/#393)", () => {
    render(<SiteFooter />);
    for (const name of [
      "Kom igång",
      "Stöd och guider",
      "Om Jobbliggaren",
      "Juridik",
    ]) {
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

  it("renders no aria-disabled link spans — every footer content route is now live (#263)", () => {
    const { container } = render(<SiteFooter />);
    // /for-utvecklare flipped live in #263 — the last gated content route. The
    // null → aria-disabled-span mechanism is kept as forward-compat scaffolding
    // (CTO 2026-06-30) but no COLUMNS entry exercises it now, so the footer must
    // render zero aria-disabled spans.
    expect(container.querySelectorAll('span[aria-disabled="true"]')).toHaveLength(
      0,
    );
  });

  it("links the now-live Hjälpcenter hub in the support column (#262)", () => {
    render(<SiteFooter />);
    const supportNav = screen.getByRole("navigation", { name: "Stöd och guider" });
    const link = within(supportNav).getByRole("link", { name: "Hjälpcenter" });
    expect(link).toHaveAttribute("href", "/hjalpcenter");
  });

  it("links the now-live För utvecklare page in the about column (#263)", () => {
    render(<SiteFooter />);
    const aboutNav = screen.getByRole("navigation", { name: "Om Jobbliggaren" });
    const link = within(aboutNav).getByRole("link", { name: "För utvecklare" });
    expect(link).toHaveAttribute("href", "/for-utvecklare");
  });

  it("has no social block or 'Följ oss' (removed — accounts not coming soon, #393)", () => {
    render(<SiteFooter />);
    expect(screen.queryByText("Följ oss")).toBeNull();
    for (const name of ["LinkedIn", "Facebook", "YouTube", "Instagram"]) {
      expect(screen.queryByText(name)).toBeNull();
    }
  });

  it("groups legal/policy links in their own 'Juridik' column, once each (#393)", () => {
    render(<SiteFooter />);
    const legalNav = screen.getByRole("navigation", { name: "Juridik" });
    // Live policy links live ONLY in this column (a single home each — no dup).
    // Tillgänglighet flipped to a live route in #263, joining the column.
    const links: ReadonlyArray<readonly [string, string]> = [
      ["Användarvillkor", "/villkor"],
      ["Integritetspolicy", "/integritet"],
      ["Cookies", "/cookies"],
      ["Tillgänglighet", "/tillganglighet"],
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
