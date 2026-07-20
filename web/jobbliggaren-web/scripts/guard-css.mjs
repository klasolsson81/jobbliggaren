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
 * only via an explicit Klas ratchet (CLAUDE.md 2.5 discipline).
 *
 * Usage: node scripts/guard-css.mjs <path-to-css> [<path-to-css> ...] [--json]
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

const findings = files.flatMap(checkFile);

if (asJson) {
  console.log(JSON.stringify(findings, null, 1));
} else {
  for (const f of findings)
    console.log(`${f.file}:${String(f.line).padStart(5)}  [${f.rule}]  ${f.selector}  →  ${f.decl.slice(0, 80)}`);
  console.log(`\n${findings.length} violation(s).`);
}
process.exit(findings.length ? 1 : 0);
