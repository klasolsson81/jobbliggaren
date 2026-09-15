import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1727 — the `.jp-pagehero` narrow-viewport arm must come AFTER the base rules it overrides.
 *
 * Reads source text; it does not render. What it guards is a NEGATIVE constraint that no rendered
 * test in this suite can state at all: jsdom applies none of `globals.css`, so a vitest render
 * cannot see a font-size the cascade silently discarded.
 *
 * The mechanism, which is the whole reason this file exists: a media query contributes NO
 * specificity (CSS Cascading and Inheritance Level 4). So `@media { .x { a: 1 } }` and a later
 * `.x { a: 2 }` are both (0,1,0), and source order alone decides — the media declaration loses at
 * every viewport, silently, with no build warning and no lint error.
 *
 * That is not hypothetical. It shipped twice from one misplaced block:
 *   - `.jp-pagehero__title`'s 32px mobile size never rendered; every pagehero title stayed 44px,
 *     which overflowed the viewport and scrolled the document sideways (WCAG 2.1 AA 1.4.10).
 *   - `.jp-pagehero__aside--stacked`'s `align-items: stretch` never rendered, so the stacked aside
 *     kept hanging at the right edge — the exact state #805 punkt 7 was written to remove. It was
 *     reviewed, merged and unrendered for two months.
 *
 * Both were invisible to `pnpm lint`, `tsc`, `guard:css` and the whole vitest suite.
 *
 * Sibling precedent for pinning a cascade fact from source text: `globals-link-rule.test.ts`
 * (specificity of a `:not()` list) and `globals-focus-ring.test.ts`.
 */
const CSS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"), "utf-8");

/**
 * Block comments are stripped before ANY matching. `globals-link-rule.test.ts` learned this from
 * `code-reviewer` in PR #1400: rule text left behind in a comment otherwise stands in for a rule
 * that was deleted, and the guard passes on prose. This block is dense with commented-out-looking
 * CSS, so the hazard is live here.
 */
const stripComments = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "));

type Rule = { selector: string; properties: string[]; start: number; inMedia: string | null };

/**
 * Splits a selector list on TOP-LEVEL commas only. A bare `prelude.split(",")` tears
 * `a:not(.jp-btn, [data-slot="button"])` into three fragments that match nothing and print as
 * nonsense when the sweep reports a failure. `globals.css` carries exactly that selector.
 */
function splitSelectorList(prelude: string): string[] {
  const out: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < prelude.length; i++) {
    const c = prelude[i];
    if (c === "(" || c === "[") depth++;
    else if (c === ")" || c === "]") depth--;
    else if (c === "," && depth === 0) {
      out.push(prelude.slice(start, i));
      start = i + 1;
    }
  }
  out.push(prelude.slice(start));
  return out.map((s) => s.trim().replace(/\s+/g, " ")).filter(Boolean);
}

/**
 * Flat rule reader: every `selector { ... }`, with the enclosing at-rule prelude when there is one.
 * Deliberately not a full CSS parser — it only needs selector text, property names and byte order,
 * and a parser that understood more could disagree with the browser in ways this guard cannot check.
 *
 * It must nonetheless be fail-CLOSED, because a parser that silently sees no rules makes the sweep
 * below report a clean stylesheet. The one error direction that matters is a `;`-terminated
 * at-statement — `@import "tailwindcss";`, `@custom-variant dark (...);`, both live at the top of
 * this file — whose text would otherwise glue onto the NEXT prelude. When the next block is the
 * `@media` itself, its prelude stops starting with "@media", every rule inside reads as
 * unconditional, and the sweep goes quiet on a stylesheet that does carry a dead declaration.
 */
