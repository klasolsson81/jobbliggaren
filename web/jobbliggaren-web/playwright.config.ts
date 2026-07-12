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
  reporter: "list",
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
    timeout: 120_000,
  },
});
