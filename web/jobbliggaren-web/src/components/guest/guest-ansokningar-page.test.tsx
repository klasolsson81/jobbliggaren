import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestAnsokningarPage } from "./guest-ansokningar-page";

/**
 * #1516 — the guest pipeline's time column renders the product's form.
 *
 * This is the surface the defect was measured on: seven rows in ONE column —
 * two carrying `idag`/`igår`, four carrying `för N dagar sedan`, and one
 * carrying `för 1 vecka sedan`, which the catalogue cannot express. The first
 * assertion is that defect expressed as a test — all three forms in one view.
 * A per-string form check would not have caught it: every single string was
 * well-formed Swedish, and it was the combination that was wrong.
 */
describe("GuestAnsokningarPage — relative time (#1516)", () => {
  it("renders the catalogue's three forms in one view", () => {
    render(<GuestAnsokningarPage />);

    // ga-3 (0 days), ga-1 (1 day), ga-2 (3 days) against the mock's frozen now.
    expect(screen.getByText("idag")).toBeInTheDocument();
    expect(screen.getByText("igår")).toBeInTheDocument();
    expect(screen.getByText("3 dagar sedan")).toBeInTheDocument();
  });

  it("renders no `för …` form anywhere on the page", () => {
    const { container } = render(<GuestAnsokningarPage />);

    // Deliberately broad: `för` followed by a digit catches both
    // "för 3 dagar sedan" and "för 1 vecka sedan". No word boundary — see
    // guest-cv-page.test.tsx for the measurement that settled that.
    expect(container.textContent).not.toMatch(/för \d/);
  });

  it("expresses the seven-day interval in days, not weeks", () => {
    // ga-6 carried "för 1 vecka sedan", which the catalogue plural cannot say.
    render(<GuestAnsokningarPage />);

    expect(screen.getByText("7 dagar sedan")).toBeInTheDocument();
    expect(screen.queryByText(/vecka/)).not.toBeInTheDocument();
  });

  it("the row's accessible name carries the source and the time", () => {
    // `aria-label` overrides the link's own content, so the meta line reaches a
    // screen reader only if the label names it — the value this PR corrects is
    // otherwise the one missing from the reading (design-reviewer, 2026-08-27).
    render(<GuestAnsokningarPage />);

    expect(
      screen.getByRole("link", {
        name: "Systemutvecklare .NET – Folksam IT – Inskickad – Platsbanken, uppdaterad 3 dagar sedan",
      })
    ).toBeInTheDocument();
  });
});
