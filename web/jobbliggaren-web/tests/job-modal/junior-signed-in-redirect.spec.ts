import { mkdirSync } from "node:fs";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { join } from "node:path";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { ADS } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness, type Navigation } from "./servers";

/**
 * A signed-in visitor of /junior lands on /jobb with the junior filter, also from a link another site shows (#2284,
 * ADR 0186 and ADR 0018, their amendments of 2026-11-21), measured in a real browser.
 *
 * The session cookie is `SameSite=Strict`, so a click on another site arrives without it. These tests click from a page
 * on 127.0.0.1, a different site from the app's `localhost`, so the navigation is cross-site; a `page.goto` is an
 * address-bar navigation (`Sec-Fetch-Site: none`). The front proxy records every document request with whether it
 * carried the session cookie, which is the measurement: the first hop arrives without it and the continuation
 * document's own hop arrives with it.
 *
 * With VISUAL_SHOTS_DIR set, the states are also saved as screenshots for the design verdict.
 */

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(() => harness.reset());

test.afterEach(() => {
  expect(harness.misses, "the page read a backend route the harness does not answer").toEqual([]);
});

const LINKEDIN_BOT = "LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)";

async function signIn(context: BrowserContext) {
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);
}

/** A page on another site that links to the app: the post on LinkedIn, the message on Discord. */
async function serveElsewhere(href: string): Promise<{ url: string; close: () => Promise<void> }> {
  const server = createServer((_request, response) => {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end(`<!doctype html><title>elsewhere</title><a id="junior" href="${href}">Juniora IT-jobb</a>`);
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}/`,
    close: () => new Promise<void>((resolve) => void server.close(() => resolve())),
  };
}

async function clickFromElsewhere(page: Page, path: string): Promise<void> {
  const elsewhere = await serveElsewhere(`${APP_ORIGIN}${path}`);
  try {
    await page.goto(elsewhere.url);
    await page.locator("#junior").click();
  } finally {
    await elsewhere.close();
  }
}

/** The document requests that reached the app, as the front proxy recorded them. */
const hops = (): Navigation[] => [...harness.navigations];

async function shot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`), fullPage: true });
}

const filterPill = (page: Page) => page.locator("button.jp-hero-pill").filter({ hasText: "Filter" });

test("a signed-in visitor who clicks the link on another site lands on /jobb with the junior filter set", async ({
  page,
  context,
}) => {
  await signIn(context);
  await clickFromElsewhere(page, "/junior");

  await page.waitForURL(`${APP_ORIGIN}/jobb?junior=on`);
  expect(hops()).toEqual([
    { path: "/junior", status: 200, site: "cross-site", session: false },
    { path: "/junior?vidare=1", status: 307, site: "same-origin", session: true },
    { path: "/jobb?junior=on", status: 200, site: "same-origin", session: true },
  ]);
  await expect(filterPill(page)).toHaveAttribute("data-active", "true");
  await expect(filterPill(page).locator(".jp-hero-pill__count")).toHaveText("1");
  await expect(page.getByText(ADS.open.title).first()).toBeVisible();
  await page.waitForLoadState("networkidle");
  await shot(page, "signed-in-landing-1280");
});

test("the list's order travels to /jobb, and its page does not", async ({ page, context }) => {
  await signIn(context);
  await clickFromElsewhere(page, "/junior?sortBy=ExpiresAtAsc&page=2&fbclid=IwAR0abc");

  await page.waitForURL(`${APP_ORIGIN}/jobb?junior=on&sortBy=ExpiresAtAsc`);
  expect(hops().map((hop) => hop.path)).toEqual([
    "/junior?sortBy=ExpiresAtAsc&page=2&fbclid=IwAR0abc",
    "/junior?sortBy=ExpiresAtAsc&page=2&vidare=1",
    "/jobb?junior=on&sortBy=ExpiresAtAsc",
  ]);
});

test("a signed-out visitor from another site lands on the public list at its clean address", async ({ page }) => {
  await clickFromElsewhere(page, "/junior?sortBy=ExpiresAtAsc&fbclid=IwAR0abc");

  await page.waitForURL(`${APP_ORIGIN}/junior?sortBy=ExpiresAtAsc`);
  expect(hops()).toEqual([
    { path: "/junior?sortBy=ExpiresAtAsc&fbclid=IwAR0abc", status: 200, site: "cross-site", session: false },
    { path: "/junior?sortBy=ExpiresAtAsc&vidare=1", status: 307, site: "same-origin", session: false },
    { path: "/junior?sortBy=ExpiresAtAsc", status: 200, site: "same-origin", session: false },
  ]);
  await expect(page.getByRole("heading", { level: 1, name: "Juniora IT-jobb" })).toBeVisible();
  await expect(page.getByRole("link", { name: ADS.open.title })).toBeVisible();
  await shot(page, "signed-out-landing-1280");
});

test("an address-bar navigation carries the Strict cookie, so it goes straight on to /jobb", async ({ page, context }) => {
  await signIn(context);
  await page.goto("/junior");

  await page.waitForURL(`${APP_ORIGIN}/jobb?junior=on`);
  // `none` is kept across the redirect (Fetch Metadata §4.1), so the follow-up is not cross-site either.
  expect(hops()).toEqual([
    { path: "/junior", status: 307, site: "none", session: true },
    { path: "/jobb?junior=on", status: 200, site: "none", session: true },
  ]);
});

test("a link previewer, which sends no Fetch Metadata, gets the page with its preview, as before", async ({ request }) => {
  const res = await request.get("/junior", { headers: { "user-agent": LINKEDIN_BOT }, maxRedirects: 0 });

  expect(res.status()).toBe(200);
  const html = await res.text();
  expect(html).toContain('<meta property="og:title" content="Juniora IT-jobb – lediga jobb för dig som är ny inom IT"');
  expect(html).toContain(ADS.open.title);
});

test("with the refresh blocked, the continuation document shows its link after the hold, and the link goes on", async ({
  page,
}) => {
  // DESIGN.md §11.6: the document holds its content invisible for two seconds. A browser that blocks the meta refresh
  // is the actor; answering the refresh's navigation with 204, which leaves the document where it is, is the mechanism.
  await page.route("**/junior?vidare=1", (route) => route.fulfill({ status: 204 }));
  await clickFromElsewhere(page, "/junior");
  await page.waitForURL(`${APP_ORIGIN}/junior`);

  const link = page.getByRole("link", { name: "Fortsätt", exact: true });
  await expect(page.locator("main")).toBeHidden();
  await expect(link).toBeVisible({ timeout: 6_000 });
  await shot(page, "continuation-refresh-blocked-1280");
  await page.setViewportSize({ width: 3440, height: 1440 });
  await shot(page, "continuation-refresh-blocked-3440");

  await page.unroute("**/junior?vidare=1");
  await link.click();
  await expect(page.getByRole("heading", { level: 1, name: "Juniora IT-jobb" })).toBeVisible();
  expect(hops().map(({ path, status }) => `${status} ${path}`)).toEqual([
    "200 /junior",
    "307 /junior?vidare=1",
    "200 /junior",
  ]);
});
