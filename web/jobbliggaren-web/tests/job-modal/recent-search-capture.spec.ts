import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { expect, test, type Page } from "@playwright/test";
import { ADS } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * #2285 — a recent search is stored only by a POST from a Server Action, never by a GET (ADR 0060 and ADR 0018, their
 * amendments of 2026-11-22), measured in a real browser. The fixture backend records in `captures` every request that
 * would store a search: a list read carrying `commit=true`, which is how one was stored before #2285, or the capture
 * POST. The front proxy records each document request with its `Sec-Fetch-Site` and whether it carried the session
 * cookie, so each test also shows which way the planted link arrived.
 */

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(async ({ context }) => {
  harness.reset();
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);
});

test.afterEach(() => {
  expect(harness.misses, "the page read a backend route the harness does not answer").toEqual([]);
});

/** A link someone else wrote: their free text, and the flag that stored it before #2285. */
const PLANTED = "/jobb?q=planterad&commit=true";
const CAPTURE = "POST /api/v1/me/recent-searches";

const field = (page: Page) => page.getByLabel("Sök efter yrke, arbetsgivare eller ort");

/** The results have answered and every effect after mount has run, so a late capture would have been recorded. */
async function settled(page: Page) {
  await expect(page.getByText(ADS.open.title).first()).toBeVisible();
  await page.waitForLoadState("networkidle");
}

/** After hydration the visible field has no name: a native GET would carry the text in a hidden input instead. */
async function hydrated(page: Page) {
  await expect(field(page)).not.toHaveAttribute("name", "q");
}

const navigationTo = (path: string) => harness.navigations.find((hop) => hop.path === path);

test("an address-bar navigation to a planted /jobb link stores nothing", async ({ page }) => {
  await page.goto(PLANTED);
  await settled(page);

  expect(navigationTo(PLANTED)).toMatchObject({ site: "none", session: true });
  expect(harness.captures).toEqual([]);
});

test("the employer's application link in the job modal, same-origin and planted, stores nothing", async ({
  page,
  context,
}) => {
  harness.applicationPages.set(ADS.open.id, `${APP_ORIGIN}${PLANTED}`);
  await page.goto("/jobb");
  await page.locator(`a[href^="/jobb/${ADS.open.id}"]`).filter({ hasText: ADS.open.title }).click();
  const modal = page.getByRole("dialog", { name: ADS.open.title });

  const [opened] = await Promise.all([
    context.waitForEvent("page"),
    modal.getByRole("link", { name: "Ansök" }).click(),
  ]);
  await settled(opened);

  expect(navigationTo(PLANTED)).toMatchObject({ site: "same-origin", session: true });
  expect(harness.captures).toEqual([]);
});

// An API request sends no Fetch Metadata, so the proxy does not log it as a navigation. It is signed in all the same:
// /jobb sends a guest to /logga-in, and this answer is the list itself, at the planted address.
test("a request without Fetch Metadata stores nothing", async ({ context }) => {
  const response = await context.request.get(`${APP_ORIGIN}${PLANTED}`);

  expect(response.status()).toBe(200);
  expect(response.url()).toBe(`${APP_ORIGIN}${PLANTED}`);
  expect(await response.text()).toContain(ADS.open.title);
  expect(harness.captures).toEqual([]);
});

test("a click on another site is bounced and its query dropped, so nothing is stored", async ({ page }) => {
  const elsewhere = createServer((_request, response) => {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end(`<!doctype html><title>elsewhere</title><a id="planted" href="${APP_ORIGIN}${PLANTED}">Jobb</a>`);
  });
  await new Promise<void>((resolve) => elsewhere.listen(0, "127.0.0.1", resolve));
  const { port } = elsewhere.address() as AddressInfo;
  try {
    await page.goto(`http://127.0.0.1:${port}/`);
    await page.locator("#planted").click();
    await page.waitForURL(`${APP_ORIGIN}/jobb`);
    await settled(page);
  } finally {
    await new Promise<void>((resolve) => void elsewhere.close(() => resolve()));
  }

  expect(navigationTo(PLANTED)).toMatchObject({ site: "cross-site", session: false });
  expect(harness.captures).toEqual([]);
});

test("a deliberate search is stored once, by a POST, and the live preview never", async ({ page }) => {
  await page.goto("/jobb");
  await settled(page);
  await hydrated(page);

  await field(page).pressSequentially("planerare samordnare ", { delay: 40 });
  await expect(page).toHaveURL(/[?&]q=planerare/);
  await page.waitForLoadState("networkidle");
  expect(harness.captures, "the live preview stores nothing").toEqual([]);

  await field(page).press("Enter");
  await expect.poll(() => harness.captures.length).toBe(1);
  await page.waitForLoadState("networkidle");

  expect(harness.captures).toHaveLength(1);
  expect(harness.captures[0]?.route).toBe(CAPTURE);
  expect(String(harness.captures[0]?.body?.q)).toContain("planerare");
});

test("Spara sökningen stores the search on screen with one POST and confirms it", async ({ page }) => {
  harness.listStorable = true;
  await page.goto("/jobb?q=utvecklare");
  await settled(page);
  await hydrated(page);

  await page.getByRole("button", { name: "Spara sökningen" }).click();

  await expect(page.locator("p.jp-hero__searchsaved")).toHaveText("Sökningen är sparad i Senaste sökningar");
  expect(harness.captures).toHaveLength(1);
  expect(harness.captures[0]?.route).toBe(CAPTURE);
  expect(harness.captures[0]?.body?.q).toBe("utvecklare");
});
