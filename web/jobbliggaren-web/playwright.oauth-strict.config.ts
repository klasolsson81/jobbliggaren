import { defineConfig, devices } from "@playwright/test";
import { HARNESS_PORTS } from "./tests/oauth-strict/servers";

// The Strict-cookie measurement behind the OAuth callback's continuation document (#1744, ADR 0142
// D8), in the three engines. Outside the default run and outside CI; run it where it is recorded:
//
//   pnpm exec playwright install firefox webkit   # once
//   pnpm exec playwright test -c playwright.oauth-strict.config.ts
//
// It builds and serves the app in production mode against stub servers (tests/oauth-strict/servers.ts),
// so it needs no stack and reaches no provider.
export default defineConfig({
  testDir: "./tests/oauth-strict",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "list",
  use: { baseURL: `http://localhost:${HARNESS_PORTS.proxy}` },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "firefox", use: { ...devices["Desktop Firefox"] } },
    { name: "webkit", use: { ...devices["Desktop Safari"] } },
  ],
  webServer: {
    command: `pnpm build && pnpm start -p ${HARNESS_PORTS.next}`,
    url: `http://localhost:${HARNESS_PORTS.next}/robots.txt`,
    env: { BACKEND_URL: `http://localhost:${HARNESS_PORTS.backend}` },
    reuseExistingServer: false,
    timeout: 600_000,
  },
});
