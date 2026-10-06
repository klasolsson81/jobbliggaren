/**
 * Reading `globals.css` as rules, for the fitness functions that pin a cascade fact from source
 * text. jsdom applies no stylesheet, so a rendered vitest test cannot see one.
 */

/**
 * Block comments are stripped before ANY matching. `globals-link-rule.test.ts` learned this from
 * `code-reviewer` in PR #1400: rule text left behind in a comment otherwise stands in for a rule
 * that was deleted, and the guard passes on prose.
 */
export const stripCssComments = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "));

export type Rule = {
  selector: string;
  properties: string[];
  declarations: { property: string; value: string }[];
  start: number;
  inMedia: string | null;
};

/**
 * Splits a selector list on TOP-LEVEL commas only. A bare `prelude.split(",")` tears
 * `a:not(.jp-btn, [data-slot="button"])` into three fragments that match nothing.
 * `globals.css` carries exactly that selector.
 */
export function splitSelectorList(prelude: string): string[] {
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
 *
 * It must nonetheless be fail-CLOSED, because a parser that silently sees no rules makes a
 * consumer's sweep report a clean stylesheet. The one error direction that matters is a `;`-terminated
 * at-statement — `@import "tailwindcss";`, `@custom-variant dark (...);`, both live at the top of
 * `globals.css` — whose text would otherwise glue onto the NEXT prelude. When the next block is the
 * `@media` itself, its prelude stops starting with "@media", and every rule inside reads as
 * unconditional.
 */
export function readRules(cssText: string): Rule[] {
  const src = stripCssComments(cssText);
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
      const declarations = [...body.matchAll(/(?:^|;)\s*([-a-zA-Z]+)\s*:([^;]*)/g)]
        .map((m) => ({ property: (m[1] ?? "").toLowerCase(), value: (m[2] ?? "").trim() }))
        .filter((d) => !d.property.startsWith("--"));
      const media = atStack.find((a) => a.prelude.startsWith("@media"))?.prelude ?? null;
      for (const selector of splitSelectorList(prelude)) {
        rules.push({ selector, properties, declarations, start: i, inMedia: media });
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
