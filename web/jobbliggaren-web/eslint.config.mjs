import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

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
    ignores: ["**/*.test.{ts,tsx,js,jsx}"],
    rules: {
      "no-restricted-syntax": [
        "error",
        {
          selector: "JSXText[value=/—/]",
          message:
            "Em-dash (—, U+2014) is forbidden in user-facing UI copy (AI cliché — Klas hard rule 2026-06-20). Use a period, colon, semicolon, comma or parentheses. En-dash (–, U+2013) for ranges is allowed.",
        },
        {
          selector: "Literal[value=/—/]",
          message:
            "Em-dash (—, U+2014) is forbidden in user-facing UI copy (AI cliché — Klas hard rule 2026-06-20). Use a period, colon, semicolon, comma or parentheses. En-dash (–, U+2013) for ranges is allowed.",
        },
        {
          selector: "TemplateElement[value.cooked=/—/]",
          message:
            "Em-dash (—, U+2014) is forbidden in user-facing UI copy (AI cliché — Klas hard rule 2026-06-20). Use a period, colon, semicolon, comma or parentheses. En-dash (–, U+2013) for ranges is allowed.",
        },
      ],
    },
  },
]);

export default eslintConfig;
