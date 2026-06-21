import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
    exclude: ["node_modules", ".next"],
  },
  resolve: {
    // Array form so a regex `find` can be used for the i18n test shim.
    alias: [
      // i18n: route the bare `@testing-library/react` entry through a wrapper
      // that injects NextIntlClientProvider (messages/sv) so components using
      // `useTranslations` render without a manual provider in every test. The
      // `$` anchor keeps `@testing-library/react/pure` (the real impl the
      // wrapper imports) un-rewritten, avoiding an alias loop.
      {
        find: /^@testing-library\/react$/,
        replacement: path.resolve(__dirname, "./src/test/render-intl.tsx"),
      },
      { find: "@", replacement: path.resolve(__dirname, "./src") },
      // `server-only` är Next.js sentinel som inte exporteras som top-level
      // resolverbar modul (bara via Next.js compiled deps). Vite-side resolution
      // failer i transform-steget när client-komponenter följs genom server-
      // actions till API-helpers ("server-only"). Shim mot tom modul så
      // test-imports fungerar — produktion-byggen respekterar fortsatt original-
      // paketet via Next.js egen resolution.
      {
        find: "server-only",
        replacement: path.resolve(__dirname, "./src/test/server-only-shim.ts"),
      },
    ],
  },
});
