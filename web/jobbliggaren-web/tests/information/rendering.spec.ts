import { test, expect } from "@playwright/test";
import { createRequire } from "node:module";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { INFORMATION_PATHS } from "../../src/components/information/destinations";
import { APP_ORIGIN, startHarness, type Harness } from "./servers";

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(true); });
test.afterAll(async () => { await harness?.stop(); });
const directory = process.env.INFORMATION_EVIDENCE_DIR ?? "C:/tmp/jbl-information-evidence/render";
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");
type AxeResult = { violations: { id: string; impact: string; nodes: { target: string[] }[] }[] };
const long = new Set(["/integritet", "/villkor", "/cookies", "/matchning", "/cv-granskning", "/tillganglighet"]);

test("all thirteen pages: light and forced dark, top and end, four widths, axe and landmarks", async ({ page }) => {
  test.setTimeout(300_000);
  mkdirSync(directory, { recursive: true });
  const results: unknown[] = [];
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  for (const mode of ["light", "forced-dark"] as const) for (const width of [390, 1280, 1920, 3440]) for (const path of INFORMATION_PATHS) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(path);
    await expect(page.locator('main a.jp-backlink')).toHaveCount(2);
    if (mode === "forced-dark") await page.evaluate(() => document.documentElement.dataset.theme = "dark");
    await expect(page.locator("main")).toHaveCount(1);
    await expect(page.locator("h1")).toHaveCount(1);
    await expect(page.locator("#main")).toHaveCount(1);
    await expect(page.locator("header")).toHaveCount(1);
    await expect(page.locator("footer")).toHaveCount(1);
    await expect(page.locator('meta[name="description"]')).toHaveCount(1);
    expect(await page.title()).not.toBe("");
    expect(await page.locator('main nav[aria-label="På den här sidan"]').count()).toBe(long.has(path) ? 1 : 0);
    const geometry = await page.evaluate(() => {
      const anchors = [...document.querySelectorAll<HTMLAnchorElement>('main nav a[href^="#"]')];
      const ids = [...document.querySelectorAll("[id]")].map(el => el.id);
      return {
        overflow: document.documentElement.scrollWidth > innerWidth,
        duplicateIds: ids.filter((id, index) => ids.indexOf(id) !== index),
        brokenAnchors: anchors.filter(a => !document.getElementById(a.hash.slice(1))).map(a => a.hash),
        targets: [...document.querySelectorAll('main a.jp-backlink, main a[href^="#"]')].map(a => a.getBoundingClientRect().height),
        informationTargets: [...document.querySelectorAll<HTMLAnchorElement>('main a[data-information-link]')].map(a => ({ href: a.getAttribute("href"), id: a.id, height: a.getBoundingClientRect().height })),
        ink: getComputedStyle(document.documentElement).getPropertyValue("--jp-ink-1"),
      };
    });
    expect(geometry.overflow).toBe(false);
    expect(geometry.duplicateIds).toEqual([]);
    expect(geometry.brokenAnchors).toEqual([]);
    expect(geometry.ink.trim().toLowerCase()).toBe(mode === "forced-dark" ? "#f4f7fc" : "#0c1a2e");
    if (width <= 768) {
      expect(geometry.targets.every(height => height >= 44)).toBe(true);
      expect(geometry.informationTargets.filter(target => target.height < 44)).toEqual([]);
    }
    const prefix = `${path.slice(1)}-${width}-${mode}`;
    await page.screenshot({ path: join(directory, `${prefix}-top.png`), animations: "disabled" });
    await page.locator('main a.jp-backlink').last().scrollIntoViewIfNeeded();
    await page.screenshot({ path: join(directory, `${prefix}-end.png`), animations: "disabled" });
    if (width === 390 || width === 1280) {
      await page.addScriptTag({ path: axePath });
      const axe = await page.evaluate(async () => {
        const engine = (window as unknown as { axe: { run: (target: Document, options: unknown) => Promise<AxeResult> } }).axe;
        return engine.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
      });
      results.push({ path, width, mode, ...geometry, axe: axe.violations.map(v => ({ id: v.id, impact: v.impact, targets: v.nodes.map(n => n.target) })) });
      writeFileSync(join(directory, "results.json"), JSON.stringify(results, null, 2));
      expect(axe.violations).toEqual([]);
    } else results.push({ path, width, mode, ...geometry });
  }
  writeFileSync(join(directory, "results.json"), JSON.stringify({ results, errors, misses: harness.misses }, null, 2));
  expect(errors).toEqual([]);
  expect(harness.misses).toEqual([]);
});

