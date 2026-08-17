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

  /**
   * `ledeLines` exists because this skeleton reserved ONE lede line while the real ledes on the
   * CV routes wrap to two or three, so the band grew when the stream landed — measured 63px on
   * `/cv/granska/[parsedId]` and 38px on `/cv` (#1062, design-reviewer M-A).
   *
   * These pin the mechanism, because a fix for an untested guarantee must itself be tested: if
   * the prop regressed to always-1 the jump would come back and nothing else in the suite would
   * notice. The bar COUNT is the thing that reserves the height; the last bar's narrower width
   * only matches how a wrapped paragraph actually looks.
   */
  it("reserves one lede line by default, so existing call sites are unchanged", () => {
    const { container } = render(<PageHeroSkeleton />);
    const main = container.querySelector(".jp-pagehero__main");
    // Title + one lede bar.
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(2);
  });

  it.each([
    [2, 3],
    [3, 4],
  ] as const)("reserves %i lede lines when asked", (ledeLines, expectedBars) => {
    const { container } = render(<PageHeroSkeleton ledeLines={ledeLines} />);
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(expectedBars);
  });

  it("narrows only the LAST lede bar, and only when the lede wraps", () => {
    const single = render(<PageHeroSkeleton />).container;
    const singleBars = [...single.querySelectorAll(".jp-pagehero__main .jp-skeleton")];
    // A one-line lede is not a wrapped paragraph, so its only bar keeps full width.
    expect(singleBars.at(-1)?.className).toContain("w-96");

    const wrapped = render(<PageHeroSkeleton ledeLines={3} />).container;
    const bars = [...wrapped.querySelectorAll(".jp-pagehero__main .jp-skeleton")].slice(1);
    expect(bars.slice(0, -1).every((b) => b.className.includes("w-96"))).toBe(true);
    expect(bars.at(-1)?.className).toContain("w-64");
  });
});
