import { describe, it, expect } from "vitest";
import { placePanel, PANEL_GAP, VIEWPORT_GUTTER } from "./placement";

/**
 * Every trigger box and viewport below is what the layout engine produced on /jobb and
 * /foretag/sok, measured 2026-10-24 on 5ad51c9c1 (headless Chromium, sv, light, unselected pills,
 * a stated occupation so the Matchning pill renders). `classic scrollbar` rows come from the same
 * build with Chromium's scrollbars shown, where the document element is 15px narrower than the
 * window.
 */
const at = (clientWidth: number, clientHeight: number, scrollY = 0) => ({
  clientWidth,
  clientHeight,
  scrollX: 0,
  scrollY,
});

describe("placePanel: an anchored panel stays inside the viewport (#2212)", () => {
  it.each([
    // [what, trigger.left, viewport, preferred width, expected left]
    ["Ort at 1280x720 fits where it is", 664, at(1280, 720), 580, 664],
    ["Yrke at 1280x720 moves left to keep 16px", 745.078125, at(1280, 720), 580, 684],
    ["Yrke at 1280x720 with a classic scrollbar", 737.578125, at(1265, 720), 580, 669],
    ["Yrke at 1366x768 with a classic scrollbar", 780.578125, at(1351, 768), 580, 755],
    ["Yrke at 1024x768", 617.078125, at(1024, 768), 580, 428],
    ["Ort at 1024x768", 536, at(1024, 768), 580, 428],
    ["Matchning at 1024x768", 796.46875, at(1024, 768), 320, 688],
    ["Filter at 800x600", 591.921875, at(800, 600), 320, 464],
    ["/foretag/sok Ort at 768x1024", 376, at(768, 1024), 580, 172],
    ["Yrke at 700x900", 125.078125, at(700, 900), 580, 104],
    ["Matchning at 1920x1080 fits where it is", 1244.46875, at(1920, 1080), 320, 1244.46875],
  ])("%s", (_what, triggerLeft, viewport, preferred, left) => {
    const placed = placePanel({ left: triggerLeft, bottom: 387.421875 }, viewport, preferred);

    expect(placed.left).toBe(left);
    expect(placed.width).toBe(preferred);
    expect(placed.left).toBeGreaterThanOrEqual(VIEWPORT_GUTTER);
    expect(placed.left + placed.width).toBeLessThanOrEqual(viewport.clientWidth - VIEWPORT_GUTTER);
  });

  it("puts the panel 8px under its trigger, in document coordinates", () => {
    // The Yrke pill at 1280x720 with the page at the top, then with the page scrolled by 200: the
    // layout engine reports the trigger 200px higher, and the panel's document top is the same.
    const atTop = placePanel({ left: 745.078125, bottom: 387.421875 }, at(1280, 720), 580);
    const scrolled = placePanel({ left: 745.078125, bottom: 187.421875 }, at(1280, 720, 200), 580);

    expect(atTop.top).toBe(387.421875 + PANEL_GAP);
    expect(scrolled.top).toBe(atTop.top);
  });

  it("narrows a panel wider than the viewport minus both gutters", () => {
    // The Ort pill at 375x812 (its left measured at 44px). Below 612px of client width a 580px
    // panel cannot keep both gutters, so it narrows and starts at the left gutter.
    const placed = placePanel({ left: 44, bottom: 596.890625 }, at(375, 812), 580);

    expect(placed.width).toBe(375 - 2 * VIEWPORT_GUTTER);
    expect(placed.left).toBe(VIEWPORT_GUTTER);
  });

  it("degrades to the trigger's own left edge when the document has no layout", () => {
    // jsdom only: it reports a zero-size viewport and zero rects, which no browser produces for a
    // rendered page. The panel must not be narrowed to a negative width or pushed off its trigger.
    for (const viewport of [at(0, 0), at(0, 768), at(1024, 0)]) {
      const placed = placePanel({ left: 0, bottom: 0 }, viewport, 580);

      expect(placed).toEqual({ top: PANEL_GAP, left: 0, width: 580 });
    }
  });
});