function readRules(cssText: string): Rule[] {
  const src = stripComments(cssText);
  const rules: Rule[] = [];
  const atStack: { prelude: string; depth: number }[] = [];
  let depth = 0;
  let tokenStart = 0;

  for (let i = 0; i < src.length; i++) {
    const ch = src[i];
    // A `;` outside any block body ends an at-statement; the next prelude starts after it.
    if (ch === ";" && src.slice(tokenStart, i).trim().startsWith("@")) {
      tokenStart = i + 1;
      continue;
    }
    if (ch === "{") {
      const prelude = src.slice(tokenStart, i).trim().replace(/\s+/g, " ");
      if (prelude.startsWith("@")) {
        atStack.push({ prelude, depth });
        depth++;
        tokenStart = i + 1;
        continue;
      }
      let d = 1;
      let j = i + 1;
      for (; j < src.length && d > 0; j++) {
        if (src[j] === "{") d++;
        else if (src[j] === "}") d--;
      }
      const body = src.slice(i + 1, j - 1);
      const properties = [...body.matchAll(/(^|;)\s*([-a-zA-Z]+)\s*:/g)]
        .map((m) => (m[2] ?? "").toLowerCase())
        .filter((p) => !p.startsWith("--"));
      const media = atStack.find((a) => a.prelude.startsWith("@media"))?.prelude ?? null;
      for (const selector of splitSelectorList(prelude)) {
        rules.push({ selector, properties, start: i, inMedia: media });
      }
      i = j - 1;
      tokenStart = i + 1;
      continue;
    }
    if (ch === "}") {
      depth--;
      while (atStack.length && (atStack[atStack.length - 1]?.depth ?? -1) >= depth) atStack.pop();
      tokenStart = i + 1;
    }
  }
  return rules;
}

/**
 * A declaration is DEAD when an identical selector declares the same property later at the top
 * level. Identical selector text means identical specificity by construction, so this decides the
 * cascade without computing specificity at all — and therefore cannot produce a specificity false
 * positive. It reports a floor, not a census: a media rule beaten by a DIFFERENT but
 * equally-specific selector is a real case this check does not claim to catch.
 */
function deadMediaDeclarations(cssText: string): string[] {
  const rules = readRules(cssText);
  const dead: string[] = [];
  for (const rule of rules) {
    if (!rule.inMedia) continue;
    for (const later of rules) {
      if (later.inMedia || later.selector !== rule.selector || later.start <= rule.start) continue;
      for (const property of rule.properties) {
        if (later.properties.includes(property)) dead.push(`${rule.selector} { ${property} }`);
      }
    }
  }
  return dead;
}

/**
 * The pre-fix shape, reduced to the two casualties. This is the POSITIVE CONTROL: a guard that only
 * ever runs against a passing file measures nothing, because a no-op checker would pass it too.
 * If this fixture ever stops reporting BOTH entries, the checker has broken — not the stylesheet.
 */
const PRE_FIX_FIXTURE = `
.jp-pagehero { background: var(--jp-canvas); padding: 24px 32px 0; }
.jp-pagehero__inner { padding: 36px 40px 40px; }
@media (max-width: 720px) {
  .jp-pagehero { padding: 16px 20px 0; }
  .jp-pagehero__inner { padding: 28px 24px; }
  .jp-pagehero__title { font-size: var(--jp-fs-hero-title-sm); }
  .jp-pagehero__aside--stacked { align-items: stretch; width: 100%; }
}
.jp-pagehero__title { font-size: var(--jp-fs-hero-title); line-height: 1.1; }
.jp-pagehero__aside--stacked { flex-direction: column; align-items: flex-end; }
`;

/**
 * The same pre-fix shape with a `;`-terminated at-statement IMMEDIATELY BEFORE the `@media`.
 * `code-reviewer` found the parser read that as CLEAN (PR #1730): the at-statement glued onto the
 * `@media` prelude, which then stopped starting with "@media", so every rule inside looked
 * unconditional and the sweep went silent on a stylesheet carrying both casualties.
 *
 * The at-statements are spliced in at the `@media` rather than written at the top, because the
 * position is the whole test. At the top, the block they swallow is `.jp-pagehero`'s base padding,
 * whose loss does not change what the sweep returns — the fixture then passes on the very parser it
 * exists to catch. Anchoring the splice keeps the position from drifting back.
 */
const PRE_FIX_BEHIND_AT_STATEMENT = PRE_FIX_FIXTURE.replace(
  "@media",
  '@import "tailwindcss";\n@custom-variant dark (&:where([data-theme="dark"]));\n@media',
);

