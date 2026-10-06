import { test, expect, devices, type Page, type BrowserContext, type Locator } from "@playwright/test";
import { ADS } from "../job-modal/fixtures";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, SYNTHETIC_CV, startHarness, type Harness } from "./servers";

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(true); });
test.afterAll(async () => { await harness?.stop(); });
test.beforeEach(() => harness.reset());
test.afterEach(() => expect(harness.misses).toEqual([]));
const returns = (page: Page, name: string) => page.locator("main").getByRole("link", { name, exact: true });
const rowInput = process.env.INFORMATION_BROWSER === "webkit" ? "WebKit keyboard opener" : "mouse opener";
async function openRow(row: Locator, browserName: string) {
  if (browserName === "webkit") {
    await row.focus();
    await row.press("Enter");
  } else await row.click();
}
async function signIn(context: BrowserContext) {
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" }]);
}

test("email draft, error and next survive a single-entry policy chain and Back/Forward", async ({ page }) => {
  await page.goto("/logga-in?next=%2Fjobb%3Fq%3Dtest");
  await page.locator('#email').fill("invalid");
  await page.locator('form button[type="submit"]').click();
  await expect(page.locator('#email')).toHaveAttribute("aria-invalid", "true");
  const message = await page.locator("form [role='alert']").innerText();
  await page.locator('#email').fill("draft@example.test");
  const length = await page.evaluate(() => history.length);
  await page.locator('#information-email-privacy').focus();
  await page.keyboard.press("Enter");
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await expect(returns(page, "Tillbaka till inloggningen").first()).toHaveAttribute("href", "/logga-in");
  expect(await page.evaluate(() => JSON.stringify({ url: location.href, history: history.state, local: Object.entries(localStorage), session: Object.entries(sessionStorage) }))).not.toContain("draft@example.test");
  await page.locator('main a[href="/villkor"]').click();
  await expect(page).toHaveURL("/villkor");
  await page.locator('footer a[href="/cookies"]').click();
  await expect(page).toHaveURL("/cookies");
  await page.getByRole("navigation", { name: "På den här sidan" }).locator('a').first().click();
  await expect(page.locator('#about-cookies')).toBeFocused();
  await page.getByRole("link", { name: "Till sidans början" }).click();
  await expect(page.locator('#cookies-heading')).toBeFocused();
  await page.getByRole("link", { name: /Hoppa till/ }).focus();
  await page.keyboard.press("Enter");
  await expect(page.locator('#main')).toBeFocused();
  expect(await page.evaluate(() => history.length)).toBe(length + 1);
  await returns(page, "Tillbaka till inloggningen").first().click();
  await expect(page).toHaveURL(/logga-in\?next=/);
  await expect(page.locator('#email')).toHaveValue("draft@example.test");
  await expect(page.locator("form [role='alert']")).toHaveText(message);
  await expect(page.locator('[name="next"]')).toHaveValue("/jobb?q=test");
  await expect(page.locator('#information-email-privacy')).toBeFocused();
  await page.locator('#email').fill("edited-after-back@example.test");
  await page.goForward();
  await expect(page).toHaveURL(/cookies/);
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await page.goBack();
  await expect(page.locator('#email')).toHaveValue("edited-after-back@example.test");
  expect(harness.requests.filter(route => route === "POST /api/v1/auth/challenge")).toHaveLength(1);
});

test("immediate Back then Forward retains a source draft before its UI remounts", async ({ page }) => {
  await page.goto("/logga-in?next=%2Fjobb");
  await page.locator('#email').fill("fast-history@example.test");
  await page.locator('#information-email-privacy').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await page.evaluate(() => new Promise<void>(resolve => {
    const forward = () => {
      window.addEventListener("popstate", () => resolve(), { once: true });
      history.forward();
    };
    window.addEventListener("popstate", forward, { once: true });
    history.back();
  }));
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await returns(page, "Tillbaka till inloggningen").first().click();
  await expect(page.locator('#email')).toHaveValue("fast-history@example.test");
  await expect(page.locator('[name="next"]')).toHaveValue("/jobb");
  expect(harness.requests.filter(route => route === "POST /api/v1/auth/challenge")).toHaveLength(0);
});

