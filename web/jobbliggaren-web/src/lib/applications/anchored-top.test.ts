import { describe, it, expect } from "vitest";
import { anchoredDialogStyle, clampAnchoredTop } from "./anchored-top";

describe("anchoredDialogStyle", () => {
  it("anchors the top and keeps only the X centring, overriding the primitive's translate", () => {
    expect(anchoredDialogStyle(120)).toEqual({ top: "120px", translate: "-50% 0" });
  });

  it("never sets a transform, which would compose with the primitive's translate (#1850)", () => {
    expect(anchoredDialogStyle(0)).not.toHaveProperty("transform");
  });

  it("leaves the primitive's centred placement alone without an anchor", () => {
    expect(anchoredDialogStyle(null)).toBeUndefined();
    expect(anchoredDialogStyle(undefined)).toBeUndefined();
  });
});

describe("clampAnchoredTop", () => {
  const viewportHeight = 900; // upperBound = 900 - 16 - 120 = 764

  it("positions the top ~240px above the click in the middle band", () => {
    expect(clampAnchoredTop(500, viewportHeight)).toBe(260); // 500 - 240
  });

  it("clamps to the top gutter when the click is near the top", () => {
    expect(clampAnchoredTop(50, viewportHeight)).toBe(16); // 50 - 240 = -190 -> gutter
  });

  it("clamps to the bottom bound when the click is near the bottom", () => {
    expect(clampAnchoredTop(1100, viewportHeight)).toBe(764); // 1100 - 240 = 860 -> upperBound
  });

  it("keeps top >= gutter even in a very short viewport", () => {
    expect(clampAnchoredTop(300, 100)).toBe(16); // upperBound collapses to gutter
  });

  it("honours custom offset/gutter/minVisible", () => {
    expect(
      clampAnchoredTop(500, 900, { offset: 100, gutter: 24, minVisible: 200 }),
    ).toBe(400); // 500 - 100 = 400, within [24, 900-24-200=676]
  });
});
