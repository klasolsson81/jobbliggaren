import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1450 — a rule may switch the focus outline off only if it draws a replacement ring.
 *
 * Reads source text; it does not render. What it guards is a cascade fact no rendered test states
 * cheaply: `*:focus-visible` (the global ring) and `.jp-input` were BOTH unlayered and BOTH (0,1,0),
 * so source order alone decided, and `.jp-input` sat ~1600 lines later. Its `outline: 0` therefore
 * won silently — no error, no warning, and `focus:outline-none`-style specificity reasoning does not
 * apply, because unlayered CSS beats every `@layer utilities` rule regardless of specificity.
 *
 * The distinction encoded here is the one the sweep found: switching the outline off is legitimate
 * when the element draws its own ring on a focus state, and a defect when it does not. Three homes
 * switch off and replace — `.jp-hero__input` (its row has `overflow: hidden`, which clips an outward
 * ring, so it draws an inward one), `.jp-app__rowlink` and `.jp-apptable__rowlink` (both move the
 * ring to the stretched `::after`). Three switched off and replaced nothing: `.jp-input`,
 * `.jp-sortfield__select` and `.jp-appcontrols__input`. Measured for #1450 on 2026-08-23, their
 * residual indicator read 2.16:1 (border state change) and 1.28:1 (glow, 1.29:1 on the third) against
 * WCAG 2.4.11's 3:1 floor. Those three are deleted; this guard is what stops a fourth being written.
 *
 * Detection is deliberately wider than the two literals the defect happened to use, because the form
 * someone reaches for when fighting a cascade is `!important`, and both idioms already exist in this
 * stylesheet (`@apply` at globals.css:632, `!important` in the dark data-slot block). So: property
 * names are lowercased (CSS property names are case-insensitive), `!important` is stripped before
 * matching, zero-with-unit and `transparent` count as off, the three longhands are read, and `@apply`
 * — which carries no colon and so never reaches a declaration — is swept separately.
 *
 * Fail-closed on the axes that were live holes when this was first written (design-reviewer Major 1,
 * code-reviewer Majors 1-2, PR #1457): a replacement must sit on a focus state and carry a non-zero
 * width AND a style keyword, so neither `outline: 0px` nor a `:hover`-only ring can absolve a
 * suppression. Declarations are collected from BOTH stylesheet entry points, because a suppression
 * moved into `app.css` must not read as a repair. Comments are stripped first, so rule text left
 * behind in a comment cannot stand in for a live rule. A declaration ending at `}` rather than `;` is
 * collected — note that `scripts/guard-css.mjs` does NOT do this (it clears its buffer on `}`
 * without flushing), so the two scanners genuinely differ and neither is a copy of the other.
 */
const HERE = dirname(fileURLToPath(import.meta.url));
const SHEETS = ["globals.css", "(app)/app.css"] as const;

interface Decl {
  sheet: string;
  selector: string;
  /** Enclosing at-rules and selectors, outermost first — lets a rule be tested for layer membership. */
  ancestry: readonly string[];
  prop: string;
  value: string;
}

function read(sheet: string): string {
  return readFileSync(resolve(HERE, sheet), "utf-8").replace(
    /\/\*[\s\S]*?\*\//g,
    "",
  );
}

/** Selector/declaration pairs, with the block stack that encloses each one. */
function declarations(sheet: string): Decl[] {
  const css = read(sheet);
  const out: Decl[] = [];
  const stack: string[] = [];
  let buf = "";

  const flush = () => {
    const selector = stack[stack.length - 1];
    const colon = buf.indexOf(":");
    if (selector && colon !== -1) {
      out.push({
        sheet,
        selector,
        ancestry: [...stack],
        prop: buf.slice(0, colon).trim().toLowerCase(),
        value: buf.slice(colon + 1).trim(),
      });
    }
    buf = "";
  };

  for (const ch of css) {
    if (ch === "{") {
      stack.push(buf.trim());
      buf = "";
    } else if (ch === "}") {
      flush();
      stack.pop();
      buf = "";
    } else if (ch === ";") {
      flush();
    } else {
      buf += ch;
    }
  }
  return out;
}

const DECLS = SHEETS.flatMap(declarations);

const withoutBang = (value: string) =>
  value.replace(/!\s*important\s*$/i, "").trim();

/** A length that renders nothing: bare zero, or zero with any unit. */
const ZERO = /^0(px|em|rem|pt|%)?$/i;
const OFF_TOKEN = /^(none|transparent)$/i;
const STYLE_KEYWORD =
  /^(solid|dashed|dotted|double|groove|ridge|inset|outset|auto)$/i;
const NONZERO_LENGTH = /^[0-9.]+(px|em|rem|pt)$/i;
const FOCUS_STATE = /:focus(-visible|-within)?\b/;

/** Every way a declaration switches the outline off — longhands and `!important` included. */
function switchesOff(d: Decl): boolean {
  const v = withoutBang(d.value);
  const tokens = v.split(/\s+/).filter(Boolean);
  switch (d.prop) {
    case "outline":
      // The shorthand is off if ANY component switches it off: `0`, `none`,
      // and `2px solid transparent` all render nothing.
      return tokens.some((t) => ZERO.test(t) || OFF_TOKEN.test(t));
    case "outline-style":
      return tokens.some((t) => /^none$/i.test(t));
    case "outline-width":
      return tokens.some((t) => ZERO.test(t));
    case "outline-color":
      return tokens.some((t) => /^transparent$/i.test(t));
    default:
      return false;
  }
}

/**
 * A real ring: on a focus state, with a non-zero width AND a style keyword. `outline: 0px` and
 * `outline: 1px` (style defaults to `none`) both render nothing and must not absolve anything.
 */
function drawsRing(d: Decl): boolean {
  if (d.prop !== "outline" || switchesOff(d)) return false;
  if (!FOCUS_STATE.test(d.selector)) return false;
  const tokens = withoutBang(d.value).split(/\s+/).filter(Boolean);
  return (
    tokens.some((t) => NONZERO_LENGTH.test(t)) &&
    tokens.some((t) => STYLE_KEYWORD.test(t))
  );
}

/** Strip pseudo-classes/elements, paren-aware so `:not(.x:has(.y))` leaves nothing behind. */
function stripPseudo(selector: string): string {
  const at = (i: number): string => selector.charAt(i);
  let out = "";
  for (let i = 0; i < selector.length; i++) {
    if (at(i) !== ":") {
      out += at(i);
      continue;
    }
    i++;
    if (at(i) === ":") i++;
    while (i < selector.length && /[a-zA-Z-]/.test(at(i))) i++;
    if (at(i) === "(") {
      let depth = 0;
      for (; i < selector.length; i++) {
        if (at(i) === "(") depth++;
        else if (at(i) === ")" && --depth === 0) break;
      }
    } else {
      i--;
    }
  }
  return out.trim();
}

/** The elements a selector targets — one per comma branch, so a branch cannot absolve its sibling. */
const targets = (selector: string): string[] =>
  selector
    .split(",")
    .map((branch) => stripPseudo(branch).trim())
    .filter(Boolean);

const SUPPRESSORS = DECLS.filter(switchesOff);

describe("focus ring — switching it off always carries a replacement (#1450)", () => {
  it("has exactly the suppressors this guard is about", () => {
    expect(
      SUPPRESSORS.map((d) => `${d.selector} { ${d.prop} }`).sort(),
      `A suppressor this guard has not been taught about. That is not necessarily a defect — ` +
        `it is this guard refusing to let one pass unread. Add it here once you have checked it ` +
        `draws its own ring.`,
    ).toEqual([
      ".jp-app__rowlink:focus-visible { outline }",
      ".jp-apptable__rowlink:focus-visible { outline }",
      ".jp-hero__input { outline }",
    ]);
  });

  it.each(SUPPRESSORS.map((d) => [d.selector, d] as const))(
    "%s draws a replacement ring on its own element",
    (_selector, d) => {
      const mine = targets(d.selector);
      const replacement = DECLS.filter(
        (r) =>
          drawsRing(r) && targets(r.selector).some((t) => mine.includes(t)),
      );
      expect(
        replacement.map((r) => `${r.selector} { outline: ${r.value} }`),
        `${d.selector} sets \`${d.prop}: ${d.value}\` and no focus-state rule on ` +
          `${mine.join(", ")} draws a ring back. That is WCAG 2.4.7 — the a11y skill states it as ` +
          `"never outline: none without a replacement", with a 3:1 indicator floor. This is the ` +
          `shape #1450 measured on .jp-input, .jp-sortfield__select and .jp-appcontrols__input. ` +
          `Draw a ring (inward via outline-offset: -2px if an ancestor clips it); do not re-suppress.`,
      ).not.toHaveLength(0);
    },
  );

  it("keeps the global ring the deletions rely on, and keeps it unlayered", () => {
    const globalRing = DECLS.filter(
      (d) => d.selector === "*:focus-visible" && drawsRing(d),
    );
    expect(
      globalRing,
      `\`*:focus-visible { outline: … }\` is what the three deleted suppressions uncovered. ` +
        `Remove or rename it and .jp-input, .jp-sortfield__select and .jp-appcontrols__input go ` +
        `back to having no focus outline at all, with every other assertion in this file green.`,
    ).not.toHaveLength(0);

    for (const rule of globalRing) {
      expect(
        rule.ancestry.filter((a) => a.startsWith("@layer")),
        `The global ring must stay unlayered. Inside @layer it loses to every unlayered .jp-* rule ` +
          `regardless of specificity — which is the exact cascade #1450 was about, re-created from ` +
          `the other side.`,
      ).toHaveLength(0);
    }
  });

  it("no @apply switches the outline off", () => {
    // `@apply` carries no colon, so it never becomes a declaration and the sweep above cannot see
    // it. `@apply outline-none` is the Tailwind spelling of the same defect; this sheet already
    // uses `@apply` (globals.css:632), so the idiom is reachable rather than hypothetical.
    const offending = SHEETS.flatMap((sheet) =>
      read(sheet)
        .split("\n")
        .map((line, i) => ({ sheet, line: line.trim(), no: i + 1 }))
        .filter(
          ({ line }) =>
            line.includes("@apply") &&
            /\boutline-(none|hidden|0)\b/.test(line),
        ),
    );
    expect(
      offending.map((o) => `${o.sheet}:${o.no} ${o.line}`),
      `An @apply that switches the outline off. It is invisible to the declaration sweep above ` +
        `(no colon), so it gets its own check rather than a silent pass.`,
    ).toEqual([]);
  });
});
