import { defineConfig, devices } from "@playwright/test";

// Overridable so the suite can run against a non-default FE port — parallel dev sessions routinely
// take :3000 (memory: parallel-stack port ownership), and the future Playwright-in-CI wiring needs
// to point at whatever port it spins up. Defaults to :3000 so existing local/CI behavior is unchanged.
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  // #813: CI runs a PRODUCTION build (routes precompiled), so cold-compile flake is
  // gone and a single retry is enough to absorb genuine infrastructure jitter. The
  // old `retries: 2` tripled the cost of every real failure and was a main driver of
  // the 25-min timeout.
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  // Prod build serves precompiled routes, so the default per-test timeout holds even
  // on a loaded runner. Keep a little CI headroom for runner jitter.
  timeout: process.env.CI ? 45_000 : 30_000,
  // "list" for readable logs; add the HTML report in CI so the workflow can upload it
  // as a failure artifact (open: never — non-interactive on the runner).
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    // #813: CI serves a PRODUCTION build (`pnpm build` runs as its own workflow step,
    // so the compile cost is visible and cached rather than hidden inside the first
    // navigation). `next dev` compiled each route on first hit, which — with
    // workers:1 and retries:2 — could not finish inside the job timeout.
    // security-headers.spec.ts asserts only branch-invariant CSP directives plus the
    // dev/prod consistency invariant, so it passes in BOTH modes.
    // Locally `pnpm dev` stays the default (fast edit loop); an already-running server
    // is reused.
    command: process.env.CI ? "pnpm start" : "pnpm dev",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
