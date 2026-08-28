import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestCvPage } from "./guest-cv-page";

/**
 * #1516 — the CV grid's `Uppdaterad` row renders the product's form.
 *
 * Same one-column mix as the pipeline: gr-1 carried `idag`, gr-2 and gr-3
 * carried `för N dagar sedan`.
 */
describe("GuestCvPage — relative time (#1516)", () => {
  it("renders the catalogue's forms in the Uppdaterad row", () => {
    render(<GuestCvPage />);

    // gr-1 (0 days), gr-2 (2 days), gr-3 (5 days).
    expect(screen.getByText("idag")).toBeInTheDocument();
    expect(screen.getByText("2 dagar sedan")).toBeInTheDocument();
    expect(screen.getByText("5 dagar sedan")).toBeInTheDocument();
  });

  it("renders no `för …` form anywhere on the page", () => {
    const { container } = render(<GuestCvPage />);

    // No word boundary, and this surface is why. `textContent` concatenates
    // across element boundaries, so `<dt>Uppdaterad</dt><dd>för 2 dagar
    // sedan</dd>` reads as "Uppdateradför 2 dagar sedan" and `\b` never
    // matches. Measured 2026-08-27: with the boundary, reverting this page to a
    // hardcoded "för 2 dagar sedan" left this assertion green.
    expect(container.textContent).not.toMatch(/för \d/);
  });
});
