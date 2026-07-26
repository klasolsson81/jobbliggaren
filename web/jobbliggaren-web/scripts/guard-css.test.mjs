import { describe, it, expect } from "vitest";
import {
  stripJs,
  blankParens,
  selectorBranches,
  selectorCompounds,
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
