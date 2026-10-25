import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2212: an open filter popover is page content anchored to its pill. When the page scrolls it
 * passes under the sticky header, whose menus then paint above it, and it stays above the hero
 * search's suggestion list. jsdom applies no stylesheet, so the order is pinned from the source:
 * globals.css, then app.css, in the order the (app) layout loads them.
 */
const here = dirname(fileURLToPath(import.meta.url));
const read = (path: string) => readFileSync(resolve(here, path), "utf8");
const rules = [...readRules(read("../globals.css")), ...readRules(read("app.css"))];

/** The z-index the cascade gives a selector outside any at-rule: its last top-level declaration. */
function zIndexOf(selector: string): number {
  const values = rules
    .filter((r) => r.selector === selector && r.inMedia === null)
    .flatMap((r) => r.declarations.filter((d) => d.property === "z-index").map((d) => Number(d.value)));
  return values.at(-1) ?? NaN;
}

describe("the filter popovers' stacking order (#2212)", () => {
  it("sits under the sticky header", () => {
    expect(zIndexOf(".jp-popover")).toBeLessThan(zIndexOf(".jp-header"));
  });

  it("sits above the hero search's suggestion list", () => {
    // The list's order is a Tailwind class in its component, not a rule in either stylesheet.
    const typeahead = read("../../components/job-ads/job-ad-typeahead.tsx");
    const listZ = Math.max(...[...typeahead.matchAll(/\bz-(\d+)\b/g)].map((m) => Number(m[1])));

    expect(listZ).toBeGreaterThan(0);
    expect(zIndexOf(".jp-popover")).toBeGreaterThan(listZ);
  });
});
