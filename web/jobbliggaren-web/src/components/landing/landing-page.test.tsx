import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import LandingPage from "@/app/(marketing)/page";
import MarketingLayout from "@/app/(marketing)/layout";
import svMessages from "../../../messages/sv";

// next/navigation: SiteHeader's LanguageSwitcher reads useRouter and there is no
// Next router context in jsdom. `useSearchParams` is stubbed only because the
// mock replaces the whole module — nothing on this surface reads it since #1480
// took the forms out of the hero.
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
}));

// SiteHeader's LanguageSwitcher posts setLocaleAction (server-only cookies).
vi.mock("@/i18n/set-locale-action", () => ({
  setLocaleAction: vi.fn().mockResolvedValue(undefined),
}));

// ADR 0064 — landing-stats fetch server-side in LandingPage. No backend in
// jsdom: mock the helper so stats resolve synchronously. The header asserts
// these values; the hero no longer shows stats (one place, no repetition).
vi.mock("@/components/landing/landing-stats", async () => {
  const actual = await vi.importActual<
    typeof import("@/components/landing/landing-stats")
  >("@/components/landing/landing-stats");
  return {
    ...actual,
    getLandingStats: vi.fn().mockResolvedValue({
      activeCount: 45_580,
      newToday: 312,
    }),
  };
});

// The layout owns the client i18n payload; return the REAL catalog.
vi.mock("next-intl/server", () => ({
  getLocale: async () => "sv",
  getMessages: async () => svMessages,
}));

// jsdom renders the WHOLE tree as client React, so the layout's scoped client
// payload would also have to carry every namespace its SERVER components read.
// Since #1480 that is `landing`, which no client component in this boundary
// reaches — in production SiteHeader, SiteFooter and the two landing sections are
// RSCs and resolve from the request config, never from this provider. Hand them
// the full catalog here; the declaration-equals-reach property belongs to
// client-namespace-payload.test.ts, which walks the import graph statically and is
// the only instrument that can tell a server reader from a client one.
vi.mock("@/i18n/client-messages", async () => {
  const actual =
    await vi.importActual<typeof import("@/i18n/client-messages")>(
      "@/i18n/client-messages",
    );
  return { ...actual, pickClientMessages: () => svMessages };
});

// Since #1477 the chrome (SiteHeader + SiteFooter) lives in
// (marketing)/layout, so the page alone is no longer the surface a visitor
// sees — compose the two the way Next does. Async RSCs can't be rendered
// directly by RTL; pre-resolve the element tree.
async function renderAsyncPage() {
  const element = await MarketingLayout({ children: LandingPage() });
  return render(element);
}

