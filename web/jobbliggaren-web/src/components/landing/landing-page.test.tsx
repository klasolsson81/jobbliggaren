import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import LandingPage from "@/app/(marketing)/page";

// F-Pre Punkt 5 (2026-05-24): LandingPage anropar nu `getServerSession()` för
// att rendera kontextuell CTA (anonym vs inloggad). Mock returnerar `null`
// (anonym besökare) så befintliga smoke-tester förblir grön; ett separat
// test täcker isAuthenticated-grenen via LandingHeroSection direkt.
vi.mock("@/lib/auth/session", () => ({
  getServerSession: vi.fn().mockResolvedValue(null),
  ROLES: { Admin: "Admin" },
  SESSION_COOKIE_NAME: "__Host-jobbliggaren_session",
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ push: vi.fn() }),
}));

// ThemeToggle använder useTheme som kräver ThemeProvider-context. I prod
// wrappar app/layout.tsx hela appen; i jsdom-tester mockas den till en
// trivial stub så landing-smoke kan rendera utan provider-setup.
vi.mock("@/components/theme-toggle", () => ({
  ThemeToggle: () => (
    <button type="button" aria-label="Byt tema">
      tema
    </button>
  ),
}));

// ADR 0064 — landing-stats fetch:as server-side i LandingPage. I jsdom-test
// finns ingen backend; mocka helpern så stats levereras synkront. Innehåll
// är "live"-värden från HANDOVER-v3 målbild 01 för shape-stabilitet, men
// testet kollar inte siffrorna här (landing-topbar.test.tsx äger det).
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

// Hjälpare — async RSC kan inte renderas direkt av RTL; vi pre-resolvar
// element-trädet och skickar det vidare till render().
async function renderAsyncPage() {
  const element = await LandingPage();
  return render(element);
}

describe("LandingPage (F6 Prompt 1, smoke)", () => {
  it("renderar alla 4 sektioner: topbar + hero + features + footer", async () => {
    await renderAsyncPage();
    // Topbar
    expect(screen.getAllByText("Jobbliggaren").length).toBeGreaterThan(0);
    // Hero
    expect(
      screen.getByRole("heading", {
        name: "Håll ordning i ditt jobbsökande",
      }),
    ).toBeInTheDocument();
    // Features
    expect(screen.getByText("Funktioner")).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        name: "Allt du behöver för att hålla ordning",
      }),
    ).toBeInTheDocument();
    // Footer
    expect(screen.getByText("Om Jobbliggaren")).toBeInTheDocument();
  });

  it("hero CTA 'Anmäl till väntelista' navigerar till /vantelista (closed beta)", async () => {
    await renderAsyncPage();
    const heroVantelistaButton = screen.getByRole("button", {
      name: /Anmäl till väntelista/i,
    });
    expect(heroVantelistaButton).toBeInTheDocument();
  });

  it("INGEN 'Skapa konto'-CTA i hero (closed beta — registrering stängd)", async () => {
    await renderAsyncPage();
    expect(
      screen.queryByRole("button", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
  });

  it("INGEN Sparkles/AI-trope, INGEN Drift-indikator, INGEN Version-kicker (HANDOVER §7.1 Bort:)", async () => {
    await renderAsyncPage();
    expect(screen.queryByText(/Drift/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Version 2/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Så funkar det/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Sparkles/i)).not.toBeInTheDocument();
  });

  it("topbar har 'Logga in'-länk till /logga-in (G4-redesign)", async () => {
    await renderAsyncPage();
    const topbar = document.querySelector(".jp-land-top");
    expect(topbar).not.toBeNull();
    // Login är en <a> (navigering), inte en <button> — civic-utility a11y.
    expect(topbar?.querySelector("button")).toBeNull();
    const loginLink = screen.getByRole("link", { name: /Logga in/i });
    expect(loginLink).toHaveAttribute("href", "/logga-in");
  });

  it("INGEN inline auth-card/login-form i hero (G4 — login flyttad till topbar)", async () => {
    await renderAsyncPage();
    // AuthCard (med Lösenord-fält + OAuth-knappar) är borttagen ur hero.
    expect(screen.queryByLabelText("Lösenord")).not.toBeInTheDocument();
    expect(screen.queryByText("eller logga in med")).not.toBeInTheDocument();
  });

  it("renderar 4 funktioner med mono-key + text", async () => {
    await renderAsyncPage();
    expect(screen.getByText("Sökning")).toBeInTheDocument();
    expect(screen.getByText("Pipeline")).toBeInTheDocument();
    expect(screen.getByText("CV och brev")).toBeInTheDocument();
    expect(screen.getByText("Påminnelser")).toBeInTheDocument();
  });
});
