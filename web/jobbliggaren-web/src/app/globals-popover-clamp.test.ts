import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The popover row's label must not be clamped to a single line.
 *
 * Reads source text; it does not render. jsdom computes no layout, so no rendered test can state
 * this property at all — and the property is invisible to type-checking, lint and every other gate.
 *
 * What makes a guard worth its lines here is the SHAPE of the regression. The four declarations are
 * one unit: drop `-webkit-box-orient` or `overflow` and the clamp goes inert, the label falls back to
 * a single line, and nothing anywhere reports it. `-webkit-box-orient: vertical` in particular reads
 * as a removable legacy prefix, so deleting it is a plausible tidy-up that compiles and renders
 * without error.
 *
 * The property pinned is "more than one line", not "exactly two". A later design change to three
 * lines is design-reviewer's call and does not break what this guard is about; a change back to one
 * is the defect it exists for.
 *
 * Block comments are stripped first, so a commented-out copy of the rule cannot stand in for a
 * deleted one (`globals-link-rule.test.ts` fail-open, PR #1400). Rules are collected by SELECTOR
 * rather than by an exact `.class {` match, so a compound (`.jp-popover__rowlabel.foo`) or
 * descendant override is seen rather than silently skipped.
 */
const CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"),
  "utf-8",
).replace(/\/\*[\s\S]*?\*\//g, "");

const CLAMP_RULES = [...CSS.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
  .filter((m) => m[1]!.includes(".jp-popover__rowlabel"))
  .map((m) => ({ selector: m[1]!.trim(), body: m[2]! }));

/** `-webkit-line-clamp: 2` -> `2`. Returns null when the declaration is absent. */
function lineClamp(body: string): number | null {
  const hit = body.match(/-webkit-line-clamp\s*:\s*(\d+)/);
  return hit ? Number(hit[1]) : null;
}

describe("globals.css — the popover row label is clamped to more than one line", () => {
  it("has exactly the one rule this guard is about", () => {
    expect(
      CLAMP_RULES.map((r) => r.selector),
      `Expected a single .jp-popover__rowlabel rule. Zero means the guard measures nothing and ` +
        `every assertion below passes vacuously. More than one means a second rule can override ` +
        `the clamp — that is not a failure of the code, it is this guard telling you it has not ` +
        `been taught about the new one.`,
    ).toHaveLength(1);
  });

  it("carries all four declarations the clamp needs to work at all", () => {
    const { body } = CLAMP_RULES[0]!;
    // Each of these alone is enough to make the clamp inert while the rule still looks right.
    expect(body, "display: -webkit-box is what -webkit-line-clamp acts on").toMatch(
      /display\s*:\s*-webkit-box/,
    );
    expect(
      body,
      "-webkit-box-orient: vertical reads as a removable legacy prefix; without it the box lays " +
        "out horizontally and the clamp does nothing",
    ).toMatch(/-webkit-box-orient\s*:\s*vertical/);
    expect(body, "without overflow: hidden the clamped lines still paint").toMatch(
      /overflow\s*:\s*hidden/,
    );
    expect(lineClamp(body), "-webkit-line-clamp is the clamp itself").not.toBeNull();
  });

  it("gives the label more than one line", () => {
    const { body } = CLAMP_RULES[0]!;
    expect(
      lineClamp(body),
      `A one-line clamp is the defect this rule was written for: the ort label enumerates every ` +
        `set granularity and the distans part sorts LAST, so one line cuts exactly the part the ` +
        `label gained because it was false without it.`,
    ).toBeGreaterThanOrEqual(2);
  });

  it("does not re-introduce nowrap, which defeats the clamp while every declaration above survives", () => {
    const { body } = CLAMP_RULES[0]!;
    expect(body).not.toMatch(/white-space\s*:\s*nowrap/);
  });
});
