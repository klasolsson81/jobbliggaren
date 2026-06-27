import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SiteHeader } from "./site-header";

// next/link resolves navigation hooks; stub the navigation surface so the RSC
// header renders in jsdom (mirrors site-footer.test.tsx).
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));

// The global test provider renders with locale "sv", so labels are Swedish.
describe("SiteHeader (LP-5a, #258, minimal public header)", () => {
  it("renders exactly one banner with a labelled nav landmark", () => {
    render(<SiteHeader />);
    expect(screen.getAllByRole("banner")).toHaveLength(1);
    expect(
      screen.getByRole("navigation", { name: "Webbplatsnavigation" }),
    ).toBeInTheDocument();
  });

  it("links the brand to the landing root with an accessible name", () => {
    render(<SiteHeader />);
    const brand = screen.getByRole("link", { name: "Jobbliggaren, startsida" });
    expect(brand).toHaveAttribute("href", "/");
  });

  it("shows the login link by default (marketing-inner surface)", () => {
    render(<SiteHeader />);
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
  });

  it("hides the login link when showLogin is false (auth surface)", () => {
    render(<SiteHeader showLogin={false} />);
    expect(screen.queryByRole("link", { name: "Logga in" })).toBeNull();
    // The nav landmark and brand still render on the auth surface.
    expect(
      screen.getByRole("link", { name: "Jobbliggaren, startsida" }),
    ).toBeInTheDocument();
  });

  it("renders a skip link to #main as the first focusable element", () => {
    const { container } = render(<SiteHeader />);
    const skip = screen.getByRole("link", { name: "Hoppa till huvudinnehåll" });
    expect(skip).toHaveAttribute("href", "#main");
    // First in DOM order so it is the first focusable element of the surface.
    expect(container.firstChild).toBe(skip);
  });

  it("consumes the shared .jp-head namespace for landing-header parity", () => {
    const { container } = render(<SiteHeader />);
    expect(container.querySelector("header.jp-head")).not.toBeNull();
    expect(container.querySelector("nav.jp-head__inner")).not.toBeNull();
    // No leftover legacy .jp-land-top markup after the #258 rewrite.
    expect(container.querySelector(".jp-land-top")).toBeNull();
  });
});
