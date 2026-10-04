import { defineConfig, devices } from "@playwright/test";
import { PREVIEW_PORTS } from "./tests/admin-preview/ports";

// The local admin preview (ADR 0150 D5) in a real browser: its pages, the account panel, the
// confirmation, the receipt, the refusals and the impersonation banner over fictional data. Outside
// the default run and outside CI; run it where it is recorded:
//
//   pnpm exec playwright test -c playwright.admin-preview.config.ts
//
// It builds with ADMIN_PREVIEW_ENABLED=true, and that build must never be deployed: the next plain
// `pnpm build` replaces it. It needs `openssl` on the PATH for its certificate.
export default defineConfig({
  testDir: "./tests/admin-preview",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: "list",
  use: { baseURL: `https://localhost:${PREVIEW_PORTS.proxy}`, ignoreHTTPSErrors: true },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `pnpm build && pnpm start -p ${PREVIEW_PORTS.next}`,
    url: `http://localhost:${PREVIEW_PORTS.next}/robots.txt`,
    env: { ADMIN_PREVIEW_ENABLED: "true", BACKEND_URL: `http://localhost:${PREVIEW_PORTS.backend}` },
    reuseExistingServer: false,
    timeout: 600_000,
  },
});
