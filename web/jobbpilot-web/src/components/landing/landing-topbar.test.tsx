import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LandingTopbar } from "./landing-topbar";

describe("LandingTopbar (F6 Prompt 1)", () => {
  it("renderar brand + två stats-block", () => {
    render(<LandingTopbar />);
    expect(screen.getByText("JobbPilot")).toBeInTheDocument();
    expect(screen.getByText("aktiva annonser")).toBeInTheDocument();
    expect(screen.getByText("nya idag")).toBeInTheDocument();
  });

  it("formaterar stora siffror med svensk locale (45 580)", () => {
    render(<LandingTopbar />);
    // sv-SE använder U+00A0 (non-breaking space) som tusentalsseparator —
    // testet använder regex för att matcha både vanlig och nbsp-variant.
    expect(screen.getByText(/45[\s ]580/)).toBeInTheDocument();
    expect(screen.getByText("312")).toBeInTheDocument();
  });

  it("INNEHÅLLER INGA inloggningsknappar (HANDOVER §6.4)", () => {
    render(<LandingTopbar />);
    expect(
      screen.queryByRole("button", { name: /Logga in/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
  });

  it("INNEHÅLLER INGA theme/lang-toggles (HANDOVER §0 punkt 7)", () => {
    render(<LandingTopbar />);
    expect(
      screen.queryByRole("button", { name: /tema|theme|mörk|ljus/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("group", { name: /Språk|Language/i }),
    ).not.toBeInTheDocument();
  });

  it("brand-länken pekar mot landing (/)", () => {
    render(<LandingTopbar />);
    expect(
      screen.getByRole("link", { name: /JobbPilot/ }),
    ).toHaveAttribute("href", "/");
  });
});