test("320px reflow, 768px touch boundary and 200 percent desktop zoom equivalent", async ({ browser }) => {
  test.setTimeout(100_000);
  for (const [width, deviceScaleFactor] of [[320, 1], [768, 1], [640, 2]] as const) {
    // 640 CSS pixels at DPR2 models the layout of a 1280px screen at 200% zoom.
    const context = await browser.newContext({ viewport: { width, height: 900 }, deviceScaleFactor, baseURL: APP_ORIGIN, ignoreHTTPSErrors: true });
    const page = await context.newPage();
    await page.goto("/logga-in");
    expect((await page.locator('#information-email-privacy').boundingBox())?.height).toBeGreaterThanOrEqual(44);
    for (const path of INFORMATION_PATHS) {
      await page.goto(path);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.locator('main a.jp-backlink').last().scrollIntoViewIfNeeded();
      expect((await page.locator('main a.jp-backlink').last().boundingBox())?.height).toBeGreaterThanOrEqual(44);
    }
    await context.close();
  }
});

test("canonical pages and section fragments work with JavaScript disabled", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL: APP_ORIGIN, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  await page.goto("/cookies");
  await expect(page.locator('main a.jp-backlink')).toHaveText(["Till startsidan", "Till startsidan"]);
  await page.locator('main nav a[href="#cookie-controls"]').click();
  await expect(page).toHaveURL(/cookies#cookie-controls$/);
  await expect(page.locator("#cookie-controls")).toBeInViewport();
  await context.close();
});

test("each information view supports keyboard skip, contents and both return controls", async ({ page }) => {
  test.setTimeout(180_000);
  for (const path of INFORMATION_PATHS) {
    await page.goto(path);
    await page.keyboard.press("Tab");
    await expect(page.getByRole("link", { name: /Hoppa till/ })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.locator("#main")).toBeFocused();
    await page.keyboard.press("Tab");
    const topReturn = page.locator('main a[data-information-return]').first();
    await expect(topReturn).toBeFocused();
    await expect(topReturn).toHaveCSS("outline-width", "2px");
    if (long.has(path)) {
      await page.keyboard.press("Tab");
      const firstSection = page.locator('main nav a[href^="#"]').first();
      await expect(firstSection).toBeFocused();
      const target = await firstSection.getAttribute("href");
      await page.keyboard.press("Enter");
      await expect(page.locator(target ?? "#main")).toBeFocused();
    }
    const bottomReturn = page.locator('main a[data-information-return]').last();
    for (let presses = 0; presses < 150; presses++) {
      await page.keyboard.press("Tab");
      if (await bottomReturn.evaluate(el => el === document.activeElement)) break;
    }
    await expect(bottomReturn).toBeFocused();
    await expect(bottomReturn).toHaveCSS("outline-width", "2px");
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL("/");
  }
});

test("English navigation has the same anchors and accessible return names", async ({ page, context }) => {
  await context.addCookies([{ name: "NEXT_LOCALE", value: "en", url: APP_ORIGIN }]);
  for (const path of INFORMATION_PATHS) {
    await page.goto(path);
    await expect(page.locator('main a.jp-backlink')).toHaveText(["Go to the home page", "Go to the home page"]);
    if (long.has(path)) await expect(page.getByRole("navigation", { name: "On this page" })).toBeVisible();
  }
});
