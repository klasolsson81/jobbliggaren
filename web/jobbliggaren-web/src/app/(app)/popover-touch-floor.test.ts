import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2212: at ≤768 every control in a `.jp-popover` reaches the 44px touch floor (DESIGN.md §5).
 * Measured 2026-10-24 on 5ad51c9c1 at 768 and 375, without it: the hero chips' rows 39.5px, their
 * footer link 41.7px, Matchning's switches 20px and its help buttons 36px. jsdom applies no
 * stylesheet, so the rules are pinned from the source; the rendered hit areas are the render
 * matrix's.
 */
const APP_CSS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "app.css"), "utf8");
const TOUCH = "@media (max-width: 768px)";
const rules = readRules(APP_CSS).filter((r) => r.inMedia === TOUCH);
const declared = (selector: string, property: string) =>
  rules
    .filter((r) => r.selector === selector)
    .flatMap((r) => r.declarations.filter((d) => d.property === property).map((d) => d.value));

describe("the popover touch floor at ≤768 (#2212)", () => {
  it.each([
    ".jp-popover .jp-checkitem",
    ".jp-popover .jp-radioitem",
    ".jp-popover .jp-popover-row",
    ".jp-popover .jp-popover__rowbtn",
    ".jp-popover .jp-popover__footlink",
    '.jp-popover [data-slot="dialog-trigger"]',
  ])("%s is at least 44px tall", (selector) => {
    expect(declared(selector, "min-height")).toEqual(["44px"]);
  });

  it.each([".jp-popover .jp-clearlink::before", ".jp-popover .jp-gradefilter__switch::before"])(
    "%s gives its control an invisible hit area",
    (selector) => {
      expect(declared(selector, "content")).toEqual(['""']);
      expect(declared(selector, "position")).toEqual(["absolute"]);
      expect(declared(selector, "inset")).toHaveLength(1);
    },
  );
});
