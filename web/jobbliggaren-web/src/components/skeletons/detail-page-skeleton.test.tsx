import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { DetailPageSkeleton } from "./detail-page-skeleton";

describe("DetailPageSkeleton", () => {
  it("announces loading via a polite sr-only status region with the given label", () => {
    render(<DetailPageSkeleton label="Jobbannonsen läses in…" />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(status).toHaveTextContent("Jobbannonsen läses in…");
  });

  it("reproduces the .jp-container.jp-page > .jp-modal detail envelope", () => {
    const { container } = render(<DetailPageSkeleton label="…" />);
    expect(container.querySelector(".jp-container.jp-page")).not.toBeNull();
    const modal = container.querySelector(".jp-modal");
    expect(modal).not.toBeNull();
    expect(modal?.querySelector(".jp-modal__head")).not.toBeNull();
    expect(modal?.querySelector(".jp-modal__body")).not.toBeNull();
    expect(modal?.querySelector(".jp-modal__foot")).not.toBeNull();
  });

  it("hides only the decorative modal envelope from assistive tech", () => {
    const { container } = render(<DetailPageSkeleton label="…" />);
    // The visual shape is decorative; the sr-only status carries the message.
    expect(container.querySelector(".jp-modal")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
  });

  it("renders no global id (safe to render alongside the real page mid-swap)", () => {
    const { container } = render(<DetailPageSkeleton label="…" />);
    expect(container.querySelector("[id]")).toBeNull();
  });
});
