import { afterEach, describe, expect, it, vi } from "vitest";
import { COARSE_POINTER_QUERY, focusOnOpen, isCoarsePointer } from "./focus-on-open";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("focusOnOpen (#2212)", () => {
  it.each([
    // [placement, coarse pointer, has a field, focus]
    ["anchored", false, true, "field"],
    ["anchored", false, false, "none"],
    ["anchored", true, true, "root"],
    ["anchored", true, false, "none"],
    ["sheet", false, true, "field"],
    ["sheet", false, false, "root"],
    ["sheet", true, true, "root"],
    ["sheet", true, false, "root"],
  ] as const)("%s, coarse %s, field %s: %s", (placement, coarse, hasField, focus) => {
    expect(focusOnOpen(placement, coarse, hasField)).toBe(focus);
  });
});

describe("isCoarsePointer (#2212)", () => {
  it("asks exactly the house's touch query", () => {
    // What a touch device's browser answers; any other query answers false.
    const matchMedia = vi.fn((query: string) => ({ matches: query === "(pointer: coarse)" }));
    vi.stubGlobal("matchMedia", matchMedia);

    expect(isCoarsePointer()).toBe(true);
    expect(matchMedia).toHaveBeenCalledWith(COARSE_POINTER_QUERY);
    expect(COARSE_POINTER_QUERY).toBe("(pointer: coarse)");
  });

  it("is false for a fine pointer", () => {
    // What a desktop browser with a mouse answers: no query about a coarse pointer matches.
    vi.stubGlobal("matchMedia", () => ({ matches: false }));

    expect(isCoarsePointer()).toBe(false);
  });

  it("is false where there is no matchMedia", () => {
    vi.stubGlobal("matchMedia", undefined);

    expect(isCoarsePointer()).toBe(false);
  });
});
