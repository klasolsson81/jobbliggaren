#!/usr/bin/env node
/**
 * CSS typography/color literal regression guard (#549 WS5, CTO D3).
 *
 * Fails (exit 1) when a target CSS file contains a hardcoded font-size or color
 * literal in a normal declaration - the doctrine is zero literals: everything
 * resolves through the token system (jobbpilot-design-tokens skill / ADR 0052,
 * 0068). Allowed:
 *   - custom-property definitions (--jp-*, --text-*, ...) anywhere: token
 *     definitions and scoped token re-pins are the sanctioned pattern
 *   - anything inside @theme blocks (Tailwind theme tokens)
 *   - declarations carrying a `guard-allow: <reason>` comment on the same
 *     line or the line directly above (the documented-exception idiom;
 *     the reason is mandatory - reject empty reasons in review)
 *
 * font-weight/line-height are OBSERVE-ONLY in v1 (CTO D3): flip to blocking
 * only via an explicit Klas ratchet (CLAUDE.md 2.5 discipline). That note scopes
 * to those two properties ONLY — do not generalise it. The literal sweep and the
 * existence sweeps below all BLOCK (CTO bind 2026-07-25): they are deterministic
 * resolver checks with a clean baseline, not measurement-based gates that can
 * flake, so 2.5's observe-only clause (which scopes to the ADR 0045 perf
 * budgets) does not apply to them.
 *
 * Usage: node scripts/guard-css.mjs <path-to-css> [<path-to-css> ...] [--json]
 * The CSS paths are the literal sweep's targets AND the set of stylesheets the
 * existence sweeps treat as the definition universe; the class sweep additionally
 * reads all of src/ (see below), so it needs no argument of its own.
 * Wired into: pre-commit (web gates) + CI frontend job (#549 WS5).
 * Guards every split CSS entry point — globals.css + (app)/app.css (#750);
 * add each new per-route-group split file here as it is introduced.
 */
import { readFileSync } from "node:fs";

const asJson = process.argv.includes("--json");
const files = process.argv.slice(2).filter((a) => a !== "--json");

// A value is a literal offence only if digits remain after every var(...) reference
// is stripped (catches `13px`, `clamp(40px, 5vw, 56px)`; passes `var(--text-h3)`).
const hasRawNumber = (value) => /\d/.test(value.replace(/var\([^)]*\)/g, ""));
const COLOR_PROPS =
  /^(color|background|background-color|border(-\w+)*-color|border(-top|-right|-bottom|-left)?|outline|outline-color|fill|stroke|box-shadow|text-decoration-color|caret-color|accent-color)$/;
const COLOR_LITERAL = /#[0-9a-fA-F]{3,8}\b|rgba?\(\s*\d|hsla?\(\s*\d/;

function checkFile(file) {
  const src = readFileSync(file, "utf-8");

  // Strip comments but keep line structure AND remember which lines carried a
  // guard-allow marker.
  const allowLines = new Set();
  let out = "";
  let i = 0;
  let line = 1;
  while (i < src.length) {
    if (src[i] === "/" && src[i + 1] === "*") {
      const end = src.indexOf("*/", i + 2);
      const body = src.slice(i, end === -1 ? src.length : end + 2);
      if (/guard-allow\s*:/.test(body)) {
        allowLines.add(line);
        // multi-line comment: mark every line it spans
        let l = line;
        for (const ch of body) if (ch === "\n") allowLines.add(++l);
      }
      for (const ch of body) {
        out += ch === "\n" ? "\n" : " ";
        if (ch === "\n") line++;
      }
      i += body.length;
    } else {
      out += src[i];
      if (src[i] === "\n") line++;
      i++;
    }
  }

  const lines = out.split("\n");
  const findings = [];
  const stack = []; // selector stack
  let buf = ""; // accumulates selector text between } or ; and {

  const inTheme = () => stack.some((s) => s.startsWith("@theme"));

  const checkDecl = (decl, lineNo) => {
    if (!decl || stack.length === 0) return;
    const colon = decl.indexOf(":");
    if (colon === -1) return;
    const prop = decl.slice(0, colon).trim();
    const value = decl.slice(colon + 1).trim();
    if (prop.startsWith("--")) return; // token definition — allowed anywhere
    if (inTheme()) return;
    if (allowLines.has(lineNo) || allowLines.has(lineNo - 1)) return;
    const selector = stack[stack.length - 1] || "?";

    if (prop === "font-size" && hasRawNumber(value)) {
      findings.push({ file, line: lineNo, selector, decl: `${prop}: ${value}`, rule: "font-size-literal" });
    }
    if (COLOR_PROPS.test(prop) && COLOR_LITERAL.test(value)) {
      findings.push({ file, line: lineNo, selector, decl: `${prop}: ${value}`, rule: "color-literal" });
    }
  };

  for (let n = 0; n < lines.length; n++) {
    const ln = lines[n];
    // Process char-wise for braces; declarations end at ';'
    let seg = "";
    for (let c = 0; c < ln.length; c++) {
      const ch = ln[c];
      if (ch === "{") {
        stack.push((buf + seg).trim());
        buf = "";
        seg = "";
      } else if (ch === "}") {
        stack.pop();
        buf = "";
        seg = "";
      } else if (ch === ";") {
        checkDecl((buf + seg).trim(), n + 1);
        buf = "";
        seg = "";
      } else {
        seg += ch;
      }
    }
    buf += seg + " ";
  }

  return findings;
}

