import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

// ── Copy rules (em-dash + ellipsis — Klas hard rules) ──────────────────────
const EM_DASH_MSG =
  "Em-dash (—, U+2014) is forbidden in user-facing UI copy (AI cliché — Klas hard rule 2026-06-20). Use a period, colon, semicolon, comma or parentheses. En-dash (–, U+2013) for ranges is allowed.";
const ELLIPSIS_MSG =
  "Literal three-dot ellipsis (...) is forbidden in user-facing UI copy. Use the ellipsis character … (U+2026), per copy-skill §4 (#278). This targets copy only — spread/rest (...props) are not string literals and never match.";

const COPY_RESTRICTIONS = [
  { selector: "JSXText[value=/—/]", message: EM_DASH_MSG },
  { selector: "Literal[value=/—/]", message: EM_DASH_MSG },
  { selector: "TemplateElement[value.cooked=/—/]", message: EM_DASH_MSG },
  { selector: "JSXText[value=/\\.\\.\\./]", message: ELLIPSIS_MSG },
  { selector: "Literal[value=/\\.\\.\\./]", message: ELLIPSIS_MSG },
  { selector: "TemplateElement[value.cooked=/\\.\\.\\./]", message: ELLIPSIS_MSG },
];

// ── Typography guard (#549 WS5, CTO D3) ────────────────────────────────────
// Zero hardcoded font sizes/colors in TSX: everything goes through the
// semantic scale + ink/heading tokens (jobbpilot-design-tokens skill).
const TYPO_MSG_ARBITRARY =
  "Arbitrary pixel text size (text-[Npx]) is forbidden (#549 WS5). Use the semantic scale: text-h1..h4, text-body(-lg/-sm), text-caption, text-label, text-mono, text-ui, text-micro, text-overline.";
const TYPO_MSG_DEFAULT =
  "Default Tailwind size classes (text-xs/sm/base/lg/xl/…) are forbidden (#549 WS5). Use the semantic scale (jobbpilot-design-tokens skill).";
const TYPO_MSG_GRAY =
  "Raw gray utilities are forbidden (#549 WS5 — Klas hard rule: no light-gray text). Content = text-text-primary; genuine metadata = text-text-secondary/-tertiary; placeholder = text-placeholder.";
const TYPO_MSG_MUTED =
  "text-muted-foreground is forbidden in product code (#549 WS5) — allowed only inside components/ui/ where the bridge token owns the remap (CTO D3). Use text-text-primary (content) or text-text-secondary (metadata).";
const TYPO_MSG_INLINE =
  "Inline style font/color is forbidden (#549 WS5). Use a semantic class or a globals.css component class. (next/og renderers hoist their structurally required values to src/lib/og-tokens.ts.)";

const TYPOGRAPHY_RESTRICTIONS = [
  { selector: "Literal[value=/\\btext-\\[[0-9]/]", message: TYPO_MSG_ARBITRARY },
  { selector: "TemplateElement[value.cooked=/\\btext-\\[[0-9]/]", message: TYPO_MSG_ARBITRARY },
  {
    selector: "Literal[value=/\\btext-(xs|sm|base|lg|xl|2xl|3xl|4xl|5xl)\\b/]",
    message: TYPO_MSG_DEFAULT,
  },
  {
    selector: "TemplateElement[value.cooked=/\\btext-(xs|sm|base|lg|xl|2xl|3xl|4xl|5xl)\\b/]",
    message: TYPO_MSG_DEFAULT,
  },
  {
    selector: "Literal[value=/\\btext-(slate|gray|zinc|neutral|stone)-[0-9]/]",
    message: TYPO_MSG_GRAY,
  },
  {
    selector: "TemplateElement[value.cooked=/\\btext-(slate|gray|zinc|neutral|stone)-[0-9]/]",
    message: TYPO_MSG_GRAY,
  },
  { selector: 'JSXAttribute[name.name="style"] Property[key.name="fontSize"]', message: TYPO_MSG_INLINE },
  { selector: 'JSXAttribute[name.name="style"] Property[key.name="fontFamily"]', message: TYPO_MSG_INLINE },
  { selector: 'JSXAttribute[name.name="style"] Property[key.name="fontWeight"]', message: TYPO_MSG_INLINE },
  { selector: 'JSXAttribute[name.name="style"] Property[key.name="color"]', message: TYPO_MSG_INLINE },
];

