import { defineConfig, devices } from "@playwright/test";

// Overridable so the suite can run against a non-default FE port — parallel dev sessions routinely
// take :3000 (memory: parallel-stack port ownership), and the future Playwright-in-CI wiring needs
// to point at whatever port it spins up. Defaults to :3000 so existing local/CI behavior is unchanged.
const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  // In CI the FE runs `pnpm dev`, which compiles each route on-demand on first hit —
  // a cold-compile navigation can exceed the 30s default. Give tests more headroom in
  // CI (subsequent hits are cached and fast); keep the snappy default locally.
  timeout: process.env.CI ? 60_000 : 30_000,
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
    command: "pnpm dev",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    // Next dev first-compile under CI load is slower than a warm local server.
    timeout: process.env.CI ? 180_000 : 120_000,
  },
});
