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
      ],
    },
  },
]);

export default eslintConfig;
