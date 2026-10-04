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

test("Användare lists the accounts with their counts, and searches by a body the URL never carries", async ({ page }) => {
  await page.goto("/admin/anvandare");
  await expect(accountRows(page)).toHaveCount(5);
  await expect(page.getByRole("radio", { name: "Ofullständiga (1)" })).toBeVisible();
  await expect(page.locator("main").getByRole("status").filter({ hasText: "konton" })).toHaveText("5 av 5 konton");

  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill("konto.d");
  await expect(accountRows(page)).toHaveCount(1);
  await expect(accountRows(page).first()).toContainText("Slutgiltigt tidigast 2026-10-30");

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
  expect(harness.requests).toContain("GET /api/v1/admin/accounts/00000000-0000-4000-8000-000000000005");

  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden();
  await expect(open).toBeFocused();
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

for (const width of [320, 375]) {
  test(`each table's Kommer snart line sits inside its scroll region at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    for (const path of TABLE_ROUTES) {
      await page.goto(path);
      // `toBeVisible()` does not see clipping by an `overflow-x: auto` ancestor, so the text's own
      // rectangle is compared with the region's.
      const fit = await page.evaluate(() => {
        const line = document.querySelector("td.jp-admintable__soon .jp-adminsoon");
        const region = line?.closest(".jp-admintable-scroll");
        if (!line || !region) return null;
        const range = document.createRange();
        range.selectNodeContents(line);
        const text = range.getBoundingClientRect();
        const box = region.getBoundingClientRect();
        return { width: text.width, left: text.left - box.left, right: box.right - text.right };
      });
      if (fit === null) throw new Error(`${path} has no Kommer snart row inside a scroll region`);
      expect(fit.width, path).toBeGreaterThan(0);
      expect(fit.left, `${path} at ${width}px`).toBeGreaterThanOrEqual(0);
      expect(fit.right, `${path} at ${width}px`).toBeGreaterThanOrEqual(0);
    }
  });
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
