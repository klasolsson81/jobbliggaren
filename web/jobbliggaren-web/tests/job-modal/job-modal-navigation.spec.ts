import { expect, test, type Locator, type Page } from "@playwright/test";
import { ADS, type HarnessAd } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The job modal's navigation contract (#1963, ADR 0053 Amendment 2026-10-03), in a real browser: a row
 * opens the ad in the intercepted modal, closing it in every way returns focus to the row, and the
 * card's two links out of it ("Gå till Ställ in matchning", "Visa ansökan") leave the modal for their
 * page. The slot's null pages (`@modal/mina-sidor`, `@modal/ansokningar`) and the application modal's
 * interceptor are what make the last two true; the file tree alone does not show that a soft
 * navigation empties the slot, which is why this runs in a browser.
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
  expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]);
});

const rowLink = (page: Page, ad: HarnessAd) => page.locator(`a[href^="/jobb/${ad.id}"]`).filter({ hasText: ad.title });
const jobModal = (page: Page, ad: HarnessAd) => page.getByRole("dialog", { name: ad.title });

async function openFromList(page: Page, list: string, ad: HarnessAd) {
  await page.goto(list);
  const row = rowLink(page, ad);
  await row.click();
  await expect(page).toHaveURL(`/jobb/${ad.id}`);
  await expect(jobModal(page, ad)).toContainText("Öppna annonsen");
  return row;
}

test("a row in /jobb opens the ad in the modal, and Esc closes it with focus back on the row", async ({ page }) => {
  const row = await openFromList(page, "/jobb", ADS.open);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL("/jobb");
  await expect(row).toBeFocused();
});

