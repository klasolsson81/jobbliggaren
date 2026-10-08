import { expect, test, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(); });
test.afterAll(async () => { await harness.stop(); });
test.beforeEach(async ({ context, page }) => {
  harness.reset();
  await page.setViewportSize({ width: 1280, height: 1000 });
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
    secure: true, httpOnly: true, sameSite: "Strict" }]);
});
test.afterEach(() => expect(harness.misses).toEqual([]));

const card = (page: Page, name: string) => page.getByRole("region", { name, exact: true });
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const axePath = createRequire(cliRequire.resolve("lighthouse")).resolve("axe-core/axe.min.js");
async function verify(page: Page, state: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.addScriptTag({ path: axePath });
  const violations = await page.evaluate(async () => {
    const engine = (window as unknown as { axe: { run: (target: Document, options: unknown) =>
      Promise<{ violations: { id: string; impact: string | null }[] }> } }).axe;
    return (await engine.run(document, { runOnly: { type: "tag",
      values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } })).violations;
  });
  expect(violations, state).toEqual([]);
  const directory = process.env.ADMIN_OVERVIEW_SCREENSHOT_DIR;
  if (directory) {
    if (!win32.isAbsolute(directory) || !win32.resolve(directory).toLowerCase().startsWith("c:/tmp/".replaceAll("/", String.fromCharCode(92))))
      throw new Error("Overview screenshots require an absolute external C:/tmp directory");
    mkdirSync(directory, { recursive: true });
    await page.screenshot({ path: join(directory, `${state}-${page.viewportSize()?.width}.png`), fullPage: true, animations: "disabled" });
  }
}

