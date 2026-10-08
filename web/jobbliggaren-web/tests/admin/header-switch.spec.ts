import { expect, test } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { HARNESS_PORTS, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
const ports = process.env.HEADER_HARNESS_PORTS === "3170" ? { proxy: 3170, next: 3171, backend: 3172 } : HARNESS_PORTS;
const origin = `https://localhost:${ports.proxy}`;
const directory = process.env.ADMIN_ACCESS_SCREENSHOT_DIR;
async function capture(page: import("@playwright/test").Page, name: string) {
  if (directory) await page.screenshot({ path: `${directory}/${name}.png`, fullPage: true });
}
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");
test.beforeAll(async () => { if (directory) mkdirSync(directory, { recursive: true }); harness = await startHarness(ports); });
test.afterAll(async () => { await harness.stop(); });
test.beforeEach(async ({ context }) => {
  harness.reset();
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: origin, secure: true, httpOnly: true, sameSite: "Strict" }]);
});

for (const width of [1280, 1920, 3440, 1024, 768, 375, 320]) {
  test(`switches only links and preserves the header at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/admin/granskning");
    const toggle = page.getByRole("button", { name: "Adminmeny" });
    await expect(toggle).toHaveAttribute("aria-pressed", "true");
    const main = await page.locator("main").innerText();
    const accountButton = page.getByRole("button", { name: "Inställningar" });
    const controls = await accountButton.boundingBox();
    expect(controls).not.toBeNull();
    const switchBox = await toggle.boundingBox();
    const statsBox = await page.locator(".jp-header-stats").boundingBox();
    expect(await page.locator(".jp-header__inner").evaluate((element) => element.getBoundingClientRect().height)).toBe(88);
    await capture(page, `admin-${width}`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
    await toggle.focus();
    await page.keyboard.press("Space");
    await expect(toggle).toHaveAttribute("aria-pressed", "false");
    await expect(toggle).toBeFocused();
    await expect(page).toHaveURL("/admin/granskning");
    expect(await page.locator("main").innerText()).toBe(main);
    expect(await accountButton.boundingBox()).toEqual(controls);
    expect(await toggle.boundingBox()).toEqual(switchBox);
    expect(await page.locator(".jp-header-stats").boundingBox()).toEqual(statsBox);
    if (width > 900) {
      await expect(page.getByRole("navigation", { name: "Huvudnavigation" }).getByRole("link", { name: "CV" })).toBeVisible();
      if (width >= 1200) await expect(page.getByText("42 000", { exact: true })).toBeVisible();
    } else {
      await page.getByRole("button", { name: "Öppna meny" }).click();
      await expect(page.getByRole("dialog", { name: "Meny" }).getByRole("link", { name: "Jobb", exact: true })).toBeVisible();
      await page.keyboard.press("Escape");
    }
    await capture(page, `user-${width}`);
    await page.getByRole("button", { name: "Inställningar" }).click();
    const account = page.getByRole("dialog", { name: "Inställningar" });
    await expect(account.getByRole("link", { name: "Inställningar" })).toHaveAttribute("href", "/mina-sidor");
    await expect(account.getByRole("link", { name: "Granskning" })).toHaveCount(0);
    await page.keyboard.press("Escape");
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () => {
      const axe = (window as unknown as { axe: { run: (context: string) => Promise<{ violations: { id: string }[] }> } }).axe;
      return (await axe.run(".jp-header")).violations.map((v) => v.id);
    });
    expect(violations).toEqual([]);
    await toggle.click();
    if (width <= 900) {
      await page.getByRole("button", { name: "Öppna meny" }).click();
      await expect(page.getByRole("dialog", { name: "Meny" }).getByRole("link", { name: "Granskning" })).toBeVisible();
      await page.keyboard.press("Escape");
    }
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
    const darkViolations = await page.evaluate(async () => {
      const axe = (window as unknown as { axe: { run: (context: string) => Promise<{ violations: { id: string }[] }> } }).axe;
      return (await axe.run(".jp-header")).violations.map((v) => v.id);
    });
    expect(darkViolations).toEqual([]);
    await capture(page, `admin-dark-${width}`);
  });
}

test("the ordinary account cannot open admin pages and has no switch", async ({ page }) => {
  harness.who = "member";
  await page.goto("/admin/granskning");
  await expect(page).toHaveURL("/");
  await expect(page.getByRole("button", { name: "Adminmeny" })).toHaveCount(0);
});