test("the close button and the scrim close the modal with focus back on the row", async ({ page }) => {
  const row = await openFromList(page, "/jobb", ADS.open);
  await jobModal(page, ADS.open).getByRole("button", { name: "Stäng" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(row).toBeFocused();

  await row.click();
  await expect(jobModal(page, ADS.open)).toBeVisible();
  // The scrim is the area beside the panel; a click inside the panel does not close it.
  await page.mouse.click(16, 360);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL("/jobb");
  await expect(row).toBeFocused();
});

test("focus starts on the close button and Tab stays inside the modal", async ({ page }) => {
  await openFromList(page, "/jobb", ADS.open);
  const modal = jobModal(page, ADS.open);
  const close = modal.getByRole("button", { name: "Stäng" });
  await expect(close).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(modal.getByRole("link", { name: /Öppna annonsen/ })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(close).toBeFocused();
});

test("back closes the modal and forward opens it again", async ({ page }) => {
  await openFromList(page, "/jobb", ADS.open);
  await page.goBack();
  await expect(page).toHaveURL("/jobb");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.goForward();
  await expect(page).toHaveURL(`/jobb/${ADS.open.id}`);
  await expect(jobModal(page, ADS.open)).toContainText("Öppna annonsen");
});

// The page behind the modal stays put (#1852): its heading does not move, and the link that opened the modal is the
// same node, so Esc returns focus to it.
async function expectPageKeptBehindModal(page: Page, list: string, ad: HarnessAd) {
  await page.goto(list);
  const heading = page.locator("main h1");
  const left = (await heading.boundingBox())?.x;
  await rowLink(page, ad).evaluate((link) => link.setAttribute("data-opener", ""));
  const opener = page.locator("[data-opener]");
  await opener.click();
  await expect(page).toHaveURL(`/jobb/${ad.id}`);
  await expect(jobModal(page, ad)).toContainText("Öppna annonsen");
  expect((await heading.boundingBox())?.x).toBe(left);
  await expect(opener).toHaveCount(1);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  await expect(opener).toBeFocused();
}

test("a row in /sparade opens the ad in the modal over an unmoved page, and Esc returns focus to the row", async ({ page }) => {
  await expectPageKeptBehindModal(page, "/sparade", ADS.saved);
});

test("a row in /matchningar opens the ad in the modal over an unmoved page, and Esc returns focus to the row", async ({ page }) => {
  await expectPageKeptBehindModal(page, "/matchningar", ADS.open);
});

// A listed record that is gone stays inside the signed-in shell (#1987): the modal says so over the unmoved list,
// where the slot's notFound() used to swap the whole shell for the public 404 frame. The heading's position and the
// opener's node tell a soft navigation from a hard one, which would keep the shell too.
async function expectNotFoundModalOverList(page: Page, list: string, row: Locator, heading: string) {
  await page.goto(list);
  const listHeading = page.getByRole("heading", { level: 1, name: heading });
  const left = (await listHeading.boundingBox())?.x;
  await row.evaluate((link) => link.setAttribute("data-opener", ""));
  const opener = page.locator("[data-opener]");
  await opener.click();
  await expect(page.getByRole("dialog", { name: "Sidan finns inte" })).toContainText(
    "Adressen kan vara felstavad eller så har sidan tagits bort."
  );
  await expect(page.getByRole("navigation", { name: "Huvudnavigation" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Logga in", exact: true })).toHaveCount(0);
  expect((await listHeading.boundingBox())?.x).toBe(left);
  await expect(opener).toHaveCount(1);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  await expect(opener).toBeFocused();
  return opener;
}

test("a row in /sparade whose ad answers 404 says so in the modal over the unmoved page, and the row opens again once it answers", async ({ page }) => {
  harness.unavailableAds.set(ADS.saved.id, 404);
  const opener = await expectNotFoundModalOverList(page, "/sparade", rowLink(page, ADS.saved), "Sparade annonser");
  harness.unavailableAds.clear();
  await opener.click();
  await expect(jobModal(page, ADS.saved)).toContainText("Öppna annonsen");
});

test("an ad erased under Art. 17 (410) says the same in the modal", async ({ page }) => {
  harness.unavailableAds.set(ADS.saved.id, 410);
  await expectNotFoundModalOverList(page, "/sparade", rowLink(page, ADS.saved), "Sparade annonser");
});

test("a row in /ansokningar whose application answers 404 says so in the modal over the unmoved page", async ({ page }) => {
  harness.unavailableApplications.add(ADS.applied.id);
  const row = page.locator('a.jp-app__rowlink[href^="/ansokningar/"]');
  await expectNotFoundModalOverList(page, "/ansokningar", row, "Mina ansökningar");
});

test("Gå till Ställ in matchning leaves the modal for /mina-sidor", async ({ page }) => {
  harness.occupationStated = false;
  await openFromList(page, "/jobb", ADS.open);
  await jobModal(page, ADS.open).getByRole("link", { name: "Gå till Ställ in matchning" }).click();
  await expect(page).toHaveURL("/mina-sidor");
  await expect(page.getByRole("heading", { level: 1, name: "Mina sidor" })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("marking the ad applied links to the new application, which opens in its own modal; back returns to the ad", async ({ page }) => {
  await openFromList(page, "/jobb", ADS.open);
  const modal = jobModal(page, ADS.open);
  await modal.getByRole("button", { name: "Markera som ansökt" }).click();

  const link = modal.getByRole("link", { name: "Visa ansökan" });
  await expect(link).toBeFocused();
  await expect(modal.getByRole("button", { name: "Markera som ansökt" })).toHaveCount(0);
  await expect(link).toHaveAttribute("href", /^\/ansokningar\/[0-9a-f-]{36}$/);
  const href = await link.getAttribute("href");

  await link.click();
  await expect(page).toHaveURL(href ?? "");
  // The application modal, not the job modal: it carries the application's flow and no job footer.
  const applicationModal = page.getByRole("dialog");
  await expect(applicationModal).toHaveCount(1);
  await expect(applicationModal).toContainText("Flytta i flödet");
  await expect(applicationModal).not.toContainText("Öppna annonsen");

  await page.goBack();
  await expect(page).toHaveURL(`/jobb/${ADS.open.id}`);
  await expect(jobModal(page, ADS.open)).toContainText("Visa ansökan");
});

test("an ad applied before this visit links to /ansokningar, and the link leaves the modal", async ({ page }) => {
  await openFromList(page, "/jobb", ADS.applied);
  const link = jobModal(page, ADS.applied).getByRole("link", { name: "Visa ansökan" });
  await expect(link).toHaveAttribute("href", "/ansokningar");
  await link.click();
  await expect(page).toHaveURL("/ansokningar");
  await expect(page.getByRole("heading", { level: 1, name: "Mina ansökningar" })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
