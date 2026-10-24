import { describe, it, expect } from "vitest";
import {
  placePanel,
  PANEL_GAP,
  VIEWPORT_GUTTER,
  type PanelPlacement,
} from "./placement";

/**
 * Every trigger box and viewport below is what the layout engine produced on /jobb and
 * /foretag/sok, measured 2026-10-24 on 5ad51c9c1 (headless Chromium, sv, light, unselected pills,
 * a stated occupation so the Matchning pill renders). `classic scrollbar` rows come from the same
 * build with Chromium's scrollbars shown, where the document element is 15px narrower and 15px
 * shorter than the window. The scrolled /foretag/sok trigger was measured the same day on the
 * branch, after the reveal scrolled the page to 517.
 */
const at = (clientWidth: number, clientHeight: number, scrollY = 0) => ({
  clientWidth,
  clientHeight,
  scrollX: 0,
  scrollY,
});

function anchored(placed: PanelPlacement) {
  if (placed.placement !== "anchored") throw new Error(`expected an anchored panel, got ${placed.placement}`);
  return placed;
}

describe("placePanel: an anchored panel stays inside the viewport (#2212)", () => {
  it.each([
    // [what, trigger box, viewport, preferred width, expected left]
    ["Ort at 1280x720 fits where it is", { left: 664, bottom: 387.421875 }, at(1280, 720), 580, 664],
    ["Yrke at 1280x720 moves left to keep 16px", { left: 745.078125, bottom: 387.421875 }, at(1280, 720), 580, 684],
    ["Yrke at 1280x720 with a classic scrollbar", { left: 737.578125, bottom: 387.421875 }, at(1265, 705), 580, 669],
    ["Yrke at 1366x768 with a classic scrollbar", { left: 780.578125, bottom: 387.421875 }, at(1351, 753), 580, 755],
    ["Yrke at 1024x768", { left: 617.078125, bottom: 455.5625 }, at(1024, 768), 580, 428],
    ["Ort at 1024x768", { left: 536, bottom: 455.5625 }, at(1024, 768), 580, 428],
    ["Matchning at 1024x768", { left: 796.46875, bottom: 455.5625 }, at(1024, 768), 320, 688],
    ["Filter at 800x600", { left: 591.921875, bottom: 475.703125 }, at(800, 600), 320, 464],
    ["/foretag/sok Ort at 768x1024", { left: 376, bottom: 669.984375 }, at(768, 1024), 580, 172],
    ["Yrke at 700x900", { left: 125.078125, bottom: 478.609375 }, at(700, 900), 580, 104],
    ["Matchning at 1920x1080 fits where it is", { left: 1244.46875, bottom: 387.421875 }, at(1920, 1080), 320, 1244.46875],
  ] as const)("%s", (_what, trigger, viewport, preferred, left) => {
    const placed = anchored(placePanel(trigger, viewport, preferred));

    expect(placed.left).toBe(left);
    expect(placed.width).toBe(preferred);
    expect(placed.left).toBeGreaterThanOrEqual(VIEWPORT_GUTTER);
    expect(placed.left + placed.width).toBeLessThanOrEqual(viewport.clientWidth - VIEWPORT_GUTTER);
  });

  it("puts the panel 8px under its trigger, in document coordinates", () => {
    // The /foretag/sok Ort trigger at 1280x720 with the page at the top, then with the page
    // scrolled to 517, where the layout engine reported its top at 104.984375 (its height is 48).
    const atTop = anchored(placePanel({ left: 416, bottom: 669.984375 }, at(1280, 720), 580));
    const scrolled = anchored(placePanel({ left: 416, bottom: 152.984375 }, at(1280, 720, 517), 580));

    expect(atTop.top).toBe(669.984375 + PANEL_GAP);
    expect(scrolled.top).toBe(atTop.top);
  });

  it("keeps both gutters for a panel wider than the viewport allows, a width no host asks for", () => {
    // Unreachable for today's hosts: their widest preference, 580px, fits between the gutters at
    // every anchored width (641 - 32 = 609). Asserted only as a safe degradation.
    const viewport = at(1024, 768);
    const placed = anchored(placePanel({ left: 536, bottom: 455.5625 }, viewport, 1200));

    expect(placed.width).toBeGreaterThan(0);
    expect(placed.left).toBeGreaterThanOrEqual(VIEWPORT_GUTTER);
    expect(placed.left + placed.width).toBeLessThanOrEqual(viewport.clientWidth - VIEWPORT_GUTTER);
  });

  it("never starts left of the gutter, a trigger position no host produces", () => {
    // Unreachable: at anchored widths every trigger starts at least 20px in (the page padding).
    // Asserted only as a safe degradation.
    expect(anchored(placePanel({ left: 0, bottom: 387.421875 }, at(1280, 720), 580)).left).toBeGreaterThanOrEqual(
      VIEWPORT_GUTTER,
    );
  });

  it("degrades to the trigger's own left edge when the document has no layout", () => {
    // jsdom only: it reports a zero-size viewport and zero rects, which no browser produces for a
    // rendered page. The panel must not be narrowed to a negative width, pushed off its trigger,
    // or made a sheet.
    for (const viewport of [at(0, 0), at(0, 768), at(1024, 0)]) {
      const placed = placePanel({ left: 0, bottom: 0 }, viewport, 580);

      expect(placed).toEqual({ placement: "anchored", top: PANEL_GAP, left: 0, width: 580 });
    }
  });
});

describe("placePanel: a sheet on a narrow or short viewport (#2212)", () => {
  it.each([
    // [what, trigger box, viewport, expected placement]
    ["the Yrke pill at 640x900 (1280px at 200% zoom)", { left: 125.078125, bottom: 478.609375 }, at(640, 900), "sheet"],
    ["the Yrke pill at 641x900", { left: 125.078125, bottom: 478.609375 }, at(641, 900), "anchored"],
    ["the Yrke pill at 1280x500", { left: 745.078125, bottom: 387.421875 }, at(1280, 500), "sheet"],
    ["the Yrke pill at 1280x501", { left: 745.078125, bottom: 387.421875 }, at(1280, 501), "anchored"],
    ["the Ort pill at 375x812", { left: 44, bottom: 596.890625 }, at(375, 812), "sheet"],
    ["the Ort pill at 812x375, a landscape phone", { left: 430, bottom: 475.703125 }, at(812, 375), "sheet"],
    ["the Ort pill at 683x384 (1366x768 at 200% zoom)", { left: 44, bottom: 478.609375 }, at(683, 384), "sheet"],
  ] as const)("%s", (_what, trigger, viewport, placement) => {
    expect(placePanel(trigger, viewport, 580).placement).toBe(placement);
  });
});