test("a footer link to the current information page keeps return usable", async ({ page }) => {
  await page.goto("/logga-in");
  await page.locator('#email').fill("same-page@example.test");
  await page.locator('#information-email-privacy').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  const length = await page.evaluate(() => history.length);
  await page.locator('footer a[href="/integritet"]').click();
  await returns(page, "Tillbaka till inloggningen").first().click();
  await expect(page.locator('#email')).toHaveValue("same-page@example.test");
  expect(await page.evaluate(() => history.length)).toBe(length);
});

test("demo footer links retain their explicit demo origins, focus and expanded ad", async ({ page, context, browserName }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/gast/jobb");
  const welcomeSaved = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/gast/jobb");
  await page.getByRole("button", { name: "Börja utforska", exact: true }).click();
  await welcomeSaved;
  expect((await context.cookies()).find(cookie => cookie.name === "__Host-jobbliggaren_guest_welcomed")?.value).toBe("1");
  const cases = [
    ["/gast/jobb", "Tillbaka till jobb i demoläget"],
    ["/gast/ansokningar", "Tillbaka till ansökningar i demoläget"],
    ["/gast/cv", "Tillbaka till CV i demoläget"],
    ["/gast/oversikt", "Tillbaka till översikten i demoläget"],
    ["/gast/jobb/gj-1", "Tillbaka till annonsen i demoläget"],
    ["/gast/ansokningar/ga-1", "Tillbaka till ansökan i demoläget"],
  ] as const;
  for (const [path, name] of cases) {
    await page.goto(path);
    if (path === "/gast/jobb/gj-1") await page.getByRole("button", { name: "Visa hela annonsen", exact: true }).click();
    const trigger = page.locator('footer a[href="/integritet"]');
    await trigger.click();
    await expect(returns(page, name)).toHaveCount(2);
    await expect(returns(page, name).first()).toHaveAttribute("href", path);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (path === "/gast/ansokningar") {
      const directory = `C:/tmp/jbl-information-evidence/states/demo-${browserName}`;
      mkdirSync(directory, { recursive: true });
      await page.evaluate(() => document.fonts.ready);
      const typography = await page.locator("h1").evaluate(heading => {
        const style = getComputedStyle(heading);
        return { status: document.fonts.status, family: style.fontFamily, weight: style.fontWeight, color: style.color, faces: [...document.fonts].map(face => ({ family: face.family, weight: face.weight, status: face.status })) };
      });
      expect(typography.status).toBe("loaded");
      expect(typography.weight).toBe("800");
      writeFileSync(join(directory, "typography.json"), JSON.stringify(typography, null, 2));
      for (const width of [320, 390, 1280, 1920, 3440]) {
        await page.setViewportSize({ width, height: 900 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        if (width <= 768) expect((await returns(page, name).first().boundingBox())?.height).toBeGreaterThanOrEqual(44);
        await returns(page, name).first().scrollIntoViewIfNeeded();
        await page.screenshot({ path: join(directory, `demo-return-${width}-top.png`), animations: "disabled" });
        await returns(page, name).last().scrollIntoViewIfNeeded();
        await page.screenshot({ path: join(directory, `demo-return-${width}-end.png`), animations: "disabled" });
      }
      await page.setViewportSize({ width: 390, height: 844 });
    }
    await returns(page, name).first().click();
    await expect(page).toHaveURL(path);
    await expect(trigger).toBeFocused();
    if (path === "/gast/jobb/gj-1") await expect(page.getByRole("button", { name: "Visa mindre", exact: true })).toBeVisible();
  }
});

for (const [list, detail, name] of [
  ["/gast/jobb", "/gast/jobb/gj-1", "Tillbaka till jobb i demoläget"],
  ["/gast/ansokningar", "/gast/ansokningar/ga-1", "Tillbaka till ansökningar i demoläget"],
] as const) test(`demo ${detail}: modal close returns row focus before and after a footer excursion (${rowInput})`, async ({ page, context, browserName }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/gast/jobb");
  const welcomeSaved = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/gast/jobb");
  await page.getByRole("button", { name: "Börja utforska", exact: true }).click();
  await welcomeSaved;
  expect((await context.cookies()).find(cookie => cookie.name === "__Host-jobbliggaren_guest_welcomed")?.value).toBe("1");
  if (list !== "/gast/jobb") await page.goto(list);
  const row = page.locator(`a[href="${detail}"]`).first();
  await openRow(row, browserName);
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  await expect(row).toBeFocused();

  const privacy = page.locator('footer a[href="/integritet"]');
  await privacy.click();
  await expect(returns(page, name)).toHaveCount(2);
  await returns(page, name).first().click();
  await expect(page).toHaveURL(list);
  await expect(privacy).toBeFocused();
  await openRow(row, browserName);
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  await expect(row).toBeFocused();
});

test("direct, new-tab, reload and forged markers fall back to home", async ({ page, context }) => {
  await page.goto("/integritet?returnTo=https%3A%2F%2Fevil.test");
  await expect(returns(page, "Till startsidan")).toHaveCount(2);
  await page.evaluate(() => history.replaceState({ ...history.state, jobbliggarenInformation: { id: "forged", step: 1 } }, ""));
  await expect(returns(page, "Till startsidan")).toHaveCount(2);
  await page.goto("/logga-in");
  await page.locator('#email').fill("draft@example.test");
  const popupPromise = context.waitForEvent("page");
  await page.locator('#information-email-privacy').click({ modifiers: ["Control"] });
  const popup = await popupPromise;
  await popup.waitForLoadState();
  await expect(returns(popup, "Till startsidan")).toHaveCount(2);
  await popup.close();
  await expect(page.locator('#email')).toHaveValue("draft@example.test");
  await page.locator('#information-email-privacy').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await page.reload();
  await expect(returns(page, "Till startsidan")).toHaveCount(2);
});

test("help guide keeps the hub as its origin, then task context takes precedence", async ({ page }) => {
  await page.goto("/hjalpcenter");
  await page.locator('#information-help-matching').click();
  await expect(returns(page, "Tillbaka till hjälpcenter")).toHaveCount(2);
  await returns(page, "Tillbaka till hjälpcenter").last().click();
  await expect(page).toHaveURL("/hjalpcenter");
  await expect(page.locator('#information-help-matching')).toBeFocused();
  await page.goto("/logga-in");
  await page.locator('footer a[href="/hjalpcenter"]').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await page.locator('#information-help-matching').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
});

test("a link to the original hub ends the chain without a return loop", async ({ page }) => {
  await page.goto("/hjalpcenter");
  const length = await page.evaluate(() => history.length);
  await page.locator('#information-help-matching').click();
  await expect(page).toHaveURL("/matchning");
  await expect(returns(page, "Tillbaka till hjälpcenter")).toHaveCount(2);
  await page.locator('footer a[href="/hjalpcenter"]').click();
  await expect(page).toHaveURL("/hjalpcenter");
  await expect(returns(page, "Till startsidan")).toHaveCount(2);
  expect(await page.evaluate(() => history.length)).toBe(length + 1);
  await page.goForward();
  await expect(page).toHaveURL("/matchning");
  await expect(returns(page, "Tillbaka till hjälpcenter")).toHaveCount(2);
});

test("missing live history proof fails closed and a new task releases the old draft", async ({ page }) => {
  await page.goto("/logga-in");
  await page.locator('#email').fill("obsolete@example.test");
  await page.locator('#information-email-privacy').click();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  // Deliberately unreachable corruption: prove only safe degradation when an own marker is replaced.
  await page.evaluate(() => history.replaceState({ ...history.state, jobbliggarenInformation: { id: "lost-proof", step: 1 } }, ""));
  await returns(page, "Tillbaka till inloggningen").first().click();
  await expect(page).toHaveURL("/");
  await page.locator('footer a[href="/logga-in"]').click();
  await expect(page.locator('#email')).toHaveValue("");
});

for (const mode of ["full", "modal"] as const) test(`job ${mode}: expanded text, scroll and notice focus survive return${mode === "modal" ? ` (${rowInput})` : ""}`, async ({ page, context, browserName }) => {
  await signIn(context);
  await page.setViewportSize({ width: 390, height: 844 });
  if (mode === "full") await page.goto(`/jobb/${ADS.saved.id}`);
  else {
    await page.goto("/sparade");
    const row = page.locator(`a[href^="/jobb/${ADS.saved.id}"]`).filter({ hasText: ADS.saved.title });
    await openRow(row, browserName);
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(row).toBeFocused();
    await openRow(row, browserName);
    await expect(page.getByRole("dialog")).toBeVisible();
  }
  await page.getByRole("button", { name: "Visa hela annonsen", exact: true }).click();
  const notice = page.locator(`#information-job-${ADS.saved.id}`);
  await notice.scrollIntoViewIfNeeded();
  const before = await page.evaluate(() => ({ window: scrollY, modal: document.querySelector('[data-information-scroll="job-modal-body"]')?.scrollTop }));
  await notice.click();
  await expect(returns(page, "Tillbaka till annonsen")).toHaveCount(2);
  await returns(page, "Tillbaka till annonsen").first().click();
  await expect(notice).toBeFocused();
  await expect(page.getByRole("button", { name: "Visa mindre", exact: true })).toBeVisible();
  expect(await page.getByRole("dialog").count()).toBe(mode === "modal" ? 1 : 0);
  const after = await page.evaluate(() => ({ window: scrollY, modal: document.querySelector('[data-information-scroll="job-modal-body"]')?.scrollTop }));
  expect(Math.abs(before.window - after.window)).toBeLessThan(3);
  if (mode === "modal") {
    expect(Math.abs((before.modal ?? 0) - (after.modal ?? 0))).toBeLessThan(3);
    await page.goForward();
    await expect(returns(page, "Tillbaka till annonsen")).toHaveCount(2);
    await page.goBack();
    await expect(notice).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.locator(`a[href^="/jobb/${ADS.saved.id}"]`).filter({ hasText: ADS.saved.title })).toBeFocused();
  }
});

for (const entry of ["saved", "filtered-title"] as const) test(`job modal ${entry}: the exact opener and source query survive an information excursion (${rowInput})`, async ({ page, context, browserName }) => {
  await signIn(context);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(entry === "saved" ? "/sparade" : "/jobb");
  if (entry === "filtered-title") {
    const search = page.locator("#jobb-q");
    await expect(search).toHaveAttribute("role", "combobox");
    await search.fill("Testledare");
    await search.press("Enter");
    await expect(page).toHaveURL(url => url.pathname === "/jobb" && url.searchParams.get("q") === "Testledare");
  }
  const opener = entry === "saved"
    ? page.locator(`a.jp-job__rowlink[href="/jobb/${ADS.saved.id}"]`)
    : page.locator(`a.jp-job__rowlink[href^="/jobb/${ADS.saved.id}?"]`).filter({ hasText: ADS.saved.title });
  await expect(opener).toHaveCount(1);
  const href = await opener.getAttribute("href");
  expect(href).not.toBeNull();
  if (entry === "filtered-title") expect(new URL(href!, APP_ORIGIN).searchParams.get("q")).toBe("Testledare");
  await openRow(opener, browserName);
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
  const notice = page.locator(`#information-job-${ADS.saved.id}`);
  await notice.click();
  await expect(returns(page, "Tillbaka till annonsen")).toHaveCount(2);
  await returns(page, "Tillbaka till annonsen").first().click();
  await expect(notice).toBeFocused();
  await expect(page).toHaveURL(href!);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(opener).toBeFocused();
  if (entry === "filtered-title") {
    await expect(page).toHaveURL(url => url.pathname === "/jobb" && url.searchParams.get("q") === "Testledare");
    await expect(page.locator("#jobb-q")).toHaveValue("Testledare");
    await expect(opener).toHaveAttribute("href", href!);
  } else await expect(page).toHaveURL("/sparade");
});

for (const mode of ["full", "modal"] as const) test(`application ${mode}: list query, view, selection and focus survive${mode === "modal" ? ` (${rowInput})` : ""}`, async ({ page, context, browserName }) => {
  await signIn(context);
  await page.goto("/ansokningar");
  await page.getByRole("radio", { name: "Tabell", exact: true }).click();
  await page.getByRole("searchbox").fill("Backend");
  await page.getByRole("checkbox").last().check();
  const row = page.locator('a[href^="/ansokningar/"]').filter({ hasText: ADS.applied.title }).first();
  const href = await row.getAttribute("href");
  if (mode === "full") await page.goto(href ?? "");
  else {
    await openRow(row, browserName);
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(row).toBeFocused();
    await openRow(row, browserName);
    await expect(page.getByRole("dialog")).toBeVisible();
  }
  const notice = page.locator('a[id^="information-application-"]');
  await notice.scrollIntoViewIfNeeded();
  const before = await page.evaluate(() => ({ window: scrollY, modal: document.querySelector('[role="dialog"] .jp-modal__body')?.scrollTop }));
  if (mode === "modal") expect(before.modal).toBeGreaterThan(0);
  await notice.click();
  await expect(returns(page, "Tillbaka till ansökan")).toHaveCount(2);
  await returns(page, "Tillbaka till ansökan").last().click();
  await expect(notice).toBeFocused();
  expect(await page.getByRole("dialog").count()).toBe(mode === "modal" ? 1 : 0);
  const after = await page.evaluate(() => ({ window: scrollY, modal: document.querySelector('[role="dialog"] .jp-modal__body')?.scrollTop }));
  expect(Math.abs(before.window - after.window)).toBeLessThan(3);
  if (mode === "modal") expect(Math.abs((before.modal ?? 0) - (after.modal ?? 0))).toBeLessThan(3);
  if (mode === "modal") {
    await page.goForward();
    await expect(returns(page, "Tillbaka till ansökan")).toHaveCount(2);
    await page.goBack();
    await expect(notice).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.getByRole("searchbox")).toHaveValue("Backend");
    await expect(page.getByRole("checkbox").last()).toBeChecked();
    await expect(row).toBeFocused();
  }
});

