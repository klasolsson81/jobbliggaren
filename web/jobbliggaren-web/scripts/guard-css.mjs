/*
 * NO SHEBANG. Vite's SSR transform hoists the node: interop onto line 1, in
 * front of a shebang, and the file then fails to PARSE — so importing it from
 * a test yields "no tests" rather than a failure anyone reads as one. The 15
 * tests in guard-css.test.mjs silently did not run on Windows because of it
 * (#1056 re-review B2). The only call site is package.json's
 * `node scripts/guard-css.mjs`, so the shebang was decorative.
 */
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
import { pathToFileURL } from "node:url";

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

/** Every stylesheet under src/ — the definition universe for both sweeps. */
async function collectStylesheets(dir, acc = []) {
  const { readdir } = await import("node:fs/promises");
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const child = new URL(entry.name + (entry.isDirectory() ? "/" : ""), dir);
    if (entry.isDirectory()) await collectStylesheets(child, acc);
    else if (entry.name.endsWith(".css")) acc.push(child);
  }
  return acc;
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
  // `[A-Za-z]*[Cc]lassName` so the prop idiom is swept too: `triggerClassName`,
  // `wrapperClassName`, `inputClassName` all carry jp-* names and a `\bclassName`
  // pattern never matches them (capital C). Skipping a whole idiom silently is
  // exactly what the template-prefix counter below exists to prevent.
  //
  // NOTE: no `marker.exec(text) ? ... : []` guard here. `exec` advances
  // `lastIndex`, and `matchAll` COPIES `lastIndex` from the source regex
  // (RegExp.prototype[@@matchAll]), so such a guard silently drops the FIRST
  // match in every file. It hid two real violations in this guard's own
  // introducing PR — one of them the same class name being removed three lines
  // further down the same file. `matchAll` already yields nothing when there is
  // no match, so the guard was redundant as well as wrong.
  const marker = /\b[A-Za-z]*[Cc]lassName\s*=\s*/g;
  for (const m of text.matchAll(marker)) {
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
  // The definition universe is DISCOVERED, not listed. The argument list names
  // the literal sweep's targets; using it here too would mean a future
  // route-group CSS split that nobody remembered to add makes every class it
  // defines a false positive across the whole tree — and, since this gate blocks,
  // stops every session's commits at once. Worktree isolation does not help
  // there: the incomplete list would be committed. A glob keeps the universe
  // true by construction, which is the same "shapes, not lists" rule the
  // exclusions follow.
  const definedTokens = new Set();
  const definedClasses = new Set();
  const cssTexts = new Map();
  for (const file of await collectStylesheets(SRC_DIR)) {
    const { text, allowLines } = stripComments(readFileSync(file, "utf-8"));
    cssTexts.set(file.pathname ? file.pathname.replace(/^\/([A-Za-z]:)/, "$1") : file, {
      text,
      allowLines,
    });
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

/* ------------------------------------------------------------------------- *
 * INVERSE existence sweep (#1056) — DEFINED but consumed by nothing.
 *
 * The mirror of the two sweeps above, and the asymmetry it closes is the point:
 * since #877 the build breaks on an identifier that is REFERENCED but never
 * DEFINED, and had no check whatsoever for the reverse. Both are the same
 * defect — an identifier that does not connect — and #1054 measured what the
 * missing half let accumulate: 86 dead classes and 19 dead tokens.
 *
 * Three things make this harder than "definitions minus references", and each
 * is a measured false verdict, not a hypothetical:
 *
 *   1. TRANSITIVITY. `--jp-density` WAS referenced — by the three calc() tokens
 *      derived from it — and those three by nothing. Internally consistent,
 *      externally orphaned; a one-hop sweep passes it. Tokens are therefore
 *      resolved as a reachability graph from real roots, not a set difference.
 *      (#1055 removed that instance; the mechanism remains.)
 *   2. COMPOSITION. `jp-statusDot--${tone}` never appears as a complete
 *      identifier, so complete-identifier matching — the discipline that stops
 *      `jp-attention` reading as alive because `jp-attentionqueue` exists —
 *      reports all six variants as dead. Measured on #1054: 13 LIVE classes
 *      across four families. This direction fails DANGEROUSLY (deleting them
 *      strips colour from every status dot in every table), so any name behind
 *      a captured `${` prefix is never reportable as dead.
 *   3. A REFERENCE IS NOT ALWAYS A CONSUMER. `.jp-land-top` survived #1054's
 *      sweep on `expect(...querySelector(".jp-land-top")).toBeNull()` — an
 *      assertion that it is ABSENT — plus a comment; `.jp-land-top__stat__num`
 *      on a single code comment, itself the written justification for loading a
 *      font weight and false twice over. Comments are stripped from the
 *      reference side; test-only references are REPORTED rather than trusted,
 *      because a static sweep cannot tell a positive assertion from a negative
 *      one (`jp-job__newflag` is dead and `jp-pill--warning` is alive, and both
 *      appear only inside a negated assertion).
 * ------------------------------------------------------------------------- */

/**
 * Strip JS/TS comments WITHOUT corrupting string and template literals.
 *
 * `stripComments(..., {lineComments: true})` above blanks the rest of the line
 * at the `//` inside `"https://…"`. On the reference side that would hide a real
 * class reference sharing such a line and report a LIVE class as dead — the one
 * error direction this sweep must never make. Offsets are preserved.
 */
function stripJs(src) {
  let out = "";
  let i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    const d = src[i + 1];
    if (c === "/" && d === "*") {
      const end = src.indexOf("*/", i + 2);
      const body = src.slice(i, end === -1 ? n : end + 2);
      for (const ch of body) out += ch === "\n" ? "\n" : " ";
      i += body.length;
    } else if (c === "/" && d === "/") {
      let j = i;
      while (j < n && src[j] !== "\n") j++;
      out += " ".repeat(j - i);
      i = j;
    } else if (c === '"' || c === "'" || c === "`") {
      const quote = c;
      let j = i + 1;
      out += c;
      while (j < n) {
        if (src[j] === "\\") {
          out += src[j] + (src[j + 1] ?? "");
          j += 2;
          continue;
        }
        if (src[j] === quote) {
          out += quote;
          j++;
          break;
        }
        out += src[j];
        j++;
      }
      i = j;
    } else {
      out += c;
      i++;
    }
  }
  return out;
}

/** Split a selector list on top-level commas (not inside parens/brackets). */
function selectorBranches(sel) {
  const out = [];
  let depth = 0;
  let cur = "";
  for (const ch of sel) {
    if (ch === "(" || ch === "[") depth++;
    else if (ch === ")" || ch === "]") depth--;
    if (ch === "," && depth === 0) {
      out.push(cur);
      cur = "";
    } else cur += ch;
  }
  if (cur.trim()) out.push(cur);
  return out;
}

/**
 * Blank the contents of every (...) group, LENGTH-PRESERVING, so a functional
 * pseudo-class contributes no names.
 *
 * This is a correctness requirement, not tidiness. `:not()` INVERTS: the less
 * `.jp-ghost` is referenced, the MORE `:not(.jp-ghost) .jp-target` matches — so
 * treating it as a required ancestor is exactly backwards. `:is()`/`:where()`
 * are disjunctions where a conjunction would be wrong. And `:has(+ .jp-x)`
 * would otherwise mis-split and make `.jp-x` the subject: live today at
 * `.jp-cvupload__drop:has(+ .jp-cvupload__input:focus-visible)`.
 *
 * Both errors produce a BLOCKING false positive whose message says "unscope a
 * rule, drop the class" — i.e. it would talk someone into deleting live CSS.
 */
function blankParens(sel) {
  let out = "";
  let depth = 0;
  for (const ch of sel) {
    if (ch === "(") {
      depth++;
      out += ch;
    } else if (ch === ")") {
      depth--;
      out += ch;
    } else out += depth > 0 ? " " : ch;
  }
  return out;
}

/**
 * Split a branch into compounds on descendant/child/sibling combinators,
 * ignoring combinator characters inside (...) and [...].
 */
function selectorCompounds(branch) {
  const out = [];
  let depth = 0;
  let cur = "";
  const flush = () => {
    if (cur.trim()) out.push(cur.trim());
    cur = "";
  };
  for (const ch of branch) {
    if (ch === "(" || ch === "[") depth++;
    else if (ch === ")" || ch === "]") depth--;
    if (depth === 0 && (ch === ">" || ch === "+" || ch === "~" || /\s/.test(ch))) flush();
    else cur += ch;
  }
  flush();
  return out;
}

/**
 * The subject and required-ancestor names of ONE selector branch.
 *
 * Extracted so the CALL SITE is testable, not merely the helpers. The re-review
 * measured why that matters: deleting the two `blankParens(...)` wrappers here
 * reproduced the entire pre-fix finding set — `:is()` read as a conjunction,
 * `:not()` read inverted, `:has(+ …)` mis-attributed as subject — while all 15
 * helper unit tests stayed GREEN. Pinning the rule is not pinning its use.
 */
function branchDefinitions(branch) {
  const comps = selectorCompounds(branch);
  if (comps.length === 0) return { subjects: [], ancestors: [] };
  // Names inside :is()/:not()/:where()/:has() are blanked — see blankParens.
  const subjects = [...blankParens(comps[comps.length - 1]).matchAll(SELECTOR_CLASS)].map((m) => m[1]);
  const ancestors = comps
    .slice(0, -1)
    .flatMap((c) => [...blankParens(c).matchAll(SELECTOR_CLASS)].map((m) => m[1]));
  return { subjects, ancestors };
}

/**
 * The reference universe is NOT src/. E2E specs live in tests/e2e, outside it,
 * and a src/-only sweep reports names as dead that Playwright selects on.
 */
async function collectReferenceFiles(dir, acc = []) {
  const { readdir } = await import("node:fs/promises");
  // `out` and `build` are gitignored too: a leftover one would be READ as part
  // of the reference universe and could keep a dead name alive — silencing the gate.
  const SKIP = new Set(["node_modules", ".next", "dist", "build", "out", ".turbo", "coverage", "playwright-report", "test-results"]);
  let entries;
  try {
    entries = await readdir(dir, { withFileTypes: true });
  } catch {
    return acc;
  }
  for (const entry of entries) {
    const child = new URL(entry.name + (entry.isDirectory() ? "/" : ""), dir);
    if (entry.isDirectory()) {
      if (!SKIP.has(entry.name)) await collectReferenceFiles(child, acc);
    } else if (!entry.name.endsWith(".css")) {
      acc.push(child);
    }
  }
  return acc;
}

const JP_ANY_IDENT = /(--jp-[A-Za-z0-9_-]+|(?<!-)\bjp-[A-Za-z0-9_-]+)/g;
const TOKEN_ANY = /--jp-[A-Za-z0-9_-]+/g;
const winPath = (u) => (u.pathname ? u.pathname.replace(/^\/([A-Za-z]:)/, "$1") : String(u));

async function checkInverseExistence() {
  const inverse = [];
  const advisories = [];
  // The package root, not src/ + tests/. #1056's own trap list measured the
  // universe at "810 non-CSS files across web/ and tests/"; src/+tests/ is 64
  // files short, and scripts/visual-verify.ts already carries four jp-* names.
  const roots = [new URL("../", import.meta.url)];

  /* ---- reference side: every non-CSS file, comments stripped ---- */
  const referencedClasses = new Set();
  const referencedTokens = new Set();
  const composedPrefixes = new Set();
  const classSeenIn = new Map(); // name -> Set("prod" | "test")
  const rawOnlyClasses = new Set(); // seen before comment-stripping (see below)
  const rawOnlyTokens = new Set();
  const isTestPath = (p) => /(\.test\.|\.spec\.|\/tests?\/)/i.test(p);
  let refFileCount = 0;

  for (const root of roots) {
    for (const url of await collectReferenceFiles(root)) {
      let raw;
      try {
        raw = readFileSync(url, "utf-8");
      } catch {
        continue;
      }
      refFileCount++;
      if (!raw.includes("jp-")) continue;
      const p = url.pathname;
      const text = /\.(ts|tsx|js|jsx|mjs|cjs)$/.test(p) ? stripJs(raw) : raw;
      // `stripJs` is a scanner, not a parser: a `//` inside a regex literal
      // (`/https?:\/\//`), inside raw JSX text, or an unspaced `2/*3` can blank
      // live code. Measured over the whole tree today the delta is 68 files and
      // every removal is a genuine comment — but "no live instances" is not
      // "cannot happen", and blanking live code would report a LIVE class as
      // dead, the one error direction this sweep must never take. So any name
      // present in the RAW text is recorded too, and a name that survives only
      // there degrades to an advisory instead of a blocking finding.
      for (const m of raw.matchAll(JP_ANY_IDENT)) {
        const name = m[0];
        if (raw.startsWith("${", m.index + name.length)) continue;
        // Only an occurrence we CANNOT confidently call a comment degrades the
        // verdict. A line whose first non-space characters are `//`, `/*` or `*`
        // is a comment under any reading, so a name found only there stays a
        // blocking finding — that is the `.jp-app__statusbadge` case, and
        // keeping it blocking is the whole point of stripping comments. What
        // degrades is a name on a CODE line that the stripper nonetheless
        // blanked: the regex-literal / raw-JSX / `2/*3` shapes, where the
        // stripper may have eaten something live.
        const lineStart = raw.lastIndexOf(String.fromCharCode(10), m.index) + 1;
        const head = raw.slice(lineStart, m.index).trimStart();
        const confidentComment = head.startsWith("//") || head.startsWith("/*") || head.startsWith("*");
        if (confidentComment) continue;
        if (!name.startsWith("--jp-")) rawOnlyClasses.add(name);
        else rawOnlyTokens.add(name);
      }
      for (const m of text.matchAll(JP_ANY_IDENT)) {
        const name = m[0];
        if (text.startsWith("${", m.index + name.length)) {
          composedPrefixes.add(name);
          continue;
        }
        if (name.startsWith("--jp-")) referencedTokens.add(name);
        else {
          referencedClasses.add(name);
          if (!classSeenIn.has(name)) classSeenIn.set(name, new Set());
          classSeenIn.get(name).add(isTestPath(p) ? "test" : "prod");
        }
      }
    }
  }

  const isComposed = (n) => [...composedPrefixes].some((p) => n.startsWith(p) && n.length > p.length);
  const classIsReferenced = (n) => referencedClasses.has(n) || isComposed(n);

  /* ---- definition side: parse every guarded stylesheet into rules ---- */
  const parsed = [];
  for (const file of await collectStylesheets(SRC_DIR)) {
    const { text, allowLines } = stripComments(readFileSync(file, "utf-8"));
    const stack = [];
    let selStart = 0;
    for (let i = 0; i < text.length; i++) {
      const ch = text[i];
      if (ch === "{") {
        stack.push({ sel: text.slice(selStart, i).trim(), open: i });
        selStart = i + 1;
      } else if (ch === "}") {
        const b = stack.pop();
        if (b)
          parsed.push({
            file,
            text,
            allowLines,
            selector: b.sel,
            body: text.slice(b.open + 1, i),
            bodyStart: b.open + 1,
            line: lineOf(text, b.open),
            insideAt: stack.some((s) => s.sel.startsWith("@")),
          });
        selStart = i + 1;
      } else if (ch === ";" && stack.length === 0) selStart = i + 1;
    }
  }

  const allowed = (r) => r.allowLines.has(r.line) || r.allowLines.has(r.line - 1);

  /* ---- class definitions, with the ancestors each depends on (AC 6) ---- */
  const classDefs = new Map(); // subject -> [{ancestors, file, line, allow}]
  for (const r of parsed) {
    if (!r.selector || r.selector.startsWith("@")) continue;
    for (const br of selectorBranches(r.selector)) {
      const comps = selectorCompounds(br);
      if (comps.length === 0) continue;
      const { subjects, ancestors } = branchDefinitions(br);
      for (const s of subjects) {
        if (!classDefs.has(s)) classDefs.set(s, []);
        classDefs.get(s).push({ ancestors, file: r.file, line: r.line, allow: allowed(r) });
      }
    }
  }

  /* ---- token reachability graph (transitivity) ---- */
  const definedTokens = new Map(); // name -> {file, line, allow}
  const edges = new Map();
  const tokenRoots = new Set(referencedTokens);

  for (const r of parsed) {
    const inTheme = r.selector.startsWith("@theme") || r.insideAt;
    // Nested blocks blanked, but LENGTH-PRESERVING so each declaration keeps its
    // offset — a gate that reports the enclosing rule's line instead of the
    // declaration's own makes the reader hunt for what it is complaining about.
    const body = r.body.replace(/\{[^{}]*\}/g, (m) => " ".repeat(m.length));
    let cursor = 0;
    for (const decl of body.split(";")) {
      const declStart = cursor;
      cursor += decl.length + 1;
      const c = decl.indexOf(":");
      if (c === -1) continue;
      const prop = decl.slice(0, c).trim();
      const value = decl.slice(c + 1);
      const declLine = lineOf(r.text, r.bodyStart + declStart + decl.indexOf(prop));
      if (prop.startsWith("--jp-") && !definedTokens.has(prop)) {
        definedTokens.set(prop, {
          file: r.file,
          line: declLine,
          allow: r.allowLines.has(declLine) || r.allowLines.has(declLine - 1),
        });
      }
      const refs = [...value.matchAll(TOKEN_ANY)].map((m) => m[0]);
      if (refs.length === 0) continue;
      if (prop.startsWith("--jp-") && !inTheme) {
        if (!edges.has(prop)) edges.set(prop, new Set());
        for (const b of refs) edges.get(prop).add(b);
      } else {
        // an ordinary property, or anything inside @theme — the Tailwind bridge
        // re-exports tokens under shadcn names and generates utilities with no
        // var() anywhere, so a token consumed only through it is alive.
        for (const b of refs) tokenRoots.add(b);
      }
    }
  }

  const aliveTokens = new Set();
  const queue = [...tokenRoots];
  while (queue.length) {
    const t = queue.pop();
    if (aliveTokens.has(t)) continue;
    aliveTokens.add(t);
    for (const b of edges.get(t) ?? []) queue.push(b);
  }

  /* ---- sweep 3: a class definition nothing can ever use ---- */
  for (const [name, defs] of classDefs) {
    if (classIsReferenced(name)) {
      const seen = classSeenIn.get(name);
      if (seen && seen.has("test") && !seen.has("prod"))
        advisories.push(
          `${name} — referenced ONLY from test files. A static sweep cannot tell a positive ` +
            `assertion from a negative one, so this is reported, not judged.`
        );
      continue;
    }
    const d = defs.find((x) => !x.allow) ?? defs[0];
    if (d.allow) continue;
    if (rawOnlyClasses.has(name)) {
      advisories.push(
        `${name} — appears in a source file only BEFORE comment-stripping. Almost certainly a ` +
          `comment (not consumption), but it could be a line the stripper blanked, so this is ` +
          `reported rather than failed.`
      );
      continue;
    }
    inverse.push({
      file: winPath(d.file),
      line: d.line,
      selector: `.${name}`,
      decl:
        `.${name} is defined in CSS but nothing in the package references it (comments are not ` +
        `consumption). Delete the rule, or add \`guard-allow: <reason>\`.`,
      rule: "unused-class",
    });
  }

  /* ---- sweep 4: a token nothing transitively reads ---- */
  for (const [name, at] of definedTokens) {
    if (aliveTokens.has(name) || at.allow) continue;
    if (rawOnlyTokens.has(name)) {
      advisories.push(
        `${name} — appears in a source file only BEFORE comment-stripping; reported rather than ` +
          `failed, for the same reason as the class case above.`
      );
      continue;
    }
    inverse.push({
      file: winPath(at.file),
      line: at.line,
      selector: name,
      decl:
        `${name} is defined but nothing transitively reads it — no ordinary declaration, no ` +
        `@theme entry, no source reference, and no live token derives from it. A token read only ` +
        `by other dead tokens is dead: that is the shape --jp-density had. Delete it, or add ` +
        `\`guard-allow: <reason>\`.`,
      rule: "unused-token",
    });
  }

  /* ---- AC 6: the FORWARD sweep's blind spot ----
   * guard-css builds definedClasses from a flat regex with no selector model, so
   * `.jp-dead .jp-alive` registers `jp-alive` as defined even when no element can
   * carry `jp-dead`. Measured 2026-07-26: `.jp-empty__kicker`/`__body` had rules
   * ONLY under `.jp-empty--brand`, removed from all markup on 2026-06-10 — five
   * elements rendered unstyled for 46 days with this gate green. */
  for (const [name, defs] of classDefs) {
    if (!classIsReferenced(name)) continue; // already reported by sweep 3
    // An ancestor whose only occurrence sits on a line `stripJs` may have blanked
    // must count as referenced HERE too. Without this the mitigation covered
    // sweeps 3 and 4 but not AC 6 — the one rule whose message says "unscope a
    // rule, drop the class" — so a regex literal eating the sole reference to an
    // ancestor still produced a blocking false positive (#1056 re-review M6).
    const ancestorReferenced = (a) => classIsReferenced(a) || rawOnlyClasses.has(a);
    if (defs.some((d) => d.ancestors.every((a) => ancestorReferenced(a)))) continue;
    const d = defs.find((x) => !x.allow) ?? defs[0];
    if (d.allow) continue;
    inverse.push({
      file: winPath(d.file),
      line: d.line,
      selector: `.${name}`,
      decl:
        `.${name} is used in the app, but EVERY rule defining it sits under an ancestor ` +
        `(${d.ancestors.map((a) => "." + a).join(", ")}) that nothing references — so no element ` +
        `can match, and it renders unstyled while the source claims otherwise. Unscope a rule, ` +
        `drop the class, or add \`guard-allow: <reason>\`.`,
      rule: "unreachable-definition",
    });
  }

  return { inverse, advisories, refFileCount, composedCount: composedPrefixes.size };
}

/* ------------------------------------------------------------------------- *
 * Test seam (#1056, code-reviewer M3). The pure selector/comment helpers are
 * where the blocking false positives live — an unparenthesised `:not()` read as
 * a required ancestor talks a reader into deleting live CSS — so they are
 * exported and unit-tested. The sweep itself only runs when this file is the
 * entry point, so importing it in a test does not execute it or call exit().
 * ------------------------------------------------------------------------- */
export { stripJs, blankParens, selectorBranches, selectorCompounds, branchDefinitions };

const isMain = () => {
  const entry = process.argv[1];
  return typeof entry === "string" && import.meta.url === pathToFileURL(entry).href;
};

if (isMain()) {
  const findings = files.flatMap(checkFile);
  const { existence, dynamicPrefixes } = await checkExistence();
  findings.push(...existence);
  const { inverse, advisories, refFileCount, composedCount } = await checkInverseExistence();
  findings.push(...inverse);

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
    // The inverse sweep's undecidables, reported for the same reason: a name it
    // cannot rule on must not read as one it checked.
    console.log(
      `note: inverse sweep read ${refFileCount} non-CSS file(s); ${composedCount} composition prefix(es) shield names it cannot decide.`
    );
    for (const a of advisories) console.log(`note: ${a}`);
    console.log(`\n${findings.length} violation(s).`);
  }
  process.exit(findings.length ? 1 : 0);
}