for (const width of [1280, 1920, 3440]) {
  test(`overview observed data and keyboard periods at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1200 });
    await page.goto("/admin");
    await expect(card(page, "Användare totalt").getByRole("link", { name: "5", exact: true })).toBeVisible();
    await expect(card(page, "Aktiva användare")).toContainText("Kommer snart");
    await expect(card(page, "Inloggningar")).toContainText("Kommer snart");
    await expect(card(page, "Senaste händelser")).toContainText("Application.StatusTransitioned");
    await expect(card(page, "Kräver uppmärksamhet").getByRole("link", { name: "1 bakgrundsjobb har misslyckats" })).toBeVisible();
    await verify(page, "observed-30-days");
    const period = page.getByRole("radio", { name: "30 dagar", exact: true });
    await period.focus();
    await page.keyboard.press("ArrowLeft");
    await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toBeFocused();
    await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toHaveAttribute("aria-checked", "true");
    await verify(page, "observed");
  });
}

test("date drill-down preserves dates through search, status, sorting and clears explicitly", async ({ page }) => {
  await page.goto("/admin");
  const target = card(page, "Nya användare").getByRole("link", { name: /senaste 30 kalenderdagarna/i });
  const href = await target.getAttribute("href");
  expect(href).toMatch(/registeredFrom=.*registeredBefore=/);
  await target.click();
  await expect(page.getByRole("button", { name: "Rensa period" })).toBeVisible();
  await expect(page.getByRole("table", { name: "Konton" })).not.toContainText("konto.c@example.test");
  await page.getByRole("searchbox", { name: /Sök/ }).fill("konto");
  await expect.poll(() => harness.searches.length).toBeGreaterThan(1);
  const last = JSON.parse(harness.searches.at(-1) ?? "{}") as Record<string, unknown>;
  expect(last.registeredFrom).toBe(new URL(href ?? "", APP_ORIGIN).searchParams.get("registeredFrom"));
  expect(last.registeredBefore).toBe(new URL(href ?? "", APP_ORIGIN).searchParams.get("registeredBefore"));
  await page.getByRole("radio", { name: "Aktiva (3)", exact: true }).click();
  await expect.poll(() => JSON.parse(harness.searches.at(-1) ?? "{}").status).toBe("Active");
  await page.getByRole("button", { name: "Konto", exact: true }).click();
  await expect.poll(() => JSON.parse(harness.searches.at(-1) ?? "{}").sort).toBe("AddressAscending");
  const sorted = JSON.parse(harness.searches.at(-1) ?? "{}") as Record<string, unknown>;
  expect(sorted.registeredFrom).toBe(last.registeredFrom);
  expect(sorted.registeredBefore).toBe(last.registeredBefore);
  await page.getByRole("button", { name: "Rensa period" }).click();
  await expect(page).not.toHaveURL(/registeredFrom|registeredBefore/);
});

test("empty sources retain real zero counts and restricted attention statements", async ({ page }) => {
  harness.overviewEmpty = true;
  await page.goto("/admin");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "0", exact: true })).toBeVisible();
  await expect(card(page, "Senaste händelser")).toContainText("Inga händelser än.");
  await expect(card(page, "Kräver uppmärksamhet")).toContainText("Inga misslyckade bakgrundsjobb.");
  await expect(card(page, "Kräver uppmärksamhet")).toHaveAttribute("data-state", "unknown");
  await verify(page, "empty");
});

test("partial failure keeps other observed sources usable", async ({ page }) => {
  harness.overviewReads.accounts = "error";
  await page.goto("/admin");
  await expect(card(page, "Användare totalt")).toContainText("Uppgifterna kunde inte hämtas");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "0", exact: true })).toHaveCount(0);
  await expect(card(page, "Kräver uppmärksamhet")).toContainText("Väntande raderingar");
  await expect(card(page, "Senaste händelser")).toContainText("Application.StatusTransitioned");
  await verify(page, "partial-failure");
});

test("invalid sampling header is a source failure, never a fabricated zero", async ({ page }) => {
  harness.overviewSampledAt = "invalid";
  await page.goto("/admin");
  await expect(card(page, "Användare totalt")).toContainText("Uppgifterna kunde inte hämtas");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "0", exact: true })).toHaveCount(0);
  await verify(page, "invalid-sampling-time");
});

test("old observations are marked", async ({ page }) => {
  harness.overviewSampledAt = new Date(Date.now() - 300_001).toISOString();
  await page.goto("/admin");
  await expect(card(page, "Användare totalt")).toContainText("äldre än fem minuter");
  await verify(page, "old-observation");
});

test("refresh retains good values on ordinary failure and projects data before the browser", async ({ page }) => {
  await page.clock.install();
  await page.goto("/admin");
  await page.getByRole("radio", { name: "7 dagar", exact: true }).click();
  await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toHaveAttribute("aria-checked", "true");
  harness.overviewReads.accounts = "error";
  const response = page.waitForResponse(answer => answer.url().endsWith("/api/admin/oversikt"));
  await page.clock.runFor(61_000);
  const refresh = await response;
  expect(refresh.headers()["cache-control"]).toBe("private, no-store");
  const body = await refresh.text();
  expect(body).not.toMatch(/192\.0\.2|userAgent|correlationId|userId|jobId|jobType|errorCategory/);
  await expect(card(page, "Användare totalt")).toContainText("Uppdateringen misslyckades");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "5", exact: true })).toBeVisible();
  const announcement = page.getByRole("status").filter({ hasText: "Kontostatistik:" });
  await expect(announcement).toHaveAttribute("aria-live", "polite");
  await expect(announcement).toHaveAttribute("aria-atomic", "true");
  await expect(announcement).toContainText("Uppdateringen misslyckades");
  await verify(page, "retained-after-failure");
  harness.overviewReads.accounts = "ok";
  harness.overviewSampledAt = new Date().toISOString();
  const recovered = page.waitForResponse(answer => answer.url().endsWith("/api/admin/oversikt"));
  await page.clock.runFor(60_000);
  await recovered;
  await expect(announcement).toContainText("Uppgifterna är aktuella.");
});

for (const width of [1280, 3440]) for (const mode of ["unauthorized", "forbidden"] as const) {
  test(`refresh ${mode} clears all privileged data at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1200 });
    await page.clock.install();
    await page.goto("/admin");
    await page.getByRole("radio", { name: "7 dagar", exact: true }).click();
    await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toHaveAttribute("aria-checked", "true");
    harness.overviewReads.audit = mode;
    const response = page.waitForResponse(answer => answer.url().endsWith("/api/admin/oversikt"));
    await page.clock.runFor(61_000);
    expect((await response).status()).toBe(mode === "unauthorized" ? 401 : 403);
    await expect(page.getByRole("region", { name: "Användare totalt", exact: true })).toHaveCount(0);
    await expect(page.getByRole("alert").filter({ hasText: /Du är inte inloggad längre|Din session saknar Admin-rollen/ })).toContainText(mode === "unauthorized" ? "Du är inte inloggad längre" : "Din session saknar Admin-rollen");
    await verify(page, mode);
  });
}

