import { test, expect } from "@playwright/test";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(true, 64); });
test.afterAll(async () => { await harness?.stop(); });
test.beforeEach(async ({ context }) => {
  harness.reset();
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" }]);
});
test.afterEach(() => expect(harness.misses).toEqual([]));

test("table page, sorting, status filter and selected row survive an application modal excursion", async ({ page }) => {
  await page.goto("/ansokningar");
  await page.getByRole("radio", { name: "Tabell", exact: true }).click();
  await page.getByRole("searchbox").fill("Volume");
  await page.locator('.jp-steprail button').filter({ hasText: "Skickad" }).click();
  await page.getByRole("button", { name: "Sortera på Roll & företag", exact: true }).click();
  await page.getByRole("button", { name: "Sida 2", exact: true }).click();
  await page.getByRole("checkbox").last().check();
  const selected = await page.getByRole("checkbox").last().getAttribute("aria-label");
  const sort = await page.locator('th[aria-sort]').first().getAttribute("aria-sort");
  await page.locator('tbody a[href^="/ansokningar/"]').last().click();
  await expect(page.getByRole("dialog")).toBeVisible();
  const notice = page.locator('a[id^="information-application-"]');
  await notice.click();
  await page.locator('main a.jp-backlink').first().click();
  await expect(notice).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("searchbox")).toHaveValue("Volume");
  await expect(page.locator('.jp-steprail button[aria-pressed="true"]')).toContainText("Skickad");
  await expect(page.locator('.jp-apppager__btn[aria-current="page"]')).toHaveText("Sida 2");
  await expect(page.locator('th[aria-sort]').first()).toHaveAttribute("aria-sort", sort ?? "");
  await expect(page.getByRole("checkbox").last()).toHaveAttribute("aria-label", selected ?? "");
  await expect(page.getByRole("checkbox").last()).toBeChecked();
});

test("list group expansion and board expansion survive a footer excursion", async ({ page }) => {
  await page.goto("/ansokningar");
  await page.getByRole("radio", { name: "Lista", exact: true }).click();
  const group = page.locator('#status-Submitted');
  const toggle = group.locator('h3 button');
  if (await toggle.getAttribute("aria-expanded") === "false") await toggle.click();
  await group.getByRole("button", { name: /Visa .* till/ }).click();
  const rows = await group.locator('a[href^="/ansokningar/"]').count();
  await page.locator('footer a[href="/integritet"]').click();
  await page.locator('main a.jp-backlink').first().click();
  await expect(toggle).toHaveAttribute("aria-expanded", "true");
  await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(rows);
  await toggle.click();
  await page.locator('footer a[href="/cookies"]').click();
  await page.locator('main a.jp-backlink').first().click();
  await expect(toggle).toHaveAttribute("aria-expanded", "false");
  await page.getByRole("radio", { name: "Tavla", exact: true }).click();
  const boardColumn = page.locator('[data-information-scroll="board-column-Submitted"]');
  await boardColumn.getByRole("button", { name: /Visa .* till/ }).click();
  const cards = await boardColumn.locator('a[href^="/ansokningar/"]').count();
  const boardGrid = page.locator('[data-information-scroll="applications-board"]');
  await page.setViewportSize({ width: 390, height: 844 });
  await boardGrid.evaluate(el => el.scrollLeft = 160);
  const before = await boardGrid.evaluate(el => el.scrollLeft);
  expect(before).toBeGreaterThan(0);
  await page.locator('footer a[href="/matchning"]').click();
  await page.locator('main a.jp-backlink').first().click();
  await expect(boardColumn.locator('a[href^="/ansokningar/"]')).toHaveCount(cards);
  await expect(page.locator('footer a[href="/matchning"]')).toBeFocused();
  expect(await boardGrid.evaluate(el => el.scrollLeft)).toBe(before);
});

test("a later view remount follows its existing defaults instead of an old return snapshot", async ({ page }) => {
  await page.goto("/ansokningar");
  const group = page.locator('#status-Submitted');
  await group.getByRole("button", { name: /Visa .* till/ }).click();
  await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(65);
  await page.locator('footer a[href="/integritet"]').click();
  await expect(page.locator('main a.jp-backlink').first()).toHaveText("Tillbaka till ansökningarna");
  await page.locator('main a.jp-backlink').first().click();
  await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(65);
  await page.getByRole("radio", { name: "Tavla", exact: true }).click();
  await page.getByRole("radio", { name: "Lista", exact: true }).click();
  await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(10);
});

test("a view-cookie response delivered during an excursion preserves the expanded source", async ({ page }) => {
  let release: () => void = () => {};
  let received: () => void = () => {};
  let delivered: () => void = () => {};
  const pending = new Promise<void>(resolve => { release = resolve; });
  const requestReceived = new Promise<void>(resolve => { received = resolve; });
  const responseDelivered = new Promise<void>(resolve => { delivered = resolve; });
  await page.route('**/ansokningar', async route => {
    if (route.request().method() !== "POST") { await route.continue(); return; }
    const response = await route.fetch();
    received();
    await pending;
    await route.fulfill({ response });
    delivered();
  });
  try {
    await page.goto("/ansokningar");
    await page.getByRole("radio", { name: "Lista", exact: true }).click();
    await requestReceived;
    const group = page.locator('#status-Submitted');
    await group.getByRole("button", { name: /Visa .* till/ }).click();
    await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(65);
    await page.locator('footer a[href="/integritet"]').click();
    await expect(page.locator('main a.jp-backlink').first()).toHaveText("Tillbaka till ansökningarna");
    const markerId = await page.evaluate(() => history.state.jobbliggarenInformation.id as string);
    await page.evaluate(() => {
      Object.assign(window, { informationReplacementObserved: 0 });
      const original = history.replaceState.bind(history);
      history.replaceState = (data: unknown, unused: string, url?: string | URL | null) => {
        original(data, unused, url);
        if (location.pathname === "/integritet") {
          const count: unknown = Reflect.get(window, "informationReplacementObserved");
          Reflect.set(window, "informationReplacementObserved", typeof count === "number" ? count + 1 : 1);
        }
      };
    });
    release();
    await responseDelivered;
    await page.waitForFunction(() => { const count: unknown = Reflect.get(window, "informationReplacementObserved"); return typeof count === "number" && count > 0; });
    expect(await page.evaluate(() => history.state.jobbliggarenInformation)).toEqual({ id: markerId, step: 1 });
    await page.locator('main a.jp-backlink').first().click();
    await expect(group.locator('a[href^="/ansokningar/"]')).toHaveCount(65);
  } finally { release(); }
});