/* ------------------------------------------------------------------------- *
 * Existence sweeps (#877) — a design identifier that is REFERENCED but never
 * DEFINED renders "fine" and does nothing, which is the failure class this
 * house chases hardest. Three shipped instances motivated this guard: an
 * undefined `--jp-surface-1` (background fell to transparent → the chip had no
 * surface), a `className="jp-link"` with zero selectors (Preflight made it
 * identical grey body text — the only offered next step in an empty state), and
 * `--jp-surface-2` against canvas at 1,00:1 (the tile was invisible).
 *
 * Neither the literal guard above nor vitest can see this: jsdom applies no CSS,
 * so a test cannot tell a live class from a dead one. Two sweeps, both pure
 * string scans (no CSS parser, no new dependency):
 *
 *   1. CSS → CSS   every `var(--jp-*)` must have a `--jp-*:` definition.
 *   2. TSX → CSS   every `jp-*` class name in source must have a selector.
 *
 * Exclusions are SHAPE-based, never name-lists — a name list rots, a shape
 * keeps telling the truth. Each was found empirically while calibrating this
 * guard against the tree, and each is a false positive a naive sweep produces:
 *
 *   - `--jp-*` token references are not class names. `bg-(--jp-accent-700)`
 *     (Tailwind 4 arbitrary property) and `var(--jp-x)` both contain the
 *     substring `jp-accent-700`; a class scan that ignores the leading `--`
 *     reports the token as a missing class.
 *   - class names carry `_` and uppercase: `jp-apptable__cell--role`,
 *     `jp-statusDot--ok`. A `[a-z0-9-]` character class truncates them at the
 *     separator and reports the truncated prefix as missing.
 *   - a fragment immediately followed by `${` is a template-literal PREFIX
 *     (`jp-pill--${tone}`), not a complete class. The composed name cannot be
 *     verified statically; the count of skipped prefixes is REPORTED rather
 *     than silently dropped.
 *   - the fallback form `var(--x, <fallback>)` degrades gracefully by
 *     construction and is deliberately allowed (#877 says so explicitly). It is
 *     load-bearing here: `--jp-mallcard-accent` is a data channel written inline
 *     via the `style` attribute from a backend value, so it is never declared in
 *     a stylesheet.
 *
 * Both sweeps honour the same `guard-allow: <reason>` comment idiom as above.
 * ------------------------------------------------------------------------- */

const SRC_DIR = new URL("../src/", import.meta.url);
const JP_IDENT = /(?<!-)\bjp-[A-Za-z0-9_-]+/g;
const TOKEN_DEF = /(--jp-[A-Za-z0-9_-]+)\s*:/g;
// `var(--jp-x)` with NO comma before the closing paren — the fallback form is allowed.
const TOKEN_USE_NO_FALLBACK = /var\(\s*(--jp-[A-Za-z0-9_-]+)\s*\)/g;
const SELECTOR_CLASS = /\.(jp-[A-Za-z0-9_-]+)/g;