const MUTED_RESTRICTIONS = [
  { selector: "Literal[value=/\\btext-muted-foreground\\b/]", message: TYPO_MSG_MUTED },
  { selector: "TemplateElement[value.cooked=/\\btext-muted-foreground\\b/]", message: TYPO_MSG_MUTED },
];

// ── `"use server"` export shape (#1053, from #1059) ────────────────────────
// Next error E352: a `"use server"` module may only export async functions.
// Turbopack's server-actions loader enumerates the module's exports and emits a
// hashed re-export per specifier; TypeScript has already erased any type-only
// binding, so the generated re-export points at nothing and the module fails to
// LINK. Webpack only checks at runtime, which is why #1059 lived three weeks on
// main with `pnpm build` green — both CI jobs that build, build for production.
//
// Shape, not enumeration. The previous attempt at this rule listed two of at
// least five expressible forms and missed `export { type X }` (a different AST
// node); a rule that enumerates forms IS the defect class. The predicate is
// "does this export carry specifiers", which collapses `export type { X }`,
// `export { type X }`, `export { x }`, `export { x } from "…"` and
// `export * from "…"` into two selectors.
//
// It deliberately does NOT fire on `export type X = …` (declaration form,
// `specifiers` empty): TypeScript erases that whole node, so it never enters the
// loader's enumeration. Measured 2026-07-26: 17 `"use server"` modules carry 50
// `export async function` + 14 `export type X = …` + ZERO specifiers, so this
// calibrates to zero today.
//
// The gate is the module's DIRECTIVE, never the file path or the text. A path
// glob (`src/lib/actions/**`) reaches only 12 of the 17 — an enumeration that
// narrows silently — and a text match false-positives on 6 files that carry the
// string in prose, including `_action-result.ts`, whose docstring teaches this
// very rule.
const USE_SERVER_MODULE = 'Program:has(> ExpressionStatement[directive="use server"])';
const E352_MSG =
  'A `"use server"` module may only export async function DECLARATIONS (Next error E352). ' +
  "This export carries specifiers, and Turbopack generates a re-export for each one at " +
  "module-link time — a type-only binding has already been erased by TypeScript, so the " +
  "re-export resolves to nothing and every page whose graph reaches this module 500s in " +
  "`next dev` (#1059). `pnpm build` does NOT catch this: both CI builds are production " +
  "builds and webpack only checks at runtime. Write `export async function foo() {}` " +
  "directly. Shared types belong in a non-`use server` module — see " +
  "src/lib/actions/_action-result.ts (SSOT).";

const SERVER_ACTION_RESTRICTIONS = [
  { selector: `${USE_SERVER_MODULE} > ExportNamedDeclaration[specifiers.length>0]`, message: E352_MSG },
  { selector: `${USE_SERVER_MODULE} > ExportAllDeclaration`, message: E352_MSG },
];

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
  {
    files: ["src/**/*.{ts,tsx,js,jsx}"],
    ignores: ["**/*.test.{ts,tsx,js,jsx}", "src/components/ui/**"],
    rules: {
      "no-restricted-syntax": [
        "error",
        ...COPY_RESTRICTIONS,
        ...TYPOGRAPHY_RESTRICTIONS,
        ...MUTED_RESTRICTIONS,
        ...SERVER_ACTION_RESTRICTIONS,
      ],
    },
  },
  // shadcn primitives: same rules EXCEPT text-muted-foreground (the bridge
  // token owns the remap there — CTO D3, #549 WS1).
  {
    files: ["src/components/ui/**/*.{ts,tsx,js,jsx}"],
    ignores: ["**/*.test.{ts,tsx,js,jsx}"],
    rules: {
      "no-restricted-syntax": [
        "error",
        ...COPY_RESTRICTIONS,
        ...TYPOGRAPHY_RESTRICTIONS,
        ...SERVER_ACTION_RESTRICTIONS,
      ],
    },
  },
]);

export default eslintConfig;