test("two-hundred-percent equivalent viewport remains usable", async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 500 });
  await page.goto("/admin");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "5", exact: true })).toBeVisible();
  await verify(page, "zoom-200");
});

for (const width of [1280, 3440]) {
  test(`initial loading and source timeout at ${width}px`, async ({ page }) => {
    test.setTimeout(45_000);
    harness.overviewDelayMs = 11_000;
    await page.setViewportSize({ width, height: 1200 });
    const navigation = page.goto("/admin", { waitUntil: "commit" });
    await navigation;
    await expect(page.getByRole("status").filter({ hasText: "Hämtar" })).toBeVisible();
    await verify(page, "loading");
    await expect(card(page, "Användare totalt")).toContainText("Uppgifterna kunde inte hämtas", { timeout: 15_000 });
    await verify(page, "source-timeout");
  });
}
test("non-resting overview sections at 3440px", async ({ page }) => {
  await page.setViewportSize({ width: 3440, height: 1200 });
  harness.overviewEmpty = true;
  await page.goto("/admin");
  await expect(card(page, "Senaste händelser")).toContainText("Inga händelser än.");
  await verify(page, "empty");
  harness.reset();
  harness.overviewReads.accounts = "error";
  await page.goto("/admin");
  await expect(card(page, "Användare totalt")).toContainText("Uppgifterna kunde inte hämtas");
  await verify(page, "partial-failure");
});
test("invalid registration period offers a clear recovery", async ({ page }) => {
  await page.goto("/admin/anvandare?registeredFrom=invalid");
  await expect(page.getByRole("alert").filter({ hasText: "Filtret kunde inte läsas" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Rensa period", exact: true })).toBeVisible();
  await verify(page, "invalid-registration-period");
});

for (const width of [1024, 1280, 3440]) {
  test(`stored long audit event codes remain readable at ${width}px`, async ({ page }) => {
    harness.overviewLongEvent = true;
    await page.setViewportSize({ width, height: 1200 });
    await page.goto("/admin");
    await expect(card(page, "Senaste händelser")).toContainText("JobSeeker.FollowedCompanyNotificationConsentUpdated");
    await verify(page, "long-stored-event");
  });
}

for (const width of [1280, 3440, 640]) {
  test("account runtime boundary keeps the frame and heading focus at " + width + "px", async ({ page }) => {
    await page.setViewportSize({ width, height: width === 640 ? 500 : 1200 });
    await page.goto("/admin/anvandare", { waitUntil: "networkidle" });
    // The browser's failed script request is the actor; the panel's lazy import rejects.
    const failedScripts: string[] = [];
    await page.route("**/_next/static/chunks/*.js", async route => {
      failedScripts.push(route.request().url());
      await route.abort("failed");
    });
    await page.getByRole("button", { name: "konto.e@example.test", exact: true }).click();
    const heading = page.getByRole("heading", { name: "Sidan kunde inte visas", exact: true });
    await expect(heading).toBeVisible();
    expect(failedScripts.length).toBeGreaterThan(0);
    await expect(heading).toBeFocused();
    await expect(page.getByRole("banner")).toBeVisible();
    await expect(page.getByRole("contentinfo")).toBeVisible();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Försök igen", exact: true })).toBeFocused();
    await page.unroute("**/_next/static/chunks/*.js");
    await verify(page, "account-runtime-error");
  });
}
