import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { SkipLink } from "./skip-link";

describe("SkipLink (shared, LP-5b #259 / #284 fold-in)", () => {
  it("renders a link to #main with the provided label", () => {
    render(<SkipLink label="Hoppa till huvudinnehåll" />);
    const link = screen.getByRole("link", { name: "Hoppa till huvudinnehåll" });
    expect(link).toHaveAttribute("href", "#main");
  });

  it("is visually hidden until focused (sr-only / focus:not-sr-only)", () => {
    render(<SkipLink label="Skip to main content" />);
    const link = screen.getByRole("link", { name: "Skip to main content" });
    expect(link).toHaveClass("sr-only");
    expect(link).toHaveClass("focus:not-sr-only");
  });

  it("renders the link itself as its root node (first-focusable when placed first)", () => {
    const { container } = render(<SkipLink label="Hoppa till huvudinnehåll" />);
    expect(container.firstChild).toBe(
      screen.getByRole("link", { name: "Hoppa till huvudinnehåll" }),
    );
  });
});
