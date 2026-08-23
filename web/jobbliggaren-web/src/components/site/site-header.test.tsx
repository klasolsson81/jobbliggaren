import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SiteHeader } from "./site-header";
import type { LandingStats } from "@/components/landing/landing-stats-format";

// next/link resolves navigation hooks; stub the navigation surface so the RSC
// header renders in jsdom (mirrors site-footer.test.tsx).
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));

// Stats arrive as a prop from the server-fetch in <LandingPage/>; the header is
// pure rendering, so these assert the rendered shape, not the fetch.
const STATS_MOCK: LandingStats = { activeCount: 45_580, newToday: 312 };

// The global test provider renders with locale "sv", so labels are Swedish.
describe("SiteHeader — the one public header (#1476)", () => {
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

  it("shows the login action by default (marketing-inner surface)", () => {
    render(<SiteHeader />);
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
  });

  it("hides the login action when showLogin is false (auth surface)", () => {
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

  it("consumes the shared .jp-head namespace", () => {
    const { container } = render(<SiteHeader />);
    expect(container.querySelector("header.jp-head")).not.toBeNull();
    expect(container.querySelector("nav.jp-head__inner")).not.toBeNull();
    // No leftover legacy .jp-land-top markup after the #258 rewrite.
    expect(container.querySelector(".jp-land-top")).toBeNull();
  });

  it("renders no stats when the surface passes none (every inner page)", () => {
    render(<SiteHeader />);
    expect(screen.queryByText("aktiva annonser")).not.toBeInTheDocument();
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
  });
});

describe("SiteHeader — the landing surface (stats slot)", () => {
  it("renders the brand + both stats blocks", () => {
    render(<SiteHeader stats={STATS_MOCK} />);
    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("formats large numbers with the Swedish locale (45 580) and a +delta", () => {
    render(<SiteHeader stats={STATS_MOCK} />);
    // sv-SE uses U+00A0 (nbsp) as the thousands separator — match either form.
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    // newToday is rendered with a leading "+" (delta tint).
    expect(screen.getByText("+312")).toBeInTheDocument();
  });

  it("carries the login action ALONGSIDE the stats — #1476", () => {
    // The landing header used to carry no account action at all, because the
    // on-page AuthCard owned the single one (epic #267 design rule 4). The
    // account card that replaces it links out to /registrera, so the header is
    // now where an existing user finds the way in. Bites on revert: dropping
    // the action leaves `/` with no login affordance above the fold.
    render(<SiteHeader stats={STATS_MOCK} />);
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
  });

  it("names the live-stats cluster via role=group (not aria-label on a role=generic div) — #609", () => {
    render(<SiteHeader stats={STATS_MOCK} />);
    // A bare `aria-label` on a `<div>` lands on role=generic, where ARIA-in-HTML
    // prohibits name-from-author (SR support is inconsistent). `role="group"`
    // supports naming, so the stats-group label is reliably announced. Bites on
    // revert: without the role the div is role=generic and getByRole("group") throws.
    expect(
      screen.getByRole("group", { name: "Aktiva annonser i Jobbliggaren" }),
    ).toBeInTheDocument();
  });

  it("contains NO theme/lang toggles (HANDOVER §0.7 — they live in the footer)", () => {
    render(<SiteHeader stats={STATS_MOCK} />);
    expect(
      screen.queryByRole("button", { name: /tema|theme|mörk|ljus/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: /Språk|Language/i }),
    ).not.toBeInTheDocument();
  });
});

describe("SiteHeader — omätta tal renderas ALDRIG (CTO-bind 2026-07-13, A′)", () => {
  it("utelämnar HELA stat-gruppen när talen inte är mätta", () => {
    // REGRESSION: tidigare gav en kall Redis-cache ett hårdkodat golv (40 000) som backend märkte med
    // isStale, men FE slängde flaggan och renderade siffran som ett faktum — på produktens ytterdörr,
    // för varje anonym besökare. Nu är ett omätt tal null och gruppen utelämnas: ett kort tomrum är
    // billigare än en permanent strukturell osanning.
    render(<SiteHeader stats={{ activeCount: null, newToday: null }} />);

    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.queryByText("aktiva annonser")).not.toBeInTheDocument();
    expect(screen.queryByText("nya idag")).not.toBeInTheDocument();
    // Framför allt: ingen siffra alls, och absolut inte golvet.
    expect(screen.queryByText(/40\s?000/)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\d/);
  });

  it("renderar en MÄTT nolla som 0 (0 och null är olika svar)", () => {
    // En sann nolla ("inget publicerat än idag", kl. 00:05 UTC) får inte döljas — bara "vi vet inte".
    render(<SiteHeader stats={{ activeCount: 41_475, newToday: 0 }} />);

    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("+0")).toBeInTheDocument();
  });
});
