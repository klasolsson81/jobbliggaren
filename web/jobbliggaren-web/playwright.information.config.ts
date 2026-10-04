import { defineConfig } from "@playwright/test";
import { HARNESS_PORTS } from "./tests/information/servers";
const browserName = process.env.INFORMATION_BROWSER === "webkit" ? "webkit" : process.env.INFORMATION_BROWSER === "firefox" ? "firefox" : "chromium";

export default defineConfig({
  testDir: "./tests/information",
  workers: 1,
  retries: 0,
  timeout: browserName === "webkit" ? 120_000 : 60_000,
  expect: { timeout: browserName === "webkit" ? 30_000 : 15_000 },
  reporter: "list",
  use: { browserName, baseURL: `https://localhost:${HARNESS_PORTS.proxy}`, ignoreHTTPSErrors: true },
  webServer: {
    command: `${process.env.INFORMATION_SKIP_BUILD === "1" ? "" : "pnpm build && "}pnpm start -p ${HARNESS_PORTS.next}`,
    url: `http://localhost:${HARNESS_PORTS.next}/robots.txt`,
    env: { BACKEND_URL: `http://localhost:${HARNESS_PORTS.backend}` },
    reuseExistingServer: false,
    timeout: 600_000,
  },
});
