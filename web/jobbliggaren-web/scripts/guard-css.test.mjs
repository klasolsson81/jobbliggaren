import { describe, it, expect } from "vitest";
import {
  stripJs,
  blankParens,
  selectorBranches,
  selectorCompounds,
  branchDefinitions,
} from "./guard-css.mjs";

/**
 * These four helpers are where guard-css's BLOCKING false positives live.
 *
 * The gate fails a commit and tells the reader to "unscope a rule, drop the
 * class" — so a wrong answer here does not merely annoy, it talks someone into
 * deleting live CSS. code-reviewer found exactly that (#1056 B1): compounds were
 * split without paren awareness, so a name inside `:is()`/`:not()`/`:has()`
 * became a REQUIRED ancestor. `:not()` inverts, which makes that backwards.
 *
 * Importing this module does not run the sweep — guard-css.mjs only executes
 * when it is the entry point.
 */

describe("blankParens", () => {
  it("removes names inside a functional pseudo-class, keeping length", () => {
    const sel = ":is(.jp-ghost, .jp-beta) .jp-target";
    const out = blankParens(sel);
    expect(out).toHaveLength(sel.length);
    expect(out).not.toContain("jp-ghost");
    expect(out).not.toContain("jp-beta");
    expect(out).toContain("jp-target");
  });

  it("blanks :not() contents — treating them as required ancestors is inverted", () => {
    expect(blankParens(":not(.jp-ghost) .jp-target")).not.toContain("jp-ghost");
  });

  it("blanks :has() contents, which live CSS in this repo relies on", () => {
    // .jp-cvupload__drop:has(+ .jp-cvupload__input:focus-visible)
    const out = blankParens(".jp-drop:has(+ .jp-input:focus-visible)");
    expect(out).toContain("jp-drop");
    expect(out).not.toContain("jp-input");
  });

  it("leaves an unparenthesised selector untouched", () => {
    expect(blankParens(".jp-a .jp-b")).toBe(".jp-a .jp-b");
  });
});

describe("selectorCompounds", () => {
  it("splits on descendant, child and sibling combinators", () => {
    expect(selectorCompounds(".jp-a > .jp-b + .jp-c ~ .jp-d .jp-e")).toEqual([
      ".jp-a",
      ".jp-b",
      ".jp-c",
      ".jp-d",
      ".jp-e",
    ]);
  });

  it("does NOT split on a combinator inside parentheses", () => {
    // The `+` belongs to :has(), so this is ONE compound whose subject is .jp-drop.
    expect(selectorCompounds(".jp-drop:has(+ .jp-input)")).toEqual([".jp-drop:has(+ .jp-input)"]);
  });

  it("does NOT split on whitespace inside an attribute selector", () => {
    expect(selectorCompounds('.jp-a[data-x="one two"] .jp-b')).toEqual([
      '.jp-a[data-x="one two"]',
      ".jp-b",
    ]);
  });

  it("does not split inside a comma-bearing :is()", () => {
    expect(selectorCompounds(":is(.jp-a, .jp-b) .jp-c")).toEqual([":is(.jp-a, .jp-b)", ".jp-c"]);
  });
});

describe("selectorBranches", () => {
  it("splits a selector list on top-level commas", () => {
    expect(selectorBranches(".jp-a, .jp-b").map((b) => b.trim())).toEqual([".jp-a", ".jp-b"]);
  });

  it("ignores commas inside :is() and inside attribute selectors", () => {
    expect(selectorBranches(":is(.jp-a, .jp-b) .jp-c")).toHaveLength(1);
    expect(selectorBranches('.jp-a[data-x="a,b"], .jp-c')).toHaveLength(2);
  });
});

describe("stripJs", () => {
  it("removes line and block comments", () => {
    expect(stripJs('// jp-dead\nconst a = 1;')).not.toContain("jp-dead");
    expect(stripJs('/* jp-dead */ const a = 1;')).not.toContain("jp-dead");
  });

  it("preserves offsets so reported positions stay correct", () => {
    const src = '// comment\nconst a = "jp-alive";';
    expect(stripJs(src)).toHaveLength(src.length);
  });

  it("does NOT treat // inside a string as a comment", () => {
    // The naive stripper blanks the rest of this line at the `//` in the URL,
    // which would hide a real class reference sharing it and report a LIVE
    // class as dead — the one error direction the sweep must never take.
    const src = 'const u = "https://example.com"; const c = "jp-alive";';
    expect(stripJs(src)).toContain("jp-alive");
  });

  it("handles escaped quotes without losing the rest of the file", () => {
    const src = 'const a = "he said \\"hi\\""; const c = "jp-alive";';
    expect(stripJs(src)).toContain("jp-alive");
  });

  it("does not treat // inside a template literal as a comment", () => {
    const src = "const u = `https://x`; const c = `jp-alive`;";
    expect(stripJs(src)).toContain("jp-alive");
  });
});

/**
 * CALL-SITE tests (re-review M5). The helper tests above all stayed green when
 * the `blankParens(...)` wrappers were deleted from the place that uses them,
 * and the whole pre-fix finding set came back. These pin the use, not the rule:
 * each case is one of the three violations that returned.
 */
describe("branchDefinitions — the call site, not just the helpers", () => {
  it(":is() members are NOT required ancestors (it is a disjunction)", () => {
    // Pre-fix this made .jp-t1 unreachable unless BOTH :is() members were used.
    expect(branchDefinitions(":is(.jp-ghost, .jp-beta) .jp-t1")).toEqual({
      subjects: ["jp-t1"],
      ancestors: [],
    });
  });

  it(":not() members are NOT required ancestors — requiring them is inverted", () => {
    // The LESS .jp-ghost is used, the MORE this matches.
    expect(branchDefinitions(":not(.jp-ghost) .jp-t2")).toEqual({
      subjects: ["jp-t2"],
      ancestors: [],
    });
  });

  it(":has() contents do not become the subject", () => {
    // Live in this repo: .jp-cvupload__drop:has(+ .jp-cvupload__input:focus-visible)
    expect(branchDefinitions(".jp-drop:has(+ .jp-input:focus-visible)")).toEqual({
      subjects: ["jp-drop"],
      ancestors: [],
    });
  });

  it("still requires a genuine ancestor outside the pseudo-class", () => {
    // The fix must not over-blank: .jp-parent IS a required ancestor here.
    expect(branchDefinitions(".jp-parent:not(.jp-x) .jp-child")).toEqual({
      subjects: ["jp-child"],
      ancestors: ["jp-parent"],
    });
  });

  it("handles a plain descendant chain", () => {
    expect(branchDefinitions(".jp-a .jp-b")).toEqual({ subjects: ["jp-b"], ancestors: ["jp-a"] });
  });
});
