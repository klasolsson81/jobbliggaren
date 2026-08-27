import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestCvPage } from "./guest-cv-page";

/**
 * #1516 — CV-gridens `Uppdaterad`-rad renderar produktens form.
 *
 * Samma kolumn-blandning som pipelinen: gr-1 bar `idag`, gr-2 och gr-3 bar
 * `för N dagar sedan`.
 */
describe("GuestCvPage — relativ tid (#1516)", () => {
  it("renderar katalogens former i Uppdaterad-raden", () => {
    render(<GuestCvPage />);

    // gr-1 (0 dagar), gr-2 (2 dagar), gr-3 (5 dagar).
    expect(screen.getByText("idag")).toBeInTheDocument();
    expect(screen.getByText("2 dagar sedan")).toBeInTheDocument();
    expect(screen.getByText("5 dagar sedan")).toBeInTheDocument();
  });

  it("renderar ingen `för …`-form någonstans på sidan", () => {
    const { container } = render(<GuestCvPage />);

    expect(container.textContent).not.toMatch(/\bför \d/);
  });
});
