import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import LandingPage from "@/app/(marketing)/page";

vi.mock("@/lib/auth/actions", () => ({
  loginAction: vi.fn(),
  registerAction: vi.fn(),
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
    expect(screen.getAllByText("JobbPilot").length).toBeGreaterThan(0);
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
    expect(screen.getByText("Om JobbPilot")).toBeInTheDocument();
  });

  it("hero CTA 'Skapa konto' flippar AuthCard till register-tab", async () => {
    await renderAsyncPage();
    const heroSkapaKonto = screen.getAllByRole("button", {
      name: /Skapa konto/i,
    })[0];
    fireEvent.click(heroSkapaKonto!);
    // Register-form-text-fragment ska finnas (datapolicy-disclaimer)
    expect(screen.getByText(/datapolicy/i)).toBeInTheDocument();
  });

  it("INGEN Sparkles/AI-trope, INGEN Drift-indikator, INGEN Version-kicker (HANDOVER §7.1 Bort:)", async () => {
    await renderAsyncPage();
    expect(screen.queryByText(/Drift/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Version 2/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Så funkar det/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Sparkles/i)).not.toBeInTheDocument();
  });

  it("INGEN inloggningsknapp i topbar (HANDOVER §6.4)", async () => {
    await renderAsyncPage();
    // Topbar har ingen <button> med login/skapa-konto-text — bara brand-link.
    // Login-knappen i AuthCard SKA finnas, så vi verifierar att topbar-
    // containern är fri:
    const topbar = document.querySelector(".jp-land-top");
    expect(topbar).not.toBeNull();
    expect(topbar?.querySelector("button")).toBeNull();
  });

  it("renderar 4 funktioner med mono-key + text", async () => {
    await renderAsyncPage();
    expect(screen.getByText("Sökning")).toBeInTheDocument();
    expect(screen.getByText("Pipeline")).toBeInTheDocument();
    expect(screen.getByText("CV och brev")).toBeInTheDocument();
    expect(screen.getByText("Påminnelser")).toBeInTheDocument();
  });
});