describe("globals.css — the .jp-pagehero narrow-viewport arm (#1727)", () => {
  it("detects the pre-fix shape — both casualties, not just the title", () => {
    // Bind 2 of the CTO ruling: a mutation that proves one half is not a pin. Moving the block
    // back above either base rule must turn this red for THAT rule specifically.
    expect(deadMediaDeclarations(PRE_FIX_FIXTURE)).toEqual([
      ".jp-pagehero__title { font-size }",
      ".jp-pagehero__aside--stacked { align-items }",
    ]);
  });

  it("still detects it behind a `;`-terminated at-statement — the sweep must fail CLOSED", () => {
    // Without the `;` handling in readRules this returns [], and the file-wide assertion below
    // would then pass on any stylesheet at all. That is the one error direction a guard must not
    // have, so it is pinned rather than trusted.
    expect(deadMediaDeclarations(PRE_FIX_BEHIND_AT_STATEMENT)).toEqual([
      ".jp-pagehero__title { font-size }",
      ".jp-pagehero__aside--stacked { align-items }",
    ]);
  });

  it("reads every top-level rule in globals.css, so the sweep below covers the whole file", () => {
    // The mis-parse above does not announce itself — it drops rules. `:root` is the canary: the
    // file declares three and the first one follows `@custom-variant`, which is exactly the
    // position that used to swallow it.
    const roots = readRules(CSS).filter((r) => r.selector === ":root");
    const declared = [...stripComments(CSS).matchAll(/(^|\n)\s*:root\s*\{/g)].length;
    expect(roots).toHaveLength(declared);
  });

  it("leaves no dead media declaration anywhere in globals.css", () => {
    expect(
      deadMediaDeclarations(CSS),
      "A media-query declaration is overridden by an identical selector later in the file. A media " +
        "query adds no specificity, so that declaration never renders at any viewport. Move the " +
        "@media block BELOW the base rule it overrides — do not raise specificity to win, which " +
        "leaves the block misplaced and the next rule appended after it silently dead.",
    ).toEqual([]);
  });

  it("puts each narrow arm after the base rule it overrides", () => {
    const rules = readRules(CSS);
    for (const selector of [".jp-pagehero__title", ".jp-pagehero__aside--stacked"]) {
      const base = rules.filter((r) => r.selector === selector && !r.inMedia);
      const arm = rules.filter((r) => r.selector === selector && r.inMedia?.includes("720px"));
      expect(base, `${selector}: expected exactly one base rule`).toHaveLength(1);
      expect(arm, `${selector}: expected exactly one <=720px arm`).toHaveLength(1);
      const [baseRule] = base;
      const [armRule] = arm;
      if (!baseRule || !armRule) throw new Error(`${selector}: missing base rule or arm`);
      expect(
        armRule.start,
        `${selector}: the <=720px arm must come AFTER its base rule in source order, or it never ` +
          `renders. Parity with .jp-hero__title, which works only because it sits after its base.`,
      ).toBeGreaterThan(baseRule.start);
    }
  });

  it("knows exactly which selectors the two narrow arms carry", () => {
    // The precedent's framing (globals-link-rule.test.ts): a new selector here is not a failure of
    // the code — it is this guard saying it has not been taught about the new one. An unpinned
    // declaration slipped into this block is precisely how the aside casualty arrived.
    const armed = readRules(CSS)
      .filter((r) => r.inMedia?.includes("720px") && r.selector.startsWith(".jp-pagehero"))
      .map((r) => r.selector);
    expect(armed.sort()).toEqual(
      [
        ".jp-pagehero",
        ".jp-pagehero__inner",
        ".jp-pagehero__title",
        ".jp-pagehero__aside--stacked",
        ".jp-pagehero__aside--stacked .jp-pagehero__btnrow",
      ].sort(),
    );
  });

  it("keeps the title's wrap and hyphenation unconditional, not inside the narrow arm", () => {
    // Both are inert when the word fits, so they belong in the base rule: `.jp-pagehero__main` is
    // `flex: 1 1 320px; min-width: 0` beside an aside, which can squeeze the title column narrow
    // above 720px too, where the narrow arm does not apply.
    const rules = readRules(CSS);
    const base = rules.find((r) => r.selector === ".jp-pagehero__title" && !r.inMedia);
    expect(base?.properties).toContain("overflow-wrap");
    expect(base?.properties).toContain("hyphens");
  });
});
