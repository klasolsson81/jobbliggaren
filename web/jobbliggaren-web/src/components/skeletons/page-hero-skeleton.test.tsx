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

  it("reserves one lede line by default, so existing call sites are unchanged", () => {
    const { container } = render(<PageHeroSkeleton />);
    const main = container.querySelector(".jp-pagehero__main");
    // Title + one lede bar.
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(2);
  });

  /**
   * `null` means "this page has no aside", which an empty node cannot express. The
   * distinction is load-bearing rather than tidy: `.jp-pagehero__inner` is a wrapping flex
   * row, so an EMPTY `__aside` is free beside `__main` at wide widths and costs a whole
   * line plus the row gap once it wraps — the band then over-reserved only at narrow
   * viewports, which is where a rendered check is least likely to look (#1385).
   */
  it("renders NO aside element when aside is null", () => {
    const { container } = render(<PageHeroSkeleton aside={null} />);
    // Positive first: the band and its main column are still there, so this is not
    // measuring a component that failed to render.
    expect(container.querySelector(".jp-pagehero__main")).not.toBeNull();
    expect(container.querySelector(".jp-pagehero__aside")).toBeNull();
  });

  /**
   * A bar can only approximate a paragraph, and a fallback that approximates its own page
   * disagrees with it at every width it was not tuned for (#1385). A pagehero's title and
   * lede are static translations, so rendering them for real hands the wrapping to the
   * browser and the band cannot disagree at all.
   *
   * These pin the substitution, in both directions — a prop that silently rendered a bar
   * *as well* would put the band back where it started, and nothing else in the suite looks.
   */
  it("renders the real title element instead of the title bar when `title` is given", () => {
    const { container } = render(<PageHeroSkeleton title="Importera CV" />);
    const main = container.querySelector(".jp-pagehero__main");
    const title = main?.querySelector("h1.jp-pagehero__title");
    expect(title?.textContent).toBe("Importera CV");
    // The bar it replaces is gone, not merely joined: only the lede bar is left.
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(1);
  });

  it("renders the real lede element instead of the lede bars when `lede` is given", () => {
    const { container } = render(<PageHeroSkeleton lede="Ladda upp ditt CV." />);
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelector("p.jp-pagehero__lede")?.textContent).toBe(
      "Ladda upp ditt CV.",
    );
    // Only the title bar is left.
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(1);
  });

  it("leaves no bar in __main once both title and lede are real", () => {
    const { container } = render(
      <PageHeroSkeleton title="Granskning av ditt CV" lede="En granskning." />,
    );
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(0);
    expect(main?.querySelectorAll("h1.jp-pagehero__title")).toHaveLength(1);
    expect(main?.querySelectorAll("p.jp-pagehero__lede")).toHaveLength(1);
  });

  it("keeps the band decorative even when it carries a real heading", () => {
    // The h1 is real markup inside an aria-hidden band: the announce stays owned by the
    // route's sr-only role="status", so the fallback must not expose a second heading.
    const { container } = render(<PageHeroSkeleton title="CV" lede="Hantera dina CV." />);
    expect(container.querySelector(".jp-pagehero")).toHaveAttribute("aria-hidden", "true");
  });
});
