import { describe, it, expect, vi } from "vitest";
import type { MouseEvent } from "react";
import { onPlainNav } from "./modified-click";

// The polarity lives here, pinned once. Every call site is then a mechanical
// forward with no branch of its own to get backwards.
type ClickShape = Partial<
  Pick<MouseEvent<Element>, "metaKey" | "ctrlKey" | "shiftKey" | "altKey" | "button">
>;

function click(shape: ClickShape = {}): MouseEvent<Element> {
  return {
    metaKey: false,
    ctrlKey: false,
    shiftKey: false,
    altKey: false,
    button: 0,
    ...shape,
  } as MouseEvent<Element>;
}

describe("onPlainNav", () => {
  it("runs the callback for a plain primary click", () => {
    const fn = vi.fn();
    onPlainNav(click(), fn);
    expect(fn).toHaveBeenCalledOnce();
  });

  const modifiers = [
    ["metaKey", { metaKey: true }],
    ["ctrlKey", { ctrlKey: true }],
    ["shiftKey", { shiftKey: true }],
    ["altKey", { altKey: true }],
  ] as const satisfies ReadonlyArray<readonly [string, ClickShape]>;

  for (const [name, shape] of modifiers) {
    it(`suppresses the callback when ${name} is held`, () => {
      const fn = vi.fn();
      onPlainNav(click(shape), fn);
      expect(fn).not.toHaveBeenCalled();
    });
  }

  it("suppresses the callback for a non-primary button", () => {
    const fn = vi.fn();
    onPlainNav(click({ button: 1 }), fn);
    expect(fn).not.toHaveBeenCalled();
  });

  it("suppresses the callback when several modifiers are combined", () => {
    const fn = vi.fn();
    onPlainNav(click({ ctrlKey: true, shiftKey: true }), fn);
    expect(fn).not.toHaveBeenCalled();
  });

  it("decides per event rather than latching on a previous one", () => {
    const fn = vi.fn();
    onPlainNav(click({ ctrlKey: true }), fn);
    expect(fn).not.toHaveBeenCalled();
    onPlainNav(click(), fn);
    expect(fn).toHaveBeenCalledOnce();
  });
});