for (const resource of ["job", "application"] as const) test(`${resource} modal WebKit mouse: main focus survives an information excursion`, async ({ page, context, browserName }) => {
  test.skip(browserName !== "webkit", "The existing mouse-to-main focus premise is specific to WebKit (#1968).");
  await signIn(context);
  await page.setViewportSize({ width: 390, height: 844 });
  const list = resource === "job" ? "/sparade" : "/ansokningar";
  await page.goto(list);
  const row = resource === "job"
    ? page.locator(`a[href^="/jobb/${ADS.saved.id}"]`).filter({ hasText: ADS.saved.title })
    : page.locator('a[href^="/ansokningar/"]').filter({ hasText: ADS.applied.title }).first();
  await row.click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  const main = page.locator("main#main");
  await expect(main).toBeFocused();

  await row.click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByRole("dialog").locator("header button").first()).toBeFocused();
  const notice = page.locator(`a[id^="information-${resource}-"]`);
  const returnName = resource === "job" ? "Tillbaka till annonsen" : "Tillbaka till ansökan";
  await notice.click();
  await expect(returns(page, returnName)).toHaveCount(2);
  await returns(page, returnName).first().click();
  await expect(notice).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page).toHaveURL(list);
  await expect(main).toBeFocused();
});

for (const resource of ["job", "application"] as const) for (const mode of ["full", "modal"] as const) test(`${resource} ${mode}: deliberately missing notice degrades to the restored surface heading`, async ({ page, context }) => {
  await signIn(context);
  await page.setViewportSize({ width: 390, height: 844 });
  if (resource === "job") {
    if (mode === "full") await page.goto(`/jobb/${ADS.saved.id}`);
    else {
      await page.goto("/sparade");
      await page.locator(`a[href^="/jobb/${ADS.saved.id}"]`).filter({ hasText: ADS.saved.title }).click();
      await expect(page.getByRole("dialog")).toBeVisible();
    }
  } else {
    await page.goto("/ansokningar");
    const row = page.locator('a[href^="/ansokningar/"]').filter({ hasText: ADS.applied.title }).first();
    if (mode === "full") await page.goto(await row.getAttribute("href") ?? "");
    else { await row.click(); await expect(page.getByRole("dialog")).toBeVisible(); }
  }
  const notice = page.locator(`a[id^="information-${resource}-"]`);
  const noticeId = await notice.getAttribute("id");
  expect(noticeId).not.toBeNull();
  await notice.click();
  await expect(returns(page, resource === "job" ? "Tillbaka till annonsen" : "Tillbaka till ansökan")).toHaveCount(2);
  // Deliberate unreachable DOM invariant break: assert only safe fallback, not production trigger loss.
  await page.evaluate(id => {
    const observer = new MutationObserver(() => {
      const trigger = document.getElementById(id);
      if (trigger) { trigger.remove(); observer.disconnect(); }
    });
    observer.observe(document.body, { subtree: true, childList: true });
  }, noticeId!);
  await page.locator('main a[data-information-return]').first().click();
  const heading = mode === "modal" ? page.getByRole("dialog").locator("h2[tabindex]").first() : page.locator("main h1[tabindex]").first();
  await expect(heading).toBeFocused();
  const directory = `C:/tmp/jbl-information-evidence/states/fallback-${resource}-${mode}`;
  mkdirSync(directory, { recursive: true });
  for (const width of [390, 768, 1280, 3440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.screenshot({ path: join(directory, `${width}.png`), animations: "disabled" });
  }
  if (mode === "modal") {
    await expect(heading).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(heading).not.toBeFocused();
    expect(await page.evaluate(() => document.activeElement?.closest('[role="dialog"]') !== null)).toBe(true);
    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => document.activeElement?.closest('[role="dialog"]') !== null)).toBe(true);
  }
});

