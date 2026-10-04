import { expect, test, type Page } from "@playwright/test";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The seven admin routes in a real browser (#1973, ADR 0150): the nav reaches every page and marks
 * the current one, each page that is not built yet keeps its structure and says "Kommer snart" with
 * no number on it, Användare lists, searches and opens the accounts (#1974), Bakgrundsjobb and
 * Granskning still render their data and their refused and failed states, an ordinary account never
 * sees the surface, and no page scrolls sideways at phone width.
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

const PAGES = [
  { label: "Översikt", path: "/admin", heading: "Översikt" },
  { label: "Användare", path: "/admin/anvandare", heading: "Användare" },
  { label: "Feedback", path: "/admin/feedback", heading: "Feedback" },
  { label: "Loggar", path: "/admin/loggar", heading: "Loggar" },
  { label: "E-postleverans", path: "/admin/e-post", heading: "E-postleverans" },
  { label: "Bakgrundsjobb", path: "/admin/jobb", heading: "Bakgrundsjobb" },
  { label: "Granskning", path: "/admin/granskning", heading: "Granskning" },
] as const;

const UNBUILT = [
  "/admin",
  "/admin/feedback",
  "/admin/loggar",
  "/admin/loggar/applikationsfel",
  "/admin/loggar/platsbanken-import",
  "/admin/e-post",
] as const;

const adminNav = (page: Page) => page.getByRole("navigation", { name: "Admin-navigation" });

test("the nav reaches all seven pages in order and marks only the current one", async ({ page }) => {
  await page.goto("/admin");
  await expect(adminNav(page).getByRole("link")).toHaveText(PAGES.map((p) => p.label));

  for (const target of PAGES) {
    await adminNav(page).getByRole("link", { name: target.label }).click();
    await expect(page).toHaveURL(target.path);
    await expect(page.getByRole("heading", { level: 1, name: target.heading })).toBeVisible();
    await expect(adminNav(page).locator('[aria-current="page"]')).toHaveText(target.label);
  }
});

for (const path of UNBUILT) {
  test(`${path} keeps its structure, says Kommer snart and shows no number`, async ({ page }) => {
    await page.goto(path);
    await expect(page.getByText("Kommer snart").first()).toBeVisible();

    const main = page.locator("main");
    for (const button of await main.getByRole("button").all()) {
      // Every control on an unbuilt page is disabled: nothing on it acts.
      await expect(button).toBeDisabled();
    }
    // No fabricated figure: the only digits on these pages are period labels and a column name
    // ("24 tim", which the mono-caps header renders upper-case).
    const text = (await main.innerText()).replace(/\b(3|7|30|90) dygn\b|\b24 tim\b/gi, "");
    expect(text).not.toMatch(/\d/);
  });
}

test("Bakgrundsjobb and Granskning still render their data", async ({ page }) => {
  await page.goto("/admin/jobb");
  await expect(page.getByRole("cell", { name: "sync-platsbanken-stream" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "HttpRequestException" })).toBeVisible();

  await page.goto("/admin/granskning");
  await expect(page.getByRole("cell", { name: "Application.StatusTransitioned" })).toBeVisible();
});

const accountRows = (page: Page) => page.getByRole("table", { name: "Konton" }).locator("tbody tr");

const ACCOUNT_E = "00000000-0000-4000-8000-000000000005";

test("Användare lists the accounts with their counts, and searches by a body the URL never carries", async ({ page }) => {
  await page.goto("/admin/anvandare");
  await expect(accountRows(page)).toHaveCount(5);
  await expect(page.getByRole("radio", { name: "Ofullständiga (1)" })).toBeVisible();
  await expect(page.locator("main").getByRole("status").filter({ hasText: "konton" })).toHaveText("5 av 5 konton");

  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill("konto.d");
  await expect(accountRows(page)).toHaveCount(1);
  await expect(accountRows(page).first()).toContainText("Slutgiltigt tidigast 2026-10-30");
  await expect(accountRows(page).first().locator(".jp-adminusers__date")).toHaveCSS("white-space", "nowrap");

  expect(new URL(page.url()).search).toBe("");
  const searched = harness.searches.map((body) => JSON.parse(body) as { address?: string });
  expect(searched.at(-1)?.address).toBe("konto.d");
  expect(harness.requests.filter((route) => route.includes("konto.d"))).toEqual([]);
});

test("Användare opens an account in the panel, shows its details, and returns focus on Escape", async ({ page }) => {
  await page.goto("/admin/anvandare");
  const open = page.getByRole("button", { name: "konto.e@example.test" });
  await open.click();

  const panel = page.getByRole("dialog", { name: "konto.e@example.test" });
  await expect(panel.getByText("CV:n")).toBeVisible();
  await expect(panel.getByRole("button", { name: "Stäng" })).toBeFocused();
  await expect(panel.getByRole("button", { name: /Kommer snart$/ }).first()).toBeVisible();
  expect(harness.requests).toContain(`GET /api/v1/admin/accounts/${ACCOUNT_E}`);

  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden();
  await expect(open).toBeFocused();
});

test("an account gone by the time it opens says so, and Escape returns focus to the table", async ({ page }) => {
  await page.goto("/admin/anvandare");
  await expect(accountRows(page)).toHaveCount(5);
  harness.gone.add(ACCOUNT_E);
  await page.getByRole("button", { name: "konto.e@example.test" }).click();

  const panel = page.getByRole("dialog", { name: "konto.e@example.test" });
  await expect(panel.getByRole("alert")).toHaveText("Kontot finns inte längre.");
  // The open panel hides the page from the accessibility tree, so the rows are counted by their markup.
  await expect(page.locator("table.jp-adminusers tbody tr")).toHaveCount(4);
  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden();
  await expect(page.getByRole("region", { name: "Konton" })).toBeFocused();
});

test("paging to the last page and back keeps focus on the pager's button", async ({ page }) => {
  harness.many = true;
  await page.goto("/admin/anvandare");
  const pager = page.getByRole("navigation", { name: "Sidnavigering" });
  await expect(pager).toContainText("Sida 1 av 2");

  const next = pager.getByRole("button", { name: "Nästa" });
  await next.focus();
  await page.keyboard.press("Enter");
  await expect(pager).toContainText("Sida 2 av 2");
  await expect(next).toHaveAttribute("aria-disabled", "true");
  await expect(next).toBeFocused();

  const previous = pager.getByRole("button", { name: "Föregående" });
  await previous.focus();
  await page.keyboard.press("Enter");
  await expect(pager).toContainText("Sida 1 av 2");
  await expect(previous).toBeFocused();
});

test("a retried read that brings rows returns focus to the table", async ({ page }) => {
  harness.mode = "error";
  await page.goto("/admin/anvandare");
  const retry = page.getByRole("button", { name: "Försök igen" });
  await expect(retry).toBeVisible();

  harness.mode = "ok";
  await retry.click();
  await expect(accountRows(page)).toHaveCount(5);
  await expect(page.getByRole("region", { name: "Konton" })).toBeFocused();
});

for (const mode of ["forbidden", "error"] as const) {
  test(`a ${mode} account read says so in place of the rows and keeps the chrome`, async ({ page }) => {
    harness.mode = mode;
    await page.goto("/admin/anvandare");
    await expect(page.locator("main").getByRole("alert")).toHaveText(
      mode === "forbidden" ? "Din session saknar Admin-rollen." : "Kontona kunde inte hämtas. Försök igen om en stund.",
    );
    await expect(accountRows(page)).toHaveCount(1);
    await expect(adminNav(page)).toBeVisible();
  });
}

const ERROR_TITLE = { forbidden: "Saknar behörighet", error: "Kunde inte ladda jobbstatusen" } as const;

for (const mode of ["forbidden", "error"] as const) {
  test(`a ${mode} admin read renders its error block in both sections and keeps the chrome`, async ({ page }) => {
    harness.mode = mode;
    await page.goto("/admin/jobb");
    await expect(page.getByRole("heading", { level: 1, name: "Bakgrundsjobb" })).toBeVisible();
    await expect(page.getByText(ERROR_TITLE[mode])).toHaveCount(2);
    await expect(page.getByRole("cell")).toHaveCount(0);
    await expect(adminNav(page)).toBeVisible();
  });
}

const TABLE_ROUTES = [
  "/admin/loggar",
  "/admin/loggar/applikationsfel",
  "/admin/loggar/platsbanken-import",
  "/admin/e-post",
] as const;

/**
 * The table's region line and each link or button under it, measured against the scroll region:
 * `toBeVisible()` does not see clipping by an `overflow-x: auto` ancestor.
 */
async function regionLineParts(page: Page) {
  return page.evaluate(() => {
    const line = document.querySelector(
      "td.jp-admintable__soon > .jp-adminsoon, td.jp-admintable__soon > .jp-admintable__line"
    );
    const region = line?.closest(".jp-admintable-scroll");
    if (!line || !region) return null;
    const box = region.getBoundingClientRect();
    const range = document.createRange();
    range.selectNodeContents(line);
    const parts = [range.getBoundingClientRect(), ...Array.from(line.querySelectorAll("a, button"), (part) => part.getBoundingClientRect())];
    return parts.map((rect) => ({ width: rect.width, left: rect.left - box.left, right: box.right - rect.right }));
  });
}

function expectInside(parts: Awaited<ReturnType<typeof regionLineParts>>, where: string) {
  if (parts === null) throw new Error(`${where} has no region line inside a scroll region`);
  for (const part of parts) {
    expect(part.width, where).toBeGreaterThan(0);
    expect(part.left, where).toBeGreaterThanOrEqual(0);
    expect(part.right, where).toBeGreaterThanOrEqual(0);
  }
}

/** WCAG 1.4.12's spacing, as its bookmarklets apply it. */
const TEXT_SPACING =
  "* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }";

const ACCOUNT_LINES = [
  { state: "empty", mode: "ok", term: "ingen-traff", text: "Inga konton matchar sökningen eller filtret." },
  { state: "rate-limited", mode: "rateLimited", text: "För många förfrågningar." },
  { state: "signed-out", mode: "unauthorized", text: "Du är inte inloggad längre." },
  { state: "failed", mode: "error", text: "Kontona kunde inte hämtas." },
] as const;

for (const width of [320, 375]) {
  test(`each table's Kommer snart line sits inside its scroll region at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    for (const path of TABLE_ROUTES) {
      await page.goto(path);
      expectInside(await regionLineParts(page), `${path} at ${width}px`);
    }
  });

  for (const spaced of [false, true]) {
    test(`Användare's region lines and their actions sit inside the scroll region at ${width}px${spaced ? " with 1.4.12 spacing" : ""}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      for (const line of ACCOUNT_LINES) {
        harness.mode = line.mode;
        await page.goto("/admin/anvandare");
        if (spaced) await page.addStyleTag({ content: TEXT_SPACING });
        if ("term" in line) await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill(line.term);
        await expect(page.locator("td.jp-admintable__soon")).toContainText(line.text);
        expectInside(await regionLineParts(page), `the ${line.state} line at ${width}px`);
      }
    });
  }
}

test("an ordinary account is sent away from the admin surface", async ({ page }) => {
  harness.who = "member";
  const response = await page.goto("/admin");
  expect(new URL(page.url()).pathname).toBe("/");
  expect(response?.ok()).toBe(true);
  await expect(adminNav(page)).toHaveCount(0);
});

test("the header reads in DOM order: skip link, brand, the seven links, then the account", async ({ page }) => {
  await page.goto("/admin");
  const order: string[] = [];
  for (let i = 0; i < 10; i++) {
    await page.keyboard.press("Tab");
    order.push(
      await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        return (el?.getAttribute("aria-label") ?? el?.textContent ?? "").trim();
      })
    );
  }
  expect(order).toEqual([
    "Hoppa till huvudinnehåll",
    "Jobbliggaren, startsida",
    ...PAGES.map((p) => p.label),
    "Logga ut",
  ]);
});

for (const width of [320, 375, 768, 1024]) {
  test(`no admin page scrolls sideways at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    for (const target of PAGES) {
      await page.goto(target.path);
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth
      );
      expect(overflow, `${target.path} at ${width}px`).toBeLessThanOrEqual(0);
    }
  });
}
