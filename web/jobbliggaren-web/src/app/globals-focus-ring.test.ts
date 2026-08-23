import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1450 — a rule may suppress the focus outline only if it draws a replacement ring.
 *
 * Reads source text; it does not render. What it guards is a cascade fact no rendered test states
 * cheaply: `*:focus-visible` (the global ring) and `.jp-input` are BOTH unlayered and BOTH (0,1,0),
 * so source order alone decides, and `.jp-input` is ~1600 lines later. `outline: 0` there therefore
 * won silently — no error, no warning, and `focus:outline-none`-style specificity reasoning does not
 * apply because unlayered CSS beats every `@layer utilities` rule regardless of specificity.
 *
 * The distinction this guard encodes is the one the sweep found: suppression is legitimate when the
 * selector draws its own ring, and a defect when it does not. Three homes suppress and replace —
 * `.jp-hero__input` (its `overflow: hidden` row clips an outward ring, so it draws an inward one),
 * `.jp-app__rowlink` and `.jp-apptable__rowlink` (both move the ring to the stretched `::after`).
 * Three homes suppressed and replaced nothing: `.jp-input`, `.jp-sortfield__select` and
 * `.jp-appcontrols__input`, whose residual indicator measured 2.16:1 (border state change) and
 * 1.28:1 (glow) against WCAG 2.4.11's 3:1 floor. Those three suppressions are deleted; this guard is
 * what stops a fourth being written.
 *
 * Fail-closed on three axes. Declarations are collected from BOTH stylesheet entry points, not just
 * `globals.css`, because the class sweep in `scripts/guard-css.mjs` already treats them as one
 * universe and a suppression moved into `app.css` must not read as a repair. A declaration that ends
 * at `}` rather than `;` is collected too, so the last declaration in a block cannot hide. And the
 * suppressor list is asserted by name: a new one is a failure of this guard telling you it has not
 * been taught about the home, never a silent pass.
 */
const HERE = dirname(fileURLToPath(import.meta.url));
const SHEETS = ["globals.css", "(app)/app.css"] as const;

interface Decl {
  sheet: string;
  selector: string;
  prop: string;
  value: string;
}

/**
 * Selector/declaration pairs, mirroring the char-wise scan in `scripts/guard-css.mjs` so both
 * guards read the stylesheet the same way. Comments are stripped first: rule text left behind in a
 * comment must not stand in for a live rule.
 */
function declarations(sheet: string): Decl[] {
  const css = readFileSync(resolve(HERE, sheet), "utf-8").replace(
    /\/\*[\s\S]*?\*\//g,
    "",
  );
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
        prop: buf.slice(0, colon).trim(),
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

/** The element a selector targets, with every pseudo-class and pseudo-element removed. */
const base = (selector: string) =>
  selector.replace(/::?[a-zA-Z-]+(\([^)]*\))?/g, "").trim();

const suppresses = (d: Decl) =>
  d.prop === "outline" && /^(0|none)$/.test(d.value);

const drawsRing = (d: Decl) =>
  d.prop === "outline" && !/^(0|none)$/.test(d.value) && d.value.includes("px");

const SUPPRESSORS = DECLS.filter(suppresses);

describe("focus ring — suppression always carries a replacement (#1450)", () => {
  it("has exactly the suppressors this guard is about", () => {
    expect(
      SUPPRESSORS.map((d) => d.selector).sort(),
      `A suppressor this guard has not been taught about. That is not necessarily a defect — ` +
        `it is this guard refusing to let one pass unread. Add it here once you have checked it ` +
        `draws its own ring.`,
    ).toEqual([
      ".jp-app__rowlink:focus-visible",
      ".jp-apptable__rowlink:focus-visible",
      ".jp-hero__input",
    ]);
  });

  it.each(SUPPRESSORS.map((d) => [d.selector, d] as const))(
    "%s draws a replacement ring on its own element",
    (_selector, d) => {
      const replacement = DECLS.filter(
        (r) => drawsRing(r) && base(r.selector) === base(d.selector),
      );
      expect(
        replacement.map((r) => `${r.selector} { outline: ${r.value} }`),
        `${d.selector} sets \`outline: ${d.value}\` and no rule on ${base(d.selector)} draws one ` +
          `back. That is WCAG 2.4.7 — the a11y skill states it as "never outline: none without a ` +
          `replacement". This is the shape #1450 measured on .jp-input, .jp-sortfield__select and ` +
          `.jp-appcontrols__input, where the residual border/glow read 2.16:1 and 1.28:1 against a ` +
          `3:1 floor. Draw a ring (inward via outline-offset: -2px if an ancestor clips it), do not ` +
          `re-suppress.`,
      ).not.toHaveLength(0);
    },
  );

  it("keeps the global ring the deletions rely on", () => {
    const globalRing = DECLS.filter(
      (d) => d.selector === "*:focus-visible" && drawsRing(d),
    );
    expect(
      globalRing,
      `\`*:focus-visible { outline: … }\` is what the three deleted suppressions uncovered. ` +
        `Remove or rename it and .jp-input, .jp-sortfield__select and .jp-appcontrols__input go ` +
        `back to having no focus outline at all, with every other assertion in this file green.`,
    ).not.toHaveLength(0);
  });
});
