import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1352 — the global link colour must exempt the button primitive, and must do it in ONE `:not()`.
 *
 * Reads source text; it does not render. What it guards is a NEGATIVE constraint that no rendered
 * test can state cheaply: the exemption must live in a selector list, because a list takes the
 * specificity of its most specific argument and keeps the rule at (0,1,1). Chaining two `:not()`s
 * makes it (0,2,1), which then outranks `.jp-foot__links a` (0,1,1) and
 * `.jp-land-hero--plate .jp-land-hero__guestlink` (0,2,0) — both written to beat exactly this rule.
 * Measured on the chained form: the footer link falls to 2.04:1 and its hover to 2.39:1, both hard
 * WCAG 1.4.3 failures. The chained form is a plausible tidy-up, it compiles, and nothing else in the
 * repo would notice.
 *
 * Fail-closed on three axes, each of which was a live fail-open in the first revision (`code-reviewer`,
 * PR #1400): the collector takes ANY trailing pseudo-class rather than only `:hover`, so a third rule
 * added as `a:not(...):focus-visible` is seen instead of silently ignored; block comments are stripped
 * first, so rule text left behind in a comment cannot stand in for a deleted rule; and the exemption is
 * checked for an id, since `#x` inside the one `:not()` satisfies the syntactic form while lifting the
 * rule to (1,0,1) and beating both surfaces anyway.
 */
const CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"),
  "utf-8",
).replace(/\/\*[\s\S]*?\*\//g, "");

/** The link-colour rules, matched by their declaration rather than by a line number. */
const LINK_RULES = CSS.split("\n")
  .map((line) => line.trim())
  .filter((line) => /^a:not\(/.test(line) && line.includes("{"));

describe("globals.css — the global link colour rule (#1352)", () => {
  it("has exactly the two rules this guard is about", () => {
    expect(
      LINK_RULES,
      `Expected the base rule and its :hover twin. A third rule here is not a failure of the ` +
        `code — it is this guard telling you it has not been taught about the new one.`,
    ).toHaveLength(2);
    expect(LINK_RULES.filter((r) => r.includes(":hover"))).toHaveLength(1);
  });

  it("exempts the button primitive and the popover row by their own selectors", () => {
    for (const rule of LINK_RULES) {
      expect(rule).toContain('[data-slot="button"]');
      expect(
        rule,
        `${rule.slice(0, rule.indexOf("{")).trim()} — the popover row is an <a> that ` +
          `carries its own ink at (0,1,0). Drop it from this list and this rule's (0,1,1) ` +
          `repaints the row accent-green, with every other assertion in this file green.`,
      ).toContain(".jp-popover__rowbtn");
    }
  });

  it("carries the exemption in ONE :not(), never two chained", () => {
    for (const rule of LINK_RULES) {
      const selector = rule.slice(0, rule.indexOf("{"));
      expect(
        selector.match(/:not\(/g) ?? [],
        `${selector.trim()} — a chained :not() raises the rule from (0,1,1) to (0,2,1) and turns ` +
          `the footer links and the hero guest link green. Keep both exemptions in one selector list.`,
      ).toHaveLength(1);
    }
  });

  it("keeps the selector free of an id, which would raise the rule the same way", () => {
    for (const rule of LINK_RULES) {
      // The whole selector, not the `:not()` argument slice. Slicing to the first `)` ends at the
      // wrong paren the moment the list contains one of its own — `:is()`, `:where()`, `:has()`, or
      // a quoted attribute value carrying `)` — and an id after it then survives (measured,
      // `code-reviewer` re-check on PR #1400). No `#` can legitimately appear anywhere in a rule
      // this guard collects, so the wider assertion is also the simpler one, and it additionally
      // catches an id appended OUTSIDE the `:not()`.
      const selector = rule.slice(0, rule.indexOf("{"));
      expect(
        selector,
        `${selector.trim()} — an id makes this rule outrank both protected surfaces wherever it ` +
          `sits: (1,0,1) inside the :not() list, (1,1,1) appended after it.`,
      ).not.toContain("#");
    }
  });
});