test("CV privacy opens separately while the pending dialog and file remain local", async ({ page, context }) => {
  await signIn(context);
  await page.goto("/cv/importera");
  // ImportResumeCommandHandlerTests.Handle_PersonnummerInBody_DoesNotCaptureOriginal_AndNeverCallsSealer
  // pins the count-only outcome. This fixture tests navigation without invoking the PDF parser.
  await page.locator('input[type="file"]').setInputFiles({ name: "synthetic.pdf", mimeType: "application/pdf", buffer: Buffer.from(SYNTHETIC_CV) });
  await page.getByRole("button", { name: /Ladda upp/ }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  expect(harness.requests.filter(route => route === "POST /api/v1/resumes/import")).toHaveLength(1);
  await expect(dialog).toContainText("öppnas i en ny flik");
  const popupPromise = page.waitForEvent("popup");
  await dialog.locator('a[href="/integritet?context=cv-upload"]').click();
  const popup = await popupPromise;
  await expect(popup.locator("main")).toContainText("Fortsätt i fliken där du laddar upp ditt CV");
  await expect(returns(popup, "Till startsidan")).toHaveCount(2);
  await popup.close();
  await expect(dialog).toBeVisible();
  expect(harness.syntheticUploadReceipts).toEqual([true]);
  harness.rejectNextImport = true;
  await dialog.getByRole("button", { name: "Spara filen ändå", exact: true }).click();
  await expect.poll(() => harness.syntheticUploadReceipts).toEqual([true, true]);
});

test("consent policy links leave a checked box untouched and no completion is posted", async ({ page }) => {
  await page.goto("/logga-in");
  await page.locator('#email').fill("new@example.test");
  await page.locator('form button[type="submit"]').click();
  await expect(page).toHaveURL(/logga-in\/kod/);
  await page.locator('[name="code"]').fill("123456");
  await page.getByRole("button", { name: "Bekräfta koden", exact: true }).click();
  await expect(page).toHaveURL(/logga-in\/villkor/);
  await page.getByRole("checkbox").check();
  const popupPromise = page.waitForEvent("popup");
  await page.locator('form a[href="/villkor"]').click();
  const popup = await popupPromise;
  await expect(returns(popup, "Till startsidan")).toHaveCount(2);
  await expect(page.getByRole("checkbox")).toBeChecked();
  await popup.close();
  expect(harness.requests.filter(route => route === "POST /api/v1/auth/consent")).toHaveLength(0);
});

test("touch navigation preserves the email draft and job modal on a mobile viewport", async ({ browser }) => {
  const context = await browser.newContext({ ...devices["iPhone 13"], baseURL: APP_ORIGIN, ignoreHTTPSErrors: true });
  const page = await context.newPage();
  await page.goto("/logga-in?next=%2Fjobb");
  await page.locator('#email').fill("touch@example.test");
  await page.locator('#information-email-privacy').tap();
  await expect(returns(page, "Tillbaka till inloggningen")).toHaveCount(2);
  await returns(page, "Tillbaka till inloggningen").first().tap();
  await expect(page.locator('#email')).toHaveValue("touch@example.test");
  await expect(page.locator('#information-email-privacy')).toBeFocused();
  expect(harness.requests.filter(route => route === "POST /api/v1/auth/challenge")).toHaveLength(0);
  await signIn(context);
  await page.goto("/sparade");
  await page.locator(`a[href^="/jobb/${ADS.saved.id}"]`).filter({ hasText: ADS.saved.title }).tap();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.locator(`#information-job-${ADS.saved.id}`).tap();
  await expect(returns(page, "Tillbaka till annonsen")).toHaveCount(2);
  await returns(page, "Tillbaka till annonsen").first().tap();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.locator(`#information-job-${ADS.saved.id}`)).toBeFocused();
  await context.close();
});