/** Strip comments, keeping line structure, and collect guard-allow lines. */
function stripComments(src, { lineComments = false } = {}) {
  const allowLines = new Set();
  let out = "";
  let i = 0;
  let line = 1;
  while (i < src.length) {
    const isBlock = src[i] === "/" && src[i + 1] === "*";
    const isLine = lineComments && src[i] === "/" && src[i + 1] === "/";
    if (isBlock || isLine) {
      const end = isBlock ? src.indexOf("*/", i + 2) : src.indexOf("\n", i + 2);
      const body = src.slice(i, end === -1 ? src.length : isBlock ? end + 2 : end);
      if (/guard-allow\s*:/.test(body)) {
        let l = line;
        allowLines.add(l);
        for (const ch of body) if (ch === "\n") allowLines.add(++l);
      }
      for (const ch of body) {
        out += ch === "\n" ? "\n" : " ";
        if (ch === "\n") line++;
      }
      i += body.length;
    } else {
      out += src[i];
      if (src[i] === "\n") line++;
      i++;
    }
  }
  return { text: out, allowLines };
}

function lineOf(text, index) {
  return text.slice(0, index).split("\n").length;
}

async function collectSourceFiles(dir, acc = []) {
  const { readdir } = await import("node:fs/promises");
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const child = new URL(entry.name + (entry.isDirectory() ? "/" : ""), dir);
    if (entry.isDirectory()) {
      await collectSourceFiles(child, acc);
    } else if (
      /\.(ts|tsx)$/.test(entry.name) &&
      !/\.(test|spec)\.(ts|tsx)$/.test(entry.name) &&
      !entry.name.endsWith(".d.ts")
    ) {
      acc.push(child);
    }
  }
  return acc;
}

/**
 * Yield every `className=` value in a source file, as {value, offset}.
 *
 * Scoped to the className position ON PURPOSE. The `jp-` prefix is used in this
 * codebase for more than classes — `id`/`aria-labelledby` pairs
 * (`jp-modal-desc`, `jp-source-ad-title`), localStorage keys (`jp-theme`,
 * `jp-oversikt-notice-prefs`) and custom event names (`jp-theme-change`) all
 * carry it. A file-wide scan reports every one of those as a missing class, and
 * a gate with false positives earns an allowlist that makes it decorative.
 *
 * A brace-balanced read of the `{...}` form (rather than a regex) is what makes
 * `className={cn("jp-x", cond && "jp-y")}` and nested ternaries work: every
 * string inside the className expression is a class-name candidate, whatever the
 * helper composing them.
 */
function* classExpressions(text) {
  const marker = /\bclassName\s*=\s*/g;
  for (const m of marker.exec(text) ? [...text.matchAll(marker)] : []) {
    let i = m.index + m[0].length;
    const opener = text[i];
    if (opener === '"' || opener === "'") {
      const end = text.indexOf(opener, i + 1);
      if (end === -1) continue;
      yield { value: text.slice(i + 1, end), offset: i + 1 };
    } else if (opener === "{") {
      let depth = 0;
      let j = i;
      for (; j < text.length; j++) {
        if (text[j] === "{") depth++;
        else if (text[j] === "}") {
          depth--;
          if (depth === 0) break;
        }
      }
      yield { value: text.slice(i + 1, j), offset: i + 1 };
    }
  }
}

/**
 * The invariant is per ELEMENT, not per identifier: a `jp-*` name without a rule
 * is only reported when NO `jp-*` name on the same element resolves either.
 *
 * That is the shape of the damage #877 was filed for. Its three shipped
 * instances each left the element with nothing: `className="jp-link"` alone on an
 * anchor rendered as identical grey body text (Preflight resets link colour), and
 * an undefined `--jp-surface-1`/`--jp-surface-2` left the chip with no surface.
 * The failure was never "an identifier lacks a rule" — it was "the element ended
 * up unstyled while the source claimed otherwise".
 *
 * Deriving it from the damage rather than from the identifier is also what keeps
 * the guard honest. Three patterns in this tree lack a rule on purpose, and all
 * three fall out of this one rule instead of needing an exemption each:
 *   - `className="jp-card jp-guest-resume"` — a container hook; the weight comes
 *     from `.jp-card` and from `.jp-guest-resume__title` etc.
 *   - `className="jp-job__match jp-job__match--neutral"` — BEM base plus styled
 *     modifier.
 *   - `<td className="jp-apptable__cell jp-apptable__cell--role">` — semantic
 *     column markers on a base-styled cell; five of six `__col--*` carry a width
 *     and `--role` deliberately absorbs the remainder, so deleting them would
 *     break the symmetry that makes the row and colgroup readable.
 *
 * An enumeration of those three would rot. This rule keeps telling the truth, and
 * still fails every instance that motivated the issue.
 */
function anyClassResolves(names, definedClasses) {
  return names.some((n) => definedClasses.has(n));
}

