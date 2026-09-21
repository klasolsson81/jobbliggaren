import { test as base, type BrowserContext, type Page } from "@playwright/test";
import { ensureConfirmedTestUser, loginAs } from "./auth";

/**
 * A `test` whose pages are already logged in, with ONE login per spec file.
 *
 * Why not a login per test, as the password form allowed: a login is now a mailed code, and the
 * login challenge's budgets are constants (`LoginChallengePolicy`). The same address asking again
 * inside 60 seconds, or a fourth time in ten minutes, gets a challenge id with NO record and no
 * mail, silently, so the second `loginAs` of a file could never succeed (#1738).
 *
 * Why not Playwright's `storageState`, the usual answer: it INJECTS the saved cookie, and Chromium
 * refuses an injected `__Host-` cookie over http (measured for `scripts/visual-verify.ts`, which
 * needs an https base URL for that reason). A `Set-Cookie` from the server on http://localhost is
 * accepted, so the context that logged in is simply kept alive and every test opens its page in
 * it. Do not "simplify" this back to `storageState`.
 *
 * The fixture is worker-scoped and each file calls this once, so each file gets its own worker, its
 * own account and its own login. A retry restarts the worker: a fresh address, so no cooldown.
 *
 * What is shared between the tests of a file as a result: cookies and localStorage. No spec here
 * logs out or clears cookies (measured 2026-09-21). One that must starts from the base `test`.
 */
const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
// A worker fixture cannot read the test-scoped `baseURL` option; this is the config's own default.
const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000";

export function loggedInTest(runId: number) {
  return base.extend<{ page: Page }, { loggedInContext: BrowserContext }>({
    loggedInContext: [
      // `provide`, not Playwright's customary `use`: the React hooks lint reads a call named
      // `use` inside a function named `page` as the React hook.
      async ({ browser }, provide) => {
        await ensureConfirmedTestUser(BACKEND_URL, runId);
        const context = await browser.newContext({ baseURL: BASE_URL });
        const page = await context.newPage();
        await loginAs(page, runId);
        await page.close();
        await provide(context);
        await context.close();
      },
      { scope: "worker" },
    ],
    page: async ({ loggedInContext }, provide) => {
      const page = await loggedInContext.newPage();
      await provide(page);
      await page.close();
    },
  });
}
