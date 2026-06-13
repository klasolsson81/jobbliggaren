import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LandingTopbar } from "./landing-topbar";
import type { LandingStats } from "./landing-stats-format";

// Mock-värden — efter ADR 0064 hämtas riktiga värden från `/api/v1/landing/stats`
// och passeras som prop från <LandingPage />. Topbar:n är ren rendering;
// dessa tester verifierar visuell shape + civic-utility-disciplin, inte fetch:en.
const STATS_MOCK: LandingStats = { activeCount: 45_580, newToday: 312 };

describe("LandingTopbar (F6 Prompt 1)", () => {
  it("renderar brand + två stats-block", () => {
    render(<LandingTopbar stats={STATS_MOCK} />);
    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("formaterar stora siffror med svensk locale (45 580)", () => {
    render(<LandingTopbar stats={STATS_MOCK} />);
    // sv-SE använder U+00A0 (non-breaking space) som tusentalsseparator —
    // testet använder regex för att matcha både vanlig och nbsp-variant.
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    expect(screen.getByText("312")).toBeInTheDocument();
  });

  it("har 'Logga in'-LÄNK (ej knapp) till /logga-in, INGEN 'Skapa konto' (G4)", () => {
    render(<LandingTopbar stats={STATS_MOCK} />);
    // G4-redesign: login flyttad från AuthCard till topbar som <a>-länk.
    // Navigering ska vara <a href>, aldrig <button> (a11y).
    expect(
      screen.queryByRole("button", { name: /Logga in/i }),
    ).not.toBeInTheDocument();
    const loginLink = screen.getByRole("link", { name: /Logga in/i });
    expect(loginLink).toHaveAttribute("href", "/logga-in");
    // Closed beta — ingen registrering från topbar.
    expect(
      screen.queryByRole("button", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
  });

  it("INNEHÅLLER INGA theme/lang-toggles (HANDOVER §0 punkt 7)", () => {
    render(<LandingTopbar stats={STATS_MOCK} />);
    expect(
      screen.queryByRole("button", { name: /tema|theme|mörk|ljus/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: /Språk|Language/i }),
    ).not.toBeInTheDocument();
  });

  it("brand-länken pekar mot landing (/)", () => {
    render(<LandingTopbar stats={STATS_MOCK} />);
    expect(
      screen.getByRole("link", { name: /Jobbliggaren/ }),
    ).toHaveAttribute("href", "/");
  });
});
