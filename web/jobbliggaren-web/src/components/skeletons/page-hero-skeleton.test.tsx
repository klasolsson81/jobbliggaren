import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { PageHeroSkeleton } from "./page-hero-skeleton";

describe("PageHeroSkeleton", () => {
  it("reproduces the shared .jp-pagehero envelope so the swap does not shift", () => {
    const { container } = render(<PageHeroSkeleton />);
    expect(container.querySelector(".jp-pagehero")).not.toBeNull();
    expect(container.querySelector(".jp-pagehero__inner")).not.toBeNull();
    expect(container.querySelector(".jp-pagehero__main")).not.toBeNull();
    expect(container.querySelector(".jp-pagehero__aside")).not.toBeNull();
  });

  it("is decorative — the whole band is hidden from assistive tech", () => {
    const { container } = render(<PageHeroSkeleton />);
    // Announcement is owned by the route loading.tsx (sr-only role=status);
    // the visual shape must not be read out as empty elements.
    expect(container.querySelector(".jp-pagehero")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
  });

  it("renders no global id (safe to render alongside the real page mid-swap)", () => {
    const { container } = render(<PageHeroSkeleton />);
    expect(container.querySelector("[id]")).toBeNull();
  });

  it("renders the default two-action aside when no override is given", () => {
    const { container } = render(<PageHeroSkeleton />);
    const aside = container.querySelector(".jp-pagehero__aside");
    expect(aside?.querySelectorAll(".jp-skeleton")).toHaveLength(2);
  });

  it("renders title + lede only by default (2 bars in __main)", () => {
    const { container } = render(<PageHeroSkeleton />);
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(2);
  });

  it("adds a kicker overline bar above title + lede when kicker is set", () => {
    const { container } = render(<PageHeroSkeleton kicker />);
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(3);
  });

  it("renders a custom aside when provided (e.g. Översikt's card block)", () => {
    const { container } = render(
      <PageHeroSkeleton aside={<span data-testid="today-card" />} />
    );
    const aside = container.querySelector(".jp-pagehero__aside");
    expect(aside?.querySelector("[data-testid='today-card']")).not.toBeNull();
    // The default two-button placeholder is replaced, not appended.
    expect(aside?.querySelectorAll(".jp-skeleton")).toHaveLength(0);
  });
});
