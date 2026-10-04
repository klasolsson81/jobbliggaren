import { test, expect } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(true); });
test.afterAll(async () => { await harness?.stop(); });
test.beforeEach(() => harness.reset());
const directory = "C:/tmp/jbl-information-evidence/states";

test("render restored email error and CV separate-tab notice at the required state widths", async ({ page, context }) => {
  test.setTimeout(120000);
  mkdirSync(directory, { recursive: true });
  for (const mode of ["light", "forced-dark"] as const) for (const width of [390, 768, 1280, 3440]) {
    const suffix = mode === "light" ? "" : "-forced-dark";
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/logga-in?next=%2Fjobb");
    await page.locator('#email').fill("invalid");
    await page.locator('form button[type="submit"]').click();
    await expect(page.locator('#email')).toHaveAttribute("aria-invalid", "true");
    await page.locator('#information-email-privacy').click();
    await page.locator('main a[data-information-return]').first().click();
    await expect(page.locator('#information-email-privacy')).toBeFocused();
    await expect(page.locator("form [role='alert']")).toBeVisible();
    if (mode === "forced-dark") await page.evaluate(() => document.documentElement.dataset.theme = "dark");
    await page.screenshot({ path: join(directory, `email-restored-error-${width}${suffix}.png`), animations: "disabled" });
    await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" }]);
    await page.goto("/cv/importera");
    await page.locator('input[type="file"]').setInputFiles({ name: "synthetic.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF synthetic navigation fixture") });
    await page.getByRole("button", { name: /Ladda upp/ }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByRole("dialog")).toHaveCSS("opacity", "1");
    if (width <= 768) await expect.poll(async () => (await page.getByRole("dialog").locator('a[href="/integritet?context=cv-upload"]').boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44);
    if (mode === "forced-dark") await page.evaluate(() => document.documentElement.dataset.theme = "dark");
    await page.screenshot({ path: join(directory, `cv-pending-dialog-${width}${suffix}.png`), animations: "disabled" });
    const popupPromise = page.waitForEvent("popup");
    await page.getByRole("dialog").locator('a[href="/integritet?context=cv-upload"]').click();
    const popup = await popupPromise;
    await popup.setViewportSize({ width, height: 900 });
    await expect(popup.locator('main')).toContainText("Fortsätt i fliken där du laddar upp ditt CV");
    if (mode === "forced-dark") await popup.evaluate(() => document.documentElement.dataset.theme = "dark");
    await popup.screenshot({ path: join(directory, `cv-information-tab-${width}${suffix}.png`), animations: "disabled" });
    await popup.close();
    await context.clearCookies();
  }
  expect(harness.misses).toEqual([]);
});
