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
    alias: {
      "@": path.resolve(__dirname, "./src"),
      // `server-only` är Next.js sentinel som inte exporteras som top-level
      // resolverbar modul (bara via Next.js compiled deps). Vite-side resolution
      // failer i transform-steget när client-komponenter följs genom server-
      // actions till API-helpers ("server-only"). Shim mot tom modul så
      // test-imports fungerar — produktion-byggen respekterar fortsatt original-
      // paketet via Next.js egen resolution.
      "server-only": path.resolve(__dirname, "./src/test/server-only-shim.ts"),
    },
  },
});
