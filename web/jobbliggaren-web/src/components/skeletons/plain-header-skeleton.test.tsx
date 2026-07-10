import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { PlainHeaderSkeleton } from "./plain-header-skeleton";

describe("PlainHeaderSkeleton", () => {
  it("announces loading via a polite sr-only status region with the given label", () => {
    render(<PlainHeaderSkeleton label="Matchningarna läses in…" />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(status).toHaveTextContent("Matchningarna läses in…");
  });

  it("renders a plain header (no jp-pagehero band) so a bandless page does not flash one", () => {
    const { container } = render(<PlainHeaderSkeleton label="…" />);
    expect(container.querySelector(".jp-pagehero")).toBeNull();
    // Title + lede placeholder bars are present.
    expect(container.querySelectorAll(".jp-skeleton").length).toBeGreaterThan(1);
  });

  it("renders bare by default (shell supplies the container)", () => {
    const { container } = render(<PlainHeaderSkeleton label="…" />);
    expect(container.querySelector(".jp-container")).toBeNull();
  });

  it("wraps in .jp-container.jp-page when contained (V3-native routes)", () => {
    const { container } = render(<PlainHeaderSkeleton label="…" contained />);
    expect(container.querySelector(".jp-container.jp-page")).not.toBeNull();
  });

  it("hides the decorative shape from assistive tech", () => {
    const { container } = render(<PlainHeaderSkeleton label="…" />);
    expect(
      container.querySelector("[aria-hidden='true']")
    ).not.toBeNull();
  });

  it("renders no global id (safe to render alongside the real page mid-swap)", () => {
    const { container } = render(<PlainHeaderSkeleton label="…" />);
    expect(container.querySelector("[id]")).toBeNull();
  });
});
