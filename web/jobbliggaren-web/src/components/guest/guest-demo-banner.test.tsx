import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestDemoBanner } from "./guest-demo-banner";

describe("<GuestDemoBanner />", () => {
  it("renderar DEMO-etikett + civic-utility-text + väntelista-CTA", () => {
    render(<GuestDemoBanner />);
    expect(screen.getByText("DEMO")).toBeInTheDocument();
    expect(
      screen.getByText(/utforskar Jobbliggaren som gäst/i)
    ).toBeInTheDocument();
    const cta = screen.getByRole("link", { name: /anmäl till väntelistan/i });
    expect(cta).toHaveAttribute("href", "/vantelista");
  });

  it("har region-roll med svenskt aria-label så skärmläsare annonserar demoläget", () => {
    render(<GuestDemoBanner />);
    expect(screen.getByRole("region", { name: "Demoläge" })).toBeInTheDocument();
  });

  it("innehåller inget utropstecken eller emoji (civic-utility-disciplin)", () => {
    const { container } = render(<GuestDemoBanner />);
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/!/);
    // No emoji range U+1F300–U+1FAFF + supplementary symbols
    expect(text).not.toMatch(
      /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/u
    );
  });
});