async function checkExistence() {
  // With no stylesheet, the definition universe is EMPTY and every `jp-*` name in
  // the app "does not resolve" — the sweep would report hundreds of violations
  // that are all artefacts of its own missing input. Refuse instead: a guard that
  // cannot see the truth must say so, not invent a verdict.
  if (files.length === 0) {
    console.error(
      "guard-css: no stylesheet given. The existence sweeps need at least one CSS " +
        "file to know what is defined.\n" +
        "Usage: node scripts/guard-css.mjs <path-to-css> [<path-to-css> ...] [--json]"
    );
    process.exit(2);
  }

  const existence = [];

  // ---- what the stylesheets DEFINE (union across every guarded entry point:
  // a token declared in globals.css is legitimately consumed from app.css) ----
  const definedTokens = new Set();
  const definedClasses = new Set();
  const cssTexts = new Map();
  for (const file of files) {
    const { text, allowLines } = stripComments(readFileSync(file, "utf-8"));
    cssTexts.set(file, { text, allowLines });
    for (const m of text.matchAll(TOKEN_DEF)) definedTokens.add(m[1]);
    for (const m of text.matchAll(SELECTOR_CLASS)) definedClasses.add(m[1]);
  }

  // ---- sweep 1: CSS → CSS ----
  for (const [file, { text, allowLines }] of cssTexts) {
    for (const m of text.matchAll(TOKEN_USE_NO_FALLBACK)) {
      if (definedTokens.has(m[1])) continue;
      const line = lineOf(text, m.index);
      if (allowLines.has(line) || allowLines.has(line - 1)) continue;
      existence.push({
        file,
        line,
        selector: (text.split("\n")[line - 1] ?? "").trim().slice(0, 90),
        decl:
          `var(${m[1]}) — undefined, so the declaration is invalid at computed-value ` +
          `time and silently does nothing. Define it, use the fallback form ` +
          `var(${m[1]}, <fallback>), or add \`guard-allow: <reason>\`.`,
        rule: "undefined-token",
      });
    }
  }

  // ---- sweep 2: TSX → CSS ----
  let dynamicPrefixes = 0;
  for (const url of await collectSourceFiles(SRC_DIR)) {
    const raw = readFileSync(url, "utf-8");
    if (!raw.includes("jp-")) continue;
    const { text, allowLines } = stripComments(raw, { lineComments: true });

    for (const { value, offset } of classExpressions(text)) {
      const found = [];
      for (const m of value.matchAll(JP_IDENT)) {
        const name = m[0];
        // A template-literal prefix (`jp-pill--${tone}`) is not a complete class.
        if (value.startsWith("${", m.index + name.length)) {
          dynamicPrefixes += 1;
          continue;
        }
        found.push({ name, index: m.index });
      }
      // Per-element check: if ANY jp-* name on this element resolves, the element
      // is styled and the rest are semantic markers within an engaged family.
      if (found.length === 0 || anyClassResolves(found.map((f) => f.name), definedClasses)) continue;
      // The element's own class string, so the message is self-service: a
      // blocking gate that does not show the way out is a flaky gate in practice.
      const element = value.replace(/\s+/g, " ").trim().slice(0, 90);
      for (const { name, index } of found) {
        const line = lineOf(text, offset + index);
        if (allowLines.has(line) || allowLines.has(line - 1)) continue;
        existence.push({
          file: url.pathname.replace(/^\/([A-Za-z]:)/, "$1"),
          line,
          selector: element,
          decl:
            `${name} — no .${name} selector in any guarded stylesheet, and no other jp-* ` +
            `class on this element resolves. Define it, drop it, or add ` +
            `\`guard-allow: <reason>\` on the line above.`,
          rule: "undefined-class",
        });
      }
    }
  }

  return { existence, dynamicPrefixes };
}

const findings = files.flatMap(checkFile);
const { existence, dynamicPrefixes } = await checkExistence();
findings.push(...existence);

if (asJson) {
  console.log(JSON.stringify(findings, null, 1));
} else {
  for (const f of findings)
    console.log(`${f.file}:${String(f.line).padStart(5)}  [${f.rule}]  ${f.selector}  →  ${f.decl.slice(0, 80)}`);
  // Report what the sweep could NOT verify, so a skipped class never reads as a
  // checked one ("no silent caps").
  if (dynamicPrefixes > 0)
    console.log(
      `\nnote: ${dynamicPrefixes} template-literal class prefix(es) skipped — composed names cannot be verified statically.`
    );
  console.log(`\n${findings.length} violation(s).`);
}
process.exit(findings.length ? 1 : 0);
