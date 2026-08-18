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
 * Both surfaces then render green on green. The chained form is a plausible tidy-up, it compiles,
 * and nothing else in the repo would notice.
 *
 * Fail-closed: a rule this cannot find fails rather than passes.
 */
const CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"),
  "utf-8",
);

/** The two link-colour rules, matched by their declaration rather than by a line number. */
const LINK_RULES = CSS.split("\n").filter((line) =>
  /^a:not\(.*\)\s*(:hover\s*)?\{/.test(line.trim()),
);

describe("globals.css — the global link colour rule (#1352)", () => {
  it("has exactly the two rules this guard is about", () => {
    expect(LINK_RULES).toHaveLength(2);
    expect(LINK_RULES.filter((r) => r.includes(":hover"))).toHaveLength(1);
  });

  it("exempts the button primitive by its own slot attribute", () => {
    for (const rule of LINK_RULES) {
      expect(rule).toContain('[data-slot="button"]');
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
});
