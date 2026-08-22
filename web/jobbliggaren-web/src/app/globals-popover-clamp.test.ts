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
 * What makes a guard worth its lines here is the SHAPE of the regression. Drop `-webkit-box-orient`
 * and the clamp goes inert, the label falls back to a single line, and nothing anywhere reports it.
 * `-webkit-box-orient: vertical` in particular reads
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

/** The rule that owns the clamp. */
const BASE = CLAMP_RULES.filter((r) => r.selector === ".jp-popover__rowlabel");
/** Every other rule in the class family — modifiers, compounds, descendants. */
const SIBLINGS = CLAMP_RULES.filter((r) => r.selector !== ".jp-popover__rowlabel");

/** The five declarations that make the clamp work, or silently unmake it. */
const CLAMP_MECHANISM =
  /display\s*:|-webkit-line-clamp\s*:|-webkit-box-orient\s*:|overflow\s*:|white-space\s*:/;

/** `-webkit-line-clamp: 2` -> `2`. Returns null when the declaration is absent. */
function lineClamp(body: string): number | null {
  const hit = body.match(/-webkit-line-clamp\s*:\s*(\d+)/);
  return hit ? Number(hit[1]) : null;
}

describe("globals.css — the popover row label is clamped to more than one line", () => {
  it("has exactly one base rule, and no sibling rule that touches the clamp", () => {
    expect(
      BASE.map((r) => r.selector),
      `Expected a single .jp-popover__rowlabel rule. Zero means the guard measures nothing and ` +
        `every assertion below passes vacuously. More than one means a second rule can override ` +
        `the clamp — that is not a failure of the code, it is this guard telling you it has not ` +
        `been taught about the new one.`,
    ).toHaveLength(1);
    // A sibling in this class family lands on the SAME element (the muted label carries both
    // classes), so it can defeat the clamp. The rule is evaluated, not rejected: it may exist,
    // it may not touch the mechanism.
    for (const sibling of SIBLINGS) {
      expect(
        sibling.body,
        `${sibling.selector} shares an element with the clamp and re-declares part of its ` +
          `mechanism. Either drop that declaration or teach this guard why it is safe.`,
      ).not.toMatch(CLAMP_MECHANISM);
    }
  });

  it("carries all four declarations the clamp needs to work at all", () => {
    const { body } = BASE[0]!;
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
    const { body } = BASE[0]!;
    expect(
      lineClamp(body),
      `A one-line clamp is the defect this rule was written for: the ort label enumerates every ` +
        `set granularity and the distans part sorts LAST, so one line cuts exactly the part the ` +
        `label gained because it was false without it.`,
    ).toBeGreaterThanOrEqual(2);
  });

  it("declares its own line-height instead of inheriting the body's", () => {
    // Form, not value: the row height that keeps five rows inside the scroll container is a
    // relation over five inputs (maxHeight, the container's padding, --text-ui, the row's
    // padding, maxItems), so a solved threshold here would be green after any of them moved.
    expect(BASE[0]!.body).toMatch(/line-height\s*:/);
  });

  it("does not re-introduce nowrap, which defeats the clamp while every declaration above survives", () => {
    const { body } = BASE[0]!;
    expect(body).not.toMatch(/white-space\s*:\s*nowrap/);
  });
});
