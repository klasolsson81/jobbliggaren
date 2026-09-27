import { describe, it, expect } from "vitest";
import { anchoredDialogStyle, clampAnchoredTop } from "./anchored-top";

describe("anchoredDialogStyle", () => {
  it("anchors the top and centres on X, overriding the primitive's translate", () => {
    const style = anchoredDialogStyle(120);
    expect(style).toMatchObject({ top: "120px" });
    expect(String(style?.translate)).toMatch(/^-50% /);
  });

  it("moves the dialog up by whatever of it would pass the bottom gutter", () => {
    expect(anchoredDialogStyle(588)).toMatchObject({
      translate: "-50% min(0px, calc(100dvh - 16px - 588px - 100%))",
    });
  });

  it("scrolls inside only a dialog taller than the room between the gutters", () => {
    expect(anchoredDialogStyle(588)).toMatchObject({
      maxHeight: "calc(100dvh - 32px)",
      overflowY: "auto",
    });
  });

  it("never sets a transform, which would compose with the primitive's translate (#1850)", () => {
    expect(anchoredDialogStyle(16)).not.toHaveProperty("transform");
  });

  it("leaves the primitive's centred placement alone without an anchor", () => {
    expect(anchoredDialogStyle(null)).toBeUndefined();
    expect(anchoredDialogStyle(undefined)).toBeUndefined();
  });
});

describe("clampAnchoredTop", () => {
  const viewportHeight = 900; // upperBound = 900 - 16 - 240 = 644

  it("positions the top 170px above the click in the middle band", () => {
    expect(clampAnchoredTop(500, viewportHeight)).toBe(330); // 500 - 170
  });

  it("clamps to the top gutter when the click is near the top", () => {
    expect(clampAnchoredTop(50, viewportHeight)).toBe(16); // 50 - 170 = -120 -> gutter
  });

  it("clamps to the bottom bound when the click is near the bottom", () => {
    expect(clampAnchoredTop(1100, viewportHeight)).toBe(644); // 1100 - 170 = 930 -> upperBound
  });

  it("keeps top >= gutter even in a very short viewport", () => {
    expect(clampAnchoredTop(300, 100)).toBe(16); // upperBound collapses to gutter
  });
});
