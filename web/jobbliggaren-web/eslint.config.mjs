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

// ── Timezone SSOT (#1141 follow-up, #1148) ─────────────────────────────────
// `SWEDISH_TIME_ZONE` is the one name for the product's home zone. Its doc used
// to ENUMERATE where the raw literal still lived, and carried a standing order to
// re-measure that paragraph by hand — so removing a site falsified the doc. This
// rule replaces the count with a gate: the literal written as a value under
// `src/` fails lint, in pre-commit and in CI.
//
// The selector matches any string `Literal` whose value IS the zone, in any
// position, and that is the whole population definition — it needs no second
// normaliser. A comment is not an AST node at all, and a test name reading
// "formats … in Europe/Stockholm" is not a Literal whose value EQUALS the zone,
// so neither can match. The prose version of this rule needed a separate clause
// to exclude test names, which are themselves quoted string tokens, and a rule
// that needs two normalisers is two rules.
//
// Value-equality also reads the PARSED value, which a text match could not:
// "Europe/Stockholm" is caught. It is deliberately NOT case-insensitive and
// not concatenation-aware. Both evade it — and `"europe/stockholm"` is working
// code, because Intl canonicalises IANA ids case-insensitively — but closing
// either one means adding the second normaliser back.
const ZONE_MSG =
  'The raw zone literal is forbidden in product code. Import SWEDISH_TIME_ZONE from "@/lib/time/swedish-calendar" — one name, one place to change it. The exemptions are the `files` list of the ZONE-subtracting block in eslint.config.mjs; this message does not restate them, because a second copy is a second thing to keep true. If this file IS test code, the config does not classify it as such — name it `*.{test,spec}.{ts,tsx}` or put it under `src/test/`. Do not silence this by importing the constant: a test that imports it cannot catch a mutation OF it.';

const ZONE_RESTRICTIONS = [
  { selector: 'Literal[value="Europe/Stockholm"]', message: ZONE_MSG },
  { selector: 'TemplateElement[value.cooked="Europe/Stockholm"]', message: ZONE_MSG },
];

// ── Composition: every block subtracts, none enumerates ────────────────────
// Three blocks that each listed their INCLUSIONS would be an enumeration, and a
// group added to only some of them narrows silently — the defect class this file
// already names for path globs (SERVER_ACTION, above). With `allExcept`, a NEW
// group reaches every block by construction, and adding it here is the only way
// to add it anywhere.
const ALL_GROUPS = {
  COPY: COPY_RESTRICTIONS,
  TYPOGRAPHY: TYPOGRAPHY_RESTRICTIONS,
  MUTED: MUTED_RESTRICTIONS,
  SERVER_ACTION: SERVER_ACTION_RESTRICTIONS,
  ZONE: ZONE_RESTRICTIONS,
};

const allExcept = (...exempt) =>
  Object.entries(ALL_GROUPS)
    .filter(([name]) => !exempt.includes(name))
    .flatMap(([, group]) => group);

