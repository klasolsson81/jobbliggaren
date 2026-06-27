import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import LandingPage from "@/app/(marketing)/page";

// next/navigation: useSearchParams must be mocked in jsdom (no Next router
// context) — the inline AuthCard's Login/RegisterForm read it, and SiteFooter's
// LanguageSwitcher reads useRouter. A hoisted ref lets a test vary the query
// string to assert next-param deep-link propagation through the mounted form.
const { searchParamsRef } = vi.hoisted(() => ({
  searchParamsRef: { current: new URLSearchParams() },
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => searchParamsRef.current,
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
}));

// The auth forms wire login/registerAction via useActionState — stub the module
// so mounting them never calls fetch().
vi.mock("@/lib/auth/actions", () => ({
  loginAction: vi.fn().mockResolvedValue(null),
  registerAction: vi.fn().mockResolvedValue(null),
}));

// SiteFooter's LanguageSwitcher posts setLocaleAction (server-only cookies).
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

// Async RSC can't be rendered directly by RTL; pre-resolve the element tree.
async function renderAsyncPage() {
  const element = await LandingPage();
  return render(element);
}

describe("LandingPage (LP-4, #257 — Liggaren ledger hero)", () => {
  beforeEach(() => {
    searchParamsRef.current = new URLSearchParams();
  });

  it("renders header + ledger hero + features + a single footer", async () => {
    await renderAsyncPage();
    // Brand appears in both the header and the inverse footer brand.
    expect(screen.getAllByText("Jobbliggaren").length).toBeGreaterThan(0);
    // Features section
    expect(screen.getByText("Funktioner")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        name: "Allt du behöver för att hålla ordning",
      }),
    ).toBeInTheDocument();
    // Exactly one footer landmark (the shared SiteFooter, K3 dedupe).
    expect(screen.getAllByRole("contentinfo")).toHaveLength(1);
    expect(screen.getByText("Om Jobbliggaren")).toBeInTheDocument();
  });

  it("hero <h1> is the crawlable 01/02/03 ledger with real verb text", async () => {
    await renderAsyncPage();
    const heading = screen.getByRole("heading", { level: 1 });
    for (const num of ["01", "02", "03"]) {
      expect(screen.getByText(num)).toBeInTheDocument();
    }
    for (const verb of ["Hitta jobbet.", "Sök jobbet.", "Följ upp ansökan."]) {
      expect(heading).toHaveTextContent(verb);
    }
  });

  it("renders live stats in the header (45 580 active ads)", async () => {
    await renderAsyncPage();
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
  });

  it("mounts the inline AuthCard tablist with the register tab live by default", async () => {
    await renderAsyncPage();
    expect(
      screen.getByRole("tablist", { name: "Logga in eller skapa konto" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("tab", { name: "Skapa konto" }),
    ).toHaveAttribute("aria-selected", "true");
    // The live RegisterForm submit button (role=button) is distinct from the tab.
    expect(
      screen.getByRole("button", { name: "Skapa konto" }),
    ).toBeInTheDocument();
  });

  it("exposes a skip link to #main", async () => {
    await renderAsyncPage();
    const skip = screen.getByRole("link", { name: "Hoppa till huvudinnehåll" });
    expect(skip).toHaveAttribute("href", "#main");
  });

  it("has NO waitlist CTA and NO product-peek (replaced by the ledger + AuthCard)", async () => {
    await renderAsyncPage();
    expect(
      screen.queryByRole("button", { name: /Anmäl till väntelista/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("A-2841")).not.toBeInTheDocument();
  });

  it("propagates the next deep-link param through the mounted AuthCard", async () => {
    searchParamsRef.current = new URLSearchParams("next=/cv");
    const { container } = await renderAsyncPage();
    // RegisterForm (the live default tab) carries a hidden next field seeded
    // from the query string — the on-page card preserves deep-link intent.
    const nextField = container.querySelector('input[name="next"]');
    expect(nextField).not.toBeNull();
    expect(nextField).toHaveValue("/cv");
  });
});
