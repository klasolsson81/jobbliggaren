import { defineConfig, devices } from "@playwright/test";
import { HARNESS_APP_VERSION, HARNESS_PORTS } from "./tests/feedback/servers";

// The feedback row and dialog (#1979) in a real browser: the screenshot is decoded and redrawn by the
// browser's own codec, which jsdom does not have, and the submission travels through the BFF route to a
// fixture backend that records what arrived. Outside the default run and outside CI; run it where it is
// recorded:
//
//   pnpm exec playwright test -c playwright.feedback.config.ts
//
// It builds and serves the app in production mode against the job-modal harness's fixture backend
// (tests/job-modal/servers.ts), so it needs no stack. It needs `openssl` on the PATH for its certificate.
export default defineConfig({
  testDir: "./tests/feedback",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "list",
  use: { baseURL: `https://localhost:${HARNESS_PORTS.proxy}`, ignoreHTTPSErrors: true },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `pnpm build && pnpm start -p ${HARNESS_PORTS.next}`,
    url: `http://localhost:${HARNESS_PORTS.next}/robots.txt`,
    env: { BACKEND_URL: `http://localhost:${HARNESS_PORTS.backend}`, APP_VERSION: HARNESS_APP_VERSION },
    reuseExistingServer: false,
    timeout: 600_000,
  },
});