describe("LandingPage (LP-4, #257 — Liggaren ledger hero)", () => {
  it("renders header + ledger hero + features + a single footer", async () => {
    await renderAsyncPage();
    // Brand appears in both the header and the inverse footer brand.
    expect(screen.getAllByText("Jobbliggaren").length).toBeGreaterThan(0);
    // Features section. Its mono kicker was removed in #1480 — the heading is
    // the section's only label now, and it is left-aligned.
    expect(
      screen.getByRole("heading", {
        name: "Allt du behöver för att hålla ordning",
      }),
    ).toBeInTheDocument();
    // Exactly one footer landmark (the shared SiteFooter, K3 dedupe).
    expect(screen.getAllByRole("contentinfo")).toHaveLength(1);
    // The "Om Jobbliggaren" about column renders (its nav is uniquely named by
    // its heading — distinct from the same-named about link inside it, #390).
    expect(
      screen.getByRole("navigation", { name: "Om Jobbliggaren" }),
    ).toBeInTheDocument();
  });

  it("hero <h1> is the crawlable verb stack with real verb text (no ledger numbers)", async () => {
    await renderAsyncPage();
    const heading = screen.getByRole("heading", { level: 1 });
    for (const verb of ["Hitta jobbet.", "Sök jobbet.", "Följ upp ansökan."]) {
      expect(heading).toHaveTextContent(verb);
    }
    // Plattan (förslag 3a) drops the 01/02/03 ledger numbers — pure verb stack.
    for (const num of ["01", "02", "03"]) {
      expect(screen.queryByText(num)).not.toBeInTheDocument();
    }
  });

  it("renders the six feature cells including the three new features", async () => {
    await renderAsyncPage();
    const featureTitles = [
      "Sökning",
      "Matchning",
      "Ansökningar",
      "CV-granskning",
      "Företagsbevakning",
      "Påminnelser",
    ];
    for (const title of featureTitles) {
      expect(
        screen.getByRole("heading", { level: 3, name: title }),
      ).toBeInTheDocument();
    }
  });

  it("shows the plate source line and the free line at the CTA", async () => {
    await renderAsyncPage();
    // The free line is unique to the hero ("helt gratis") — distinct from the
    // footer's "Jobbliggaren är gratis att använda." closing row.
    expect(
      screen.getByText("Jobbliggaren är helt gratis att använda."),
    ).toBeInTheDocument();
    // The mono source line renders on the plate (and once more in the footer).
    // Both name SCB since #1480; the exact-string query bites if only one moved.
    expect(
      screen.getAllByText("Byggd på öppen data från Arbetsförmedlingen och SCB")
        .length,
    ).toBe(2);
  });

  it("renders live stats in the header (45 580 active ads)", async () => {
    await renderAsyncPage();
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
  });

  it("sells the account instead of demanding a form fill (2b, #1480)", async () => {
    const { container } = await renderAsyncPage();
    expect(
      screen.getByRole("heading", { name: "Det här får du med ett konto" }),
    ).toBeInTheDocument();
    for (const row of [
      "Annonser du sparar, med påminnelse före sista ansökningsdag",
      "Varje ansökan spårad från utkast till svar",
      "Företag du bevakar, med deras nya annonser på översikten",
      "Ditt CV granskat mot en svensk kvalitetsrubrik",
    ]) {
      expect(screen.getByText(row)).toBeInTheDocument();
    }
    // The CTA is a LINK to /registrera, not a submit. Scoped to the card: the
    // footer's "Kom igång" column carries a link with the same label. Bites on
    // revert: mounting a form here turns this back into a role="button".
    const card = container.querySelector(".jp-land-account") as HTMLElement;
    expect(card).not.toBeNull();
    const cta = within(card).getByRole("link", { name: "Skapa konto" });
    expect(cta).toHaveAttribute("href", "/registrera");
  });

  it("is the page's only solid primary button (ADR 0038 / DESIGN.md §6)", async () => {
    const { container } = await renderAsyncPage();
    const primaries = container.querySelectorAll(".jp-btn--primary");
    expect(primaries).toHaveLength(1);
    expect(primaries[0]).toHaveTextContent("Skapa konto");
  });

  it("exposes a skip link to #main", async () => {
    await renderAsyncPage();
    const skip = screen.getByRole("link", { name: "Hoppa till huvudinnehåll" });
    expect(skip).toHaveAttribute("href", "#main");
  });

  it("has NO waitlist CTA and NO product-peek (replaced by the plate hero)", async () => {
    await renderAsyncPage();
    expect(
      screen.queryByRole("button", { name: /Anmäl till väntelista/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("A-2841")).not.toBeInTheDocument();
  });

  it("puts the one 'Logga in' in the header, now that no hero tab carries it", async () => {
    // #1476 shipped the header half first, so for one wave the landing mounted
    // SiteHeader with showLogin={false}: two controls labelled "Logga in" with
    // different behaviour — one navigating, one switching a tab panel in place —
    // sat ~134px apart on the product's front door. #1480 removed the tab, so the
    // header is the label's only home. Bites on revert in both directions:
    // restoring showLogin={false} empties the header, remounting a tabbed card
    // re-creates the pair.
    const { container } = await renderAsyncPage();
    const head = container.querySelector(".jp-head");
    expect(head).not.toBeNull();
    expect(
      within(head as HTMLElement).getByRole("link", { name: /Logga in/i }),
    ).toHaveAttribute("href", "/logga-in");
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
  });

  it("mounts no form field at all — the whole point of 2b (#1480)", async () => {
    // The hero used to ask an anonymous visitor to fill in a registration form
    // before the page had said what an account is for. Bites on revert: remount
    // AuthCard, or any form, and the landing document grows inputs again.
    const { container } = await renderAsyncPage();
    expect(container.querySelectorAll("input")).toHaveLength(0);
    expect(container.querySelectorAll("form")).toHaveLength(0);
  });
});