// Test-hood has ONE definition here, and it FOLLOWS the repo's own:
// `vitest.config.ts` collects `src/**/*.{test,spec}.{ts,tsx}`, and `.spec` means
// test here with no second meaning (the package's other `.spec.ts` files are all
// Playwright specs under `tests/e2e/`). The blocks below ignored `.test` only, so
// a `.spec` file WOULD BE linted as product code — none has ever existed under
// `src/`, so this is a prospective fix, not a repair. For the zone rule it would
// be worse than inconsistent: the message would tell its author to import the
// constant, which is exactly the change that blinds a test as an oracle.
//
// The glob is deliberately one notch WIDER than vitest's, not identical to it: it
// adds `js,jsx` so it spans every extension the blocks' own `files` reach. That
// span is prospective too — there are no `.js`/`.jsx` files under `src/` today.
// Vitest's second include, `scripts/**`, is outside those blocks entirely.
const TEST_FILES = ["**/*.{test,spec}.{ts,tsx,js,jsx}"];

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
    ignores: [...TEST_FILES, "src/components/ui/**"],
    rules: {
      "no-restricted-syntax": ["error", ...allExcept()],
    },
  },
  // shadcn primitives: same rules EXCEPT text-muted-foreground (the bridge
  // token owns the remap there — CTO D3, #549 WS1).
  {
    files: ["src/components/ui/**/*.{ts,tsx,js,jsx}"],
    ignores: TEST_FILES,
    rules: {
      "no-restricted-syntax": ["error", ...allExcept("MUTED")],
    },
  },
  // The zone's own declarations, plus the test harness — test code that is not
  // named `*.{test,spec}.*` and so is not covered by the ignores above.
  //
  // This block SUBTRACTS the zone rule rather than naming it, because ESLint
  // resolves a rule's options LAST-WINS rather than by concatenation: a block
  // carrying a shorter `no-restricted-syntax` array would silently switch the
  // copy, typography and `"use server"` guards OFF for these paths.
  //
  // Do NOT add a path under `src/components/ui/**` here — `allExcept("ZONE")`
  // includes MUTED, so it would re-enable the muted-foreground ban that the
  // block above drops on purpose (CTO D3, #549 WS1).
  //
  // The guard is one-directional: it fails an ADDED site, and cannot see an
  // exemption that is no longer needed. Removing the literal from a path listed
  // here means removing its entry in the same commit. See #1148.
  {
    files: [
      "src/i18n/request.ts",
      "src/lib/time/swedish-calendar.ts",
      "src/test/**/*.{ts,tsx,js,jsx}",
    ],
    ignores: TEST_FILES,
    rules: {
      "no-restricted-syntax": ["error", ...allExcept("ZONE")],
    },
  },
  // ── Server-side logging: AGENTS.md §5 `Backend:` (no sensitive data in logs) ──
  // Every line this app writes to stdout leaves the box: the Next container is one
  // of the four whose output `jobbliggaren-logship.sh` ships offsite, and that
  // script calls the app leg the only one carrying data-subject personal data. A
  // `console.*` added to a Server Action or a Server Component is therefore not a
  // debug aid — it is a new export path, and the `SECURITY (§5)` docblocks under
  // `src/lib/actions/` and `src/components/auth/` already promise callers that a
  // token or an email is never logged. Until this block those promises had no
  // mechanism.
  //
  // `error`, never `warn`: a warning fails a run only when the `lint` script is
  // given `--max-warnings`, which this block cannot guarantee and must not depend
  // on, so the severity has to carry the gate by itself. And no `{ allow: [...] }`:
  // every known call site is `console.error`, so allowing that method would make
  // the rule inert exactly where it is needed.
  //
  // This block deliberately does NOT subtract from ALL_GROUPS and must never be
  // folded into `no-restricted-syntax`. That composition exists to stop last-wins
  // narrowing WITHIN one rule key; `no-console` is its own key and shares last-wins
  // with nothing. Folding it in would also widen every exemption: a single
  // `eslint-disable-next-line no-restricted-syntax` would switch the copy,
  // typography, `"use server"` and zone guards off on the same line.
  //
  // Its own `files`/`ignores` rather than the first block's: that one drops
  // `src/components/ui/**`, and a logging gate that skips a directory is a gate
  // over part of the surface.
  //
  // Exemptions are rad-lokala `eslint-disable-next-line no-console` comments and
  // are not restated here — a second copy is a second thing to keep true, the same
  // reason ZONE_MSG gives for not listing its own.
  //
  // The guard is lexical, not semantic: it matches the `console.*` member
  // expression and nothing else. `const c = console`, `globalThis.console`, a
  // direct `process.stdout.write`, and a dependency's own logging all pass it.
  // Named so this block is not itself an unmeasured promise of the kind it exists
  // to support.
  {
    files: ["src/**/*.{ts,tsx,js,jsx}"],
    ignores: TEST_FILES,
    rules: {
      "no-console": "error",
    },
  },
]);

export default eslintConfig;
