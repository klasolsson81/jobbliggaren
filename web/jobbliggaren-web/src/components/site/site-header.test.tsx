import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
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
  it("renders exactly one banner, and the nav landmark carries the brand", () => {
    const { container } = render(<SiteHeader />);
    expect(screen.getAllByRole("banner")).toHaveLength(1);
    const nav = screen.getByRole("navigation", { name: "Webbplatsnavigation" });
    // The landmark wraps the brand link and NOT the stats cluster: a screen
    // reader jumping to site navigation should not be read a count first.
    // Bites on revert: moving the label back onto .jp-head__inner puts the
    // stats group inside the landmark and this query returns the outer element.
    expect(
      nav.querySelector("a.jp-brand"),
    ).not.toBeNull();
    expect(container.querySelector("div.jp-head__inner")).not.toBeNull();
    expect(container.querySelector("nav.jp-head__inner")).toBeNull();
  });

  it("links the brand to the landing root with an accessible name", () => {
    render(<SiteHeader />);
    const brand = screen.getByRole("link", { name: "Jobbliggaren, startsida" });
    expect(brand).toHaveAttribute("href", "/");
  });

  it("shows the login action by default, and marks the row as an action row", () => {
    const { container } = render(<SiteHeader />);
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
    // `jp-head--action` is the hinge the whole narrow-screen ladder turns on: the
    // ≤480 step fires only on action rows. jsdom loads no CSS, so nothing else in
    // this suite can see the ladder — this class is the only thing it CAN see.
    // Bites on revert: inverting the ternary flips every public header's narrow
    // behaviour with no other test noticing.
    expect(container.querySelector("header.jp-head--action")).not.toBeNull();
  });

  it("hides the login action when showLogin is false, and drops the action row class", () => {
    const { container } = render(<SiteHeader showLogin={false} />);
    expect(screen.queryByRole("link", { name: "Logga in" })).toBeNull();
    expect(container.querySelector("header.jp-head--action")).toBeNull();
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
    // No leftover legacy .jp-land-top markup after the #258 rewrite.
    expect(container.querySelector(".jp-land-top")).toBeNull();
  });

  it("renders no stats when the surface passes none (every inner page)", () => {
    render(<SiteHeader />);
    expect(screen.queryByText("aktiva annonser")).not.toBeInTheDocument();
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
  });
});

