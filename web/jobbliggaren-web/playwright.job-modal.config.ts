import { defineConfig, devices } from "@playwright/test";
import { HARNESS_PORTS } from "./tests/job-modal/servers";

// The job modal's navigation contract (#1963): soft navigation out of the intercepted job modal empties
// the @modal slot, and every way of closing it returns focus to the row. Outside the default run and
// outside CI, whose e2e stack seeds no job ads; run it where it is recorded:
//
//   pnpm exec playwright test -c playwright.job-modal.config.ts
//
// It builds and serves the app in production mode against a fixture backend (tests/job-modal/servers.ts),
// so it needs no stack. It needs `openssl` on the PATH for its certificate.
export default defineConfig({
  testDir: "./tests/job-modal",
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
