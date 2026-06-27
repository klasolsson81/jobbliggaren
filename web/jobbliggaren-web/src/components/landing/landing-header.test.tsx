import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LandingHeader } from "./landing-header";
import type { LandingStats } from "./landing-stats-format";

// Stats arrive as a prop from the server-fetch in <LandingPage/>; the header is
// pure rendering, so these tests assert the visual shape + civic-utility
// discipline, not the fetch. (Replaces the deleted landing-topbar.test.tsx —
// LP-4/#257 swapped the bespoke topbar for the shared `.jp-head` header.)
const STATS_MOCK: LandingStats = { activeCount: 45_580, newToday: 312 };

describe("LandingHeader (LP-4, #257)", () => {
  it("renders the brand + both stats blocks", () => {
    render(<LandingHeader stats={STATS_MOCK} />);
    expect(screen.getByText("Jobbliggaren")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("formats large numbers with the Swedish locale (45 580) and a +delta", () => {
    render(<LandingHeader stats={STATS_MOCK} />);
    // sv-SE uses U+00A0 (nbsp) as the thousands separator — match either form.
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    // newToday is rendered with a leading "+" (delta tint).
    expect(screen.getByText("+312")).toBeInTheDocument();
  });

  it("carries NO account action — no login link, no 'Skapa konto' (the AuthCard owns it)", () => {
    render(<LandingHeader stats={STATS_MOCK} />);
    expect(
      screen.queryByRole("link", { name: /Logga in/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Logga in|Skapa konto/i }),
    ).not.toBeInTheDocument();
  });

  it("brand link points at the landing (/)", () => {
    render(<LandingHeader stats={STATS_MOCK} />);
    expect(
      screen.getByRole("link", { name: /Jobbliggaren/ }),
    ).toHaveAttribute("href", "/");
  });

  it("contains NO theme/lang toggles (HANDOVER §0.7 — they live in the footer)", () => {
    render(<LandingHeader stats={STATS_MOCK} />);
    expect(
      screen.queryByRole("button", { name: /tema|theme|mörk|ljus/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: /Språk|Language/i }),
    ).not.toBeInTheDocument();
  });
});