// The landing is the only surface that passes `stats`, and since #1480 it passes
// no showLogin — so these render the combination production actually produces
// (AGENTS.md §5 `Tests:`). Until #1480 the landing suppressed the action because
// the hero's AuthCard mounted a tab with the same label; the absence pinned below
// is now a presence.
describe("SiteHeader — the landing surface (stats slot)", () => {
  const renderLanding = (stats: LandingStats) =>
    render(<SiteHeader stats={stats} />);

  it("renders the brand + both stats blocks", () => {
    renderLanding(STATS_MOCK);
    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("formats large numbers with the Swedish locale (45 580) and a +delta", () => {
    renderLanding(STATS_MOCK);
    // sv-SE uses U+00A0 (nbsp) as the thousands separator — match either form.
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    // newToday is rendered with a leading "+" (delta tint).
    expect(screen.getByText("+312")).toBeInTheDocument();
  });

  it("carries the account action, now that no hero tab owns the label", () => {
    // Two controls labelled "Logga in" with different behaviour (one navigating,
    // one switching a tab panel in place) sat ~134px apart on the product's front
    // door when the header half shipped ahead of the hero half. #1480 removed the
    // tab, so the header is the label's only home here. Bites on revert: passing
    // showLogin={false} again empties the right cluster of its only action.
    renderLanding(STATS_MOCK);
    expect(screen.getByRole("link", { name: /Logga in/i })).toHaveAttribute(
      "href",
      "/logga-in",
    );
    // Still exactly one account control: registration is reached from the hero
    // card, never from the header.
    expect(
      screen.queryByRole("link", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Logga in|Skapa konto/i }),
    ).not.toBeInTheDocument();
  });

  it("keeps the stats cluster OUT of the nav landmark", () => {
    // The div-vs-nav assertion in the block above only catches that exact revert.
    // This one crosses the threshold its comment claims: a refactor that moves
    // .jp-head__stats INTO the <nav> while .jp-head__inner stays a <div> passes
    // there and fails here.
    renderLanding(STATS_MOCK);
    const nav = screen.getByRole("navigation", { name: "Webbplatsnavigation" });
    expect(within(nav).queryByRole("group")).toBeNull();
    expect(nav.querySelector(".jp-head__stats")).toBeNull();
    // ...and the cluster DOES render on this surface, so the absence above is a
    // separation, not a vacuous pass on a header that has no stats at all.
    expect(
      screen.getByRole("group", { name: "Aktiva annonser i Jobbliggaren" }),
    ).toBeInTheDocument();
  });

  it("names the live-stats cluster via role=group (not aria-label on a role=generic div) — #609", () => {
    renderLanding(STATS_MOCK);
    // A bare `aria-label` on a `<div>` lands on role=generic, where ARIA-in-HTML
    // prohibits name-from-author (SR support is inconsistent). `role="group"`
    // supports naming, so the stats-group label is reliably announced. Bites on
    // revert: without the role the div is role=generic and getByRole("group") throws.
    expect(
      screen.getByRole("group", { name: "Aktiva annonser i Jobbliggaren" }),
    ).toBeInTheDocument();
  });

  it("carries the language control and still NO theme toggle (HANDOVER §0.7, amended 2026-08-23)", () => {
    // ⚠ The assertion this replaces queried `role="group"` — the shape the OLD
    // button-pair switcher had. The menu trigger is a role=button, so once the
    // switcher moved in, that query went INERT rather than red: it PASSED while
    // the header did the opposite of what its own title claimed. Caught by
    // senior-cto-advisor, not by the suite.
    //
    // Klas lifted the language half of §0.7; the theme half stands, so this pins
    // both directions at once.
    renderLanding(STATS_MOCK);
    expect(
      screen.getByRole("button", { name: /Språk/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /tema|theme|mörk|ljus/i }),
    ).not.toBeInTheDocument();
  });
});

describe("SiteHeader — omätta tal renderas ALDRIG (CTO-bind 2026-07-13, A′)", () => {
  it("utelämnar HELA stat-gruppen när talen inte är mätta", () => {
    // REGRESSION: tidigare gav en kall Redis-cache ett hårdkodat golv (40 000) som backend märkte med
    // isStale, men FE slängde flaggan och renderade siffran som ett faktum — på produktens ytterdörr,
    // för varje anonym besökare. Nu är ett omätt tal null och gruppen utelämnas: ett kort tomrum är
    // billigare än en permanent strukturell osanning.
    render(
      <SiteHeader
        stats={{ activeCount: null, newToday: null }}
        showLogin={false}
      />,
    );

    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.queryByText("aktiva annonser")).not.toBeInTheDocument();
    expect(screen.queryByText("nya idag")).not.toBeInTheDocument();
    // Framför allt: ingen siffra alls, och absolut inte golvet.
    expect(screen.queryByText(/40\s?000/)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\d/);
  });

  it("renderar en MÄTT nolla som 0 på BÅDA talen (0 och null är olika svar)", () => {
    // En sann nolla ("inget publicerat än idag", kl. 00:05 UTC) får inte döljas — bara "vi vet inte".
    // Båda talen pinnas: en truthiness-vakt (`stats.activeCount ? …`) i stället för `!== null` skulle
    // dölja en mätt nolla, och en ensidig pinne hade bara fångat halva den fällan.
    render(
      <SiteHeader stats={{ activeCount: 0, newToday: 0 }} showLogin={false} />,
    );

    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.getByText("+0")).toBeInTheDocument();
  });
});
