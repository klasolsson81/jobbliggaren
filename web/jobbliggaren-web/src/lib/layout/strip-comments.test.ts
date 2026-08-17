import { describe, expect, it } from "vitest";
import { stripComments } from "./strip-comments";

/**
 * `stripComments` is the container guard's ORACLE: `v3-native-routes.test.ts` decides whether a
 * page owns a width container by looking for the class names in this function's output. An
 * untested oracle is not one — and this function has already shipped two fail-open holes.
 *
 * Both are pinned below as cases rather than described in prose, so a future revision that
 * reintroduces either fails here instead of passing silently:
 *
 * 1. **The comment hole (#1062, code-reviewer M1).** No stripping at all: a docblock naming
 *    `jp-container` satisfied the guard, and the page it named had every container class deleted
 *    from its markup.
 * 2. **The quote-desync hole (#1062, code-reviewer N1).** The first fix tracked quote state, so an
 *    apostrophe in JSX text, a regex literal, or a stray inch mark opened a string that never
 *    closed — after which comments stopped being stripped and hole 1 returned.
 *
 * The three N1 shapes are code-reviewer's own counter-examples, pinned so the re-check can measure
 * them rather than trust them.
 */
describe("stripComments — the container guard's oracle", () => {
  const NAMED_IN_A_DOCBLOCK = `/**\n * Shell: this page uses jp-container jp-page.\n */\n`;

  it("removes a block comment, so a class named only in a docblock does not count", () => {
    expect(stripComments(`${NAMED_IN_A_DOCBLOCK}export const x = 1;`)).not.toContain(
      "jp-container",
    );
  });

  it("removes a line comment", () => {
    expect(stripComments(`// jp-pagehero lives here\nconst x = 1;`)).not.toContain(
      "jp-pagehero",
    );
  });

  it("keeps a class that is actually rendered", () => {
    expect(stripComments(`<div className="jp-container jp-page">`)).toContain("jp-container");
  });

  it("keeps a class rendered through a variable, which the attribute form cannot see", () => {
    // PlainHeaderSkeleton does exactly this, and reading className="…" instead of stripping
    // comments produced a false failure on /ny-ansokan/loading.tsx because of it.
    const source = `const wrapperClass = contained ? "jp-container jp-page" : "";`;
    expect(stripComments(source)).toContain("jp-container");
  });

  // ── N1: an unpaired quote must not disable comment stripping ──────────────
  // Each case pairs a quote-opening shape with a docblock that names a container class. The
  // assertion is on the CLASS, not on the quote: what matters is that the comment still goes.
  //
  // ⚠ The first three are PROVEN to fall: reverting this module to the quote-tracking
  // implementation (which compiled) turned exactly those three red, 3 failed / 7 passed. The
  // fourth is a regression pin only — a template literal opens and closes symmetrically, so it
  // passed under both implementations and never discriminated. Kept because a future
  // "improvement" could break it, but it is not evidence for this fix and is not counted as such.

  it.each([
    ["an apostrophe in JSX text", `<p>Klas' CV</p>`],
    ["an apostrophe in a regex literal", `const r = /don't/;`],
    ["an unpaired inch mark in JSX text", `<p>en 5" skärm</p>`],
    ["an apostrophe in a template literal (pin only — never fell)", "const s = `Klas' CV`;"],
  ])("strips a following docblock despite %s", (_shape, prefix) => {
    expect(stripComments(`${prefix}\n${NAMED_IN_A_DOCBLOCK}`)).not.toContain("jp-container");
  });

  // ── The URL branch, both directions ───────────────────────────────────────
  // This is the only KEEP branch in the function, so it is the only one whose mistakes are
  // fail-OPEN. The first revision pinned only that URLs survive and never that comments still
  // die, and a whitespace-walking colon check then passed that half while reintroducing M1 on
  // six shapes (#1062, code-reviewer M-1). Both directions are pinned now.

  it("does not mistake a URL's slashes for a line comment", () => {
    const source = `<a href="https://jobbliggaren.se/cv" className="jp-container">`;
    const out = stripComments(source);
    expect(out).toContain("jp-container");
    expect(out).toContain("https://jobbliggaren.se/cv");
  });

  it.each([
    ["a switch case label", `case "a": // jp-container`],
    ["a default label", `default: // jp-container`],
    ["an object literal key", `const o = { shell: // jp-container`],
    ["a ternary else", `const v = a ? b : // jp-container`],
    ["a type annotation", `const x: // jp-container`],
    ["a loop label", `outer: // jp-container`],
  ])("still strips a line comment that follows %s", (_shape, source) => {
    // All six were measured FAIL-OPEN under the walk-back version: the colon before the `//`
    // made it read as a URL scheme and the whole comment survived.
    expect(stripComments(source)).not.toContain("jp-container");
  });

  it("has one known residual, pinned so it is a decision and not a surprise", () => {
    // A label colon ABUTTING the slashes is indistinguishable from a scheme by look-back alone.
    // No such shape exists in the corpus and nobody writes one — but a guard that hides its own
    // reach is worse than a narrow one, so the hole is asserted rather than described.
    expect(stripComments(`default:// jp-container`)).toContain("jp-container");
  });

  it("biases fail-closed: a shape it mis-reads drops text rather than keeping it", () => {
    // An unterminated block comment swallows the rest of the file. That makes the guard
    // stricter (a page would fail), never more permissive — the safe direction for a guard.
    expect(stripComments(`/* unterminated\n<div className="jp-container">`)).not.toContain(
      "jp-container",
    );
  });
});
