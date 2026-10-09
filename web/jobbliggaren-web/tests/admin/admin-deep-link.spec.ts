import { expect, test, type Page } from "@playwright/test";
import { FEEDBACK_IDS } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The notice mail's link through login (#1979): a signed-out administrator who opens
 * `/admin/feedback?id=<guid>` lands on the login page with that path in `next`, so logging in returns to the
 * submission the mail named. The proxy writes the path into a request header and the `(admin)` layout reads
 * it; only the pathname and one GUID id survive, and a header the browser sends is overwritten.
 */

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(() => {
  harness.reset();
});

test.afterEach(() => {
  expect(harness.misses, "the page read a backend route the harness does not answer").toEqual([]);
});

const ID = FEEDBACK_IDS.queued;
const OTHER_ID = FEEDBACK_IDS.failed;

async function landsOnLoginWith(page: Page, next: string | null) {
  await page.waitForURL((url) => url.pathname === "/logga-in");
  const url = new URL(page.url());
  expect(url.searchParams.get("next")).toBe(next);
  expect([...url.searchParams.keys()]).toEqual(next === null ? [] : ["next"]);
  if (next !== null) await expect(page.locator('input[name="next"]').first()).toHaveValue(next);
}

test("without a session cookie, the mail's link keeps its id through the login redirect", async ({ page }) => {
  await page.goto(`/admin/feedback?id=${ID}&status=ny`);

  await landsOnLoginWith(page, `/admin/feedback?id=${ID}`);
});

test("with a session the backend has ended, the same", async ({ context, page }) => {
  harness.who = "stale";
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);

  await page.goto(`/admin/feedback?id=${ID}`);

  await landsOnLoginWith(page, `/admin/feedback?id=${ID}`);
});

test("an id that is not a GUID is dropped, and the pathname is kept", async ({ page }) => {
  await page.goto("/admin/feedback?id=namn.efternamn%40example.test");

  await landsOnLoginWith(page, "/admin/feedback");
});

test("a header the browser sends is overwritten by the proxy", async ({ page }) => {
  await page.setExtraHTTPHeaders({ "x-jobbliggaren-admin-return": `/admin/feedback?id=${OTHER_ID}` });

  await page.goto("/admin/anvandare");

  await landsOnLoginWith(page, "/admin/anvandare");
});
