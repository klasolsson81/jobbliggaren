import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import Loading from "./loading";

// The harness aliases `@testing-library/react` to a render shim that wraps every tree in
// NextIntlClientProvider (messages/sv), so this fallback's `useTranslations("pages")`
// resolves without a manual provider (see vitest.config.ts / src/test/render-intl.tsx).
//
// ⚠ These pin the fallback's STRUCTURE — the class contract it shares with page.tsx —
// and deliberately not its rendered height: jsdom loads no CSS, so a computed-height
// assertion here would measure nothing. The height itself is verified rendered, with
// JavaScript disabled so the Suspense fallback stays in the DOM (#1467's own method).

describe("/ansokningar loading fallback (#1467)", () => {
  it("reserves the aside with the page's own --stacked modifier", () => {
    const { container } = render(<Loading />);
    const aside = container.querySelector(".jp-pagehero__aside");
    // page.tsx composes exactly these two class names on this element; without the
    // modifier the rows lay out as a wrapping row and the band loses a whole row.
    expect(aside).toHaveClass("jp-pagehero__aside", "jp-pagehero__aside--stacked");
  });

  it("reserves both button rows, with the page's three controls across them", () => {
    const { container } = render(<Loading />);
    const rows = container.querySelectorAll(
      ".jp-pagehero__aside .jp-pagehero__btnrow"
    );
    expect(rows).toHaveLength(2);
    expect(rows[0]?.querySelectorAll(".jp-skeleton")).toHaveLength(1);
    expect(rows[1]?.querySelectorAll(".jp-skeleton")).toHaveLength(2);
  });

  /**
   * The defect this closes was a height error before it was anything else: every aside bar
   * stood at `h-10` (40px) against `.jp-btn { height: 44px }`. Asserting `h-11` alone would
   * pass on a file that also still carried an `h-10` bar, so the negative half is the one
   * that crosses the threshold.
   */
  it("sizes every aside bar at the button height, and none at the old one", () => {
    const { container } = render(<Loading />);
    const bars = container.querySelectorAll(".jp-pagehero__aside .jp-skeleton");
    expect(bars).toHaveLength(3);
    for (const bar of bars) {
      expect(bar).toHaveClass("h-11");
      expect(bar).not.toHaveClass("h-10");
    }
  });

  /**
   * The fallback used to draw the secondary actions as a right-aligned row BELOW the hero.
   * page.tsx renders no such row — all three controls live in the aside — so that block
   * over-reserved beneath the band while the band itself under-reserved above it.
   */
  it("draws no secondary-action row below the hero, because the page has none", () => {
    const { container } = render(<Loading />);
    const belowHero = container.querySelector(".jp-container.jp-page");
    expect(belowHero).not.toBeNull();
    // Positive control first: the ledger section IS still reserved down there, so this is
    // not measuring a container that failed to render.
    expect(belowHero?.querySelector(".jp-section")).not.toBeNull();
    expect(belowHero?.querySelector(".justify-end")).toBeNull();
  });

  it("renders the page's real title and lede, so the browser wraps them identically", () => {
    const { container } = render(<Loading />);
    const main = container.querySelector(".jp-pagehero__main");
    expect(main?.querySelector("h1.jp-pagehero__title")?.textContent).toBe(
      "Mina ansökningar"
    );
    expect(main?.querySelector("p.jp-pagehero__lede")?.textContent).toBe(
      "Pipeline över alla ansökningar. Klicka på en rad för detaljer."
    );
    // Both bars are replaced, not joined — a bar left beside the real text would put the
    // band back where it started.
    expect(main?.querySelectorAll(".jp-skeleton")).toHaveLength(0);
  });

  it("announces the load once, and keeps the visual shape decorative", () => {
    const { container } = render(<Loading />);
    const status = container.querySelectorAll('[role="status"]');
    expect(status).toHaveLength(1);
    expect(status[0]).toHaveClass("sr-only");
    expect(container.querySelector(".jp-pagehero")).toHaveAttribute(
      "aria-hidden",
      "true"
    );
    // The real page's h1 is the document's heading; the band's must not be exposed as a
    // second one while both are briefly in the DOM mid-swap.
    expect(
      container.querySelector(".jp-pagehero[aria-hidden='true'] h1")
    ).not.toBeNull();
  });
});
