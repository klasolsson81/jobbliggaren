import { defineConfig, devices } from "@playwright/test";
import { HARNESS_PORTS } from "./tests/reload-notice/servers";

// Where the line after a stale-build reload lands (#1988, ADR 0148 D7), in a real browser: jsdom has no
// layout and no scroll restoration, so the tests in src/ cannot see it. Outside the default run and
// outside CI; run it where it is recorded:
//
//   pnpm exec playwright test -c playwright.reload-notice.config.ts
//
// It builds and serves the app in production mode against the job-modal harness's fixture backend
// (tests/job-modal/servers.ts), so it needs no stack. It needs `openssl` on the PATH for its certificate.
export default defineConfig({
  testDir: "./tests/reload-notice",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "list",
  use: { baseURL: `https://localhost:${HARNESS_PORTS.proxy}`, ignoreHTTPSErrors: true },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `pnpm build && pnpm start -p ${HARNESS_PORTS.next}`,
    url: `http://localhost:${HARNESS_PORTS.next}/robots.txt`,
    env: { BACKEND_URL: `http://localhost:${HARNESS_PORTS.backend}` },
    reuseExistingServer: false,
    timeout: 600_000,
  },
});
