import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApplicationDetailSkeleton } from "./application-detail-skeleton";

describe("ApplicationDetailSkeleton (#1827 M6)", () => {
  it("announces loading via a polite sr-only status region with the given label", () => {
    render(<ApplicationDetailSkeleton label="Ansökan läses in…" />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(status).toHaveTextContent("Ansökan läses in…");
  });

  it("draws the one body's shape inside the full page's envelope", () => {
    const { container } = render(<ApplicationDetailSkeleton label="…" />);
    const modal = container.querySelector(".jp-container.jp-page .jp-modal");
    expect(modal).not.toBeNull();
    expect(modal?.querySelector(".jp-modal__head")).not.toBeNull();
    expect(modal?.querySelector(".jp-modal__foot")).not.toBeNull();

    const body = modal?.querySelector(".jp-modal__body");
    expect(body?.querySelector(".jp-status-block")).not.toBeNull();
    // The primary CTA, the seven steps and the park row's three buttons.
    const actions = body?.querySelector(".jp-drawer-actions");
    expect(actions?.querySelectorAll(".h-11.w-full")).toHaveLength(1);
    expect(actions?.querySelectorAll(".size-6")).toHaveLength(7);
    expect(actions?.querySelectorAll(".flex-1")).toHaveLength(3);
  });

  it("puts the back link's block above the panel", () => {
    const { container } = render(<ApplicationDetailSkeleton label="…" />);
    const envelope = container.querySelector("[aria-hidden='true']");
    expect(envelope?.firstElementChild).toHaveClass("jp-skeleton");
    expect(envelope?.children[1]).toHaveClass("jp-modal");
  });

  it("hides only the decorative blocks from assistive tech", () => {
    const { container } = render(<ApplicationDetailSkeleton label="…" />);
    expect(container.querySelector(".jp-modal")?.closest("[aria-hidden='true']")).not.toBeNull();
    expect(screen.getByRole("status").closest("[aria-hidden='true']")).toBeNull();
  });

  it("renders no global id (safe to render alongside the real page mid-swap)", () => {
    const { container } = render(<ApplicationDetailSkeleton label="…" />);
    expect(container.querySelector("[id]")).toBeNull();
  });
});
