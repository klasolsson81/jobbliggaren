import { chromium, expect, test, type BrowserContext, type Page } from "@playwright/test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve, win32 } from "node:path";
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
/** Alerts with content: Next's route announcer is an empty role=alert that is always there. */
const alerts = (page: Page) => page.getByRole("alert").filter({ hasText: /\S/ });
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const axePath = createRequire(cliRequire.resolve("lighthouse")).resolve("axe-core/axe.min.js");
async function verify(page: Page, state: string, nativeZoom?: number) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.getByRole("radiogroup").evaluateAll(async groups => {
    await Promise.all(groups.flatMap(group => group.getAnimations({ subtree: true })
      .map(animation => animation.finished.catch(() => undefined))));
  });
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
    const screenshotPath = join(directory, `${state}-${page.viewportSize()?.width}.png`);
    if (nativeZoom !== undefined) {
      const session = await page.context().newCDPSession(page);
      try {
        const { data } = await session.send("Page.captureScreenshot", {
          format: "png", fromSurface: true, captureBeyondViewport: false,
        });
        const png = Buffer.from(data, "base64");
        const width = png.readUInt32BE(16);
        const height = png.readUInt32BE(20);
        expect(width).toBe(page.viewportSize()?.width);
        expect(height).toBe(page.viewportSize()?.height);
        writeFileSync(screenshotPath, png);
        const layout = await page.evaluate(() => ({ pixelRatio: window.devicePixelRatio,
          innerWidth: window.innerWidth, scrollWidth: document.documentElement.scrollWidth }));
        writeFileSync(screenshotPath + ".json", JSON.stringify({
          capture: "chromium-native-viewport", nativeZoom, width, height, layout,
        }, null, 2));
      } finally { await session.detach(); }
    } else {
      await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
    }
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
  await page.getByRole("radio", { name: "Aktiva (2)", exact: true }).click();
  await expect.poll(() => JSON.parse(harness.searches.at(-1) ?? "{}").status).toBe("Active");
  await page.getByRole("button", { name: "Konto", exact: true }).click();
  await expect.poll(() => JSON.parse(harness.searches.at(-1) ?? "{}").sort).toBe("AddressAscending");
  const sorted = JSON.parse(harness.searches.at(-1) ?? "{}") as Record<string, unknown>;
  expect(sorted.registeredFrom).toBe(last.registeredFrom);
  expect(sorted.registeredBefore).toBe(last.registeredBefore);
  await page.getByRole("button", { name: "Rensa period" }).click();
  await expect(page).not.toHaveURL(/registeredFrom|registeredBefore/);
  await expect(page.getByRole("searchbox", { name: /Sök/ })).toHaveValue("konto");
  await expect(page.getByRole("radio", { name: "Aktiva (2)", exact: true })).toHaveAttribute("aria-checked", "true");
});

for (const query of [
  "registeredFrom=2026-09-08T22%3A00%3A00.000Z&registeredBefore=2026-10-08T12%3A00%3A00.000Z",
  "status=Suspended",
]) {
  test(`header users navigation resets directory filters: ${query}`, async ({ page }) => {
    await page.goto(`/admin/anvandare?${query}`);
    await expect(page.getByRole("table", { name: "Konton" })).not.toContainText("konto.c@example.test");
    await page.getByRole("link", { name: "Användare", exact: true }).click();
    await expect(page).toHaveURL(/\/admin\/anvandare$/);
    await expect(page.getByRole("button", { name: "Rensa period" })).toHaveCount(0);
    await expect(page.getByRole("table", { name: "Konton" })).toContainText("konto.c@example.test");
    await expect(page.getByRole("radio", { name: "Alla (5)", exact: true })).toHaveAttribute("aria-checked", "true");
  });
}

for (const width of [1280, 640]) {
  test(`overview drill links meet hit targets without overlap at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1200 });
    for (const empty of [false, true]) {
      harness.overviewEmpty = empty;
      await page.goto("/admin");
      const targets = await card(page, "Nya användare").getByRole("link")
        .or(card(page, "Användare totalt").getByRole("link"))
        .or(card(page, "Kräver uppmärksamhet").getByRole("link")).evaluateAll(links => links.map(link => {
        const rect = link.getBoundingClientRect();
        return { text: link.textContent, x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      }));
      expect(targets).toHaveLength(empty ? 9 : 11);
      const minimum = width <= 768 ? 44 : 32;
      for (const target of targets) {
        expect(target.width, target.text ?? "link width").toBeGreaterThanOrEqual(minimum);
        expect(target.height, target.text ?? "link height").toBeGreaterThanOrEqual(minimum);
      }
      for (let index = 0; index < targets.length; index++) for (const other of targets.slice(index + 1)) {
        const target = targets[index]!;
        const overlaps = target.x < other.x + other.width && other.x < target.x + target.width
          && target.y < other.y + other.height && other.y < target.y + target.height;
        expect(overlaps, `${target.text} / ${other.text}`).toBe(false);
      }
    }
  });
}

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

for (const width of [1280, 1920, 3440]) {
  test(`the Backup card shows the host's observation at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1200 });
    await page.goto("/admin");
    const backup = card(page, "Backup");
    await expect(backup).toContainText("Senaste lyckade körning");
    await expect(backup).toContainText("för 10 timmar sedan");
    await expect(backup).toContainText("Nästa planerade körning");
    await expect(backup).toContainText("Uppgift från");
    await expect(backup.getByText("Saknar verifierad datakälla")).toHaveCount(2);
    await expect(backup).toContainText("inte att en återställning fungerar");
    await verify(page, "backup-observed");
  });
}

test("the Backup card tells the state the box is in until the backup is switched on, without an alarm", async ({ page }) => {
  harness.overviewBackup = "switchedOff";
  await page.goto("/admin");
  const backup = card(page, "Backup");
  await expect(backup).toContainText("Ingen lyckad körning registrerad");
  await expect(backup).toContainText("Ingen körning planerad");
  await expect(alerts(page)).toHaveCount(0);
  await verify(page, "backup-switched-off");
});

test("the Backup card says that the host has not reported, which is neither a failure nor 'Kommer snart'", async ({ page }) => {
  harness.overviewBackup = "notObserved";
  await page.goto("/admin");
  const backup = card(page, "Backup");
  await expect(backup).toContainText("Värden har inte rapporterat någon observation ännu.");
  await expect(backup).not.toContainText("Kommer snart");
  await expect(backup).not.toContainText("kunde inte hämtas");
  await expect(alerts(page)).toHaveCount(0);
  await verify(page, "backup-awaiting");
});

test("a refused or unreadable host file is a failure of the Backup source only", async ({ page }) => {
  harness.overviewBackup = "failed";
  await page.goto("/admin");
  await expect(card(page, "Backup")).toContainText("Uppgifterna kunde inte hämtas");
  await expect(card(page, "Användare totalt").getByRole("link", { name: "5", exact: true })).toBeVisible();
  await verify(page, "backup-failed");
});

test("an old host sample keeps its own time and loses its age", async ({ page }) => {
  harness.overviewBackup = "old";
  await page.goto("/admin");
  const backup = card(page, "Backup");
  await expect(backup).toContainText("Uppgiften är äldre än fem minuter.");
  await expect(backup).not.toContainText("sedan");
  await verify(page, "backup-old");
});

test("a run older than twenty-six hours is marked, with its time", async ({ page }) => {
  harness.overviewBackup = "overdue";
  await page.goto("/admin");
  await expect(card(page, "Backup")).toContainText("Äldre än 26 timmar");
  await verify(page, "backup-overdue");
});

test("an unknown timer is an unknown value, never a date", async ({ page }) => {
  harness.overviewBackup = "timerUnknown";
  await page.goto("/admin");
  await expect(page.getByText("Nästa planerade körning").locator("xpath=following-sibling::dd[1]")).toContainText("Uppgift saknas");
  await verify(page, "backup-timer-unknown");
});

test("the host is asked only once an admin read has succeeded, and not at all when every first read refuses", async ({ page }) => {
  await page.goto("/admin");
  const overview = harness.requests.filter(route => route.includes("/overview/") || route.includes("/audit-log") || route.includes("/jobs/failed"));
  expect(overview.at(-1)).toBe("GET /api/v1/admin/overview/backup");
  expect(overview).toHaveLength(4);

  harness.requests.length = 0;
  harness.mode = "unauthorized";
  await page.goto("/admin");
  expect(harness.requests).not.toContain("GET /api/v1/admin/overview/backup");
});

test("a refresh keeps the last Backup value, marks the failure at once and announces it", async ({ page }) => {
  await page.clock.install();
  await page.goto("/admin");
  await expect(card(page, "Backup")).toContainText("Senaste lyckade körning");
  // Hydrated: the refresh timer exists only once the page's script has run.
  await page.getByRole("radio", { name: "7 dagar", exact: true }).click();
  await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toHaveAttribute("aria-checked", "true");
  harness.overviewReads.backup = "error";
  const response = page.waitForResponse(answer => answer.url().endsWith("/api/admin/oversikt"));
  await page.clock.runFor(61_000);
  await response;
  await expect(card(page, "Backup")).toContainText("Uppdateringen misslyckades");
  await expect(card(page, "Backup")).toContainText("Nästa planerade körning");
  await expect(page.getByRole("status").filter({ hasText: "Backup:" })).toContainText("Uppdateringen misslyckades");
  await verify(page, "backup-retained-after-failure");
});

test("a refresh that finds the Backup source refused clears every privileged value", async ({ page }) => {
  await page.clock.install();
  await page.goto("/admin");
  await page.getByRole("radio", { name: "7 dagar", exact: true }).click();
  await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toHaveAttribute("aria-checked", "true");
  harness.overviewReads.backup = "forbidden";
  const response = page.waitForResponse(answer => answer.url().endsWith("/api/admin/oversikt"));
  await page.clock.runFor(61_000);
  expect((await response).status()).toBe(403);
  await expect(page.getByRole("region", { name: "Backup", exact: true })).toHaveCount(0);
  await expect(page.getByRole("region", { name: "Användare totalt", exact: true })).toHaveCount(0);
  await verify(page, "backup-refused");
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

test("actual two-hundred-percent browser zoom keeps overview and drill-down usable", async () => {
  const temporaryRoot = resolve(tmpdir());
  const directory = mkdtempSync(join(temporaryRoot, "admin-overview-zoom-"));
  let zoomContext: BrowserContext | undefined;
  try {
    const extension = join(directory, "extension");
    mkdirSync(extension);
    writeFileSync(join(extension, "manifest.json"), JSON.stringify({
      manifest_version: 3, name: "Local admin zoom verification", version: "1.0",
      host_permissions: ["https://localhost/*"], background: { service_worker: "background.js" },
    }));
    writeFileSync(join(extension, "background.js"), "chrome.runtime.onInstalled.addListener(() => {});");
    zoomContext = await chromium.launchPersistentContext(join(directory, "profile"), {
      channel: "chromium", headless: true, ignoreHTTPSErrors: true, baseURL: APP_ORIGIN,
      viewport: { width: 1280, height: 1000 }, deviceScaleFactor: 1,
      args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`],
    });
    await zoomContext.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
      secure: true, httpOnly: true, sameSite: "Strict" }]);
    const worker = zoomContext.serviceWorkers()[0] ?? await zoomContext.waitForEvent("serviceworker");
    const page = zoomContext.pages()[0] ?? await zoomContext.newPage();
    await page.goto("/admin");
    const zoom = await worker.evaluate(async origin => {
      const tabs = (globalThis as unknown as { chrome: { tabs: {
        query: (options: { active: boolean; currentWindow: boolean }) => Promise<{ id?: number; url?: string }[]>;
        setZoom: (tabId: number, factor: number) => Promise<void>;
        getZoom: (tabId: number) => Promise<number>;
      } } }).chrome.tabs;
      const [tab] = await tabs.query({ active: true, currentWindow: true });
      if (typeof tab?.id !== "number" || !tab.url?.startsWith(origin + "/"))
        throw new Error("Zoom verification requires the local admin tab");
      await tabs.setZoom(tab.id, 2);
      return await tabs.getZoom(tab.id);
    }, APP_ORIGIN);
    expect(zoom).toBe(2);
    await expect.poll(() => page.evaluate(() => window.devicePixelRatio)).toBe(2);
    await expect.poll(() => page.evaluate(() => window.innerWidth)).toBe(640);
    await expect(card(page, "Användare totalt").getByRole("link", { name: "5", exact: true })).toBeVisible();
    await page.getByRole("radio", { name: "30 dagar", exact: true }).focus();
    await page.keyboard.press("ArrowLeft");
    await expect(page.getByRole("radio", { name: "7 dagar", exact: true })).toBeFocused();
    await page.getByRole("heading", { level: 1, name: "Översikt", exact: true }).scrollIntoViewIfNeeded();
    await verify(page, "actual-zoom-200-overview", zoom);
    await page.getByRole("radiogroup").scrollIntoViewIfNeeded();
    await verify(page, "actual-zoom-200-chart", zoom);
    await card(page, "Nya användare").getByRole("link", { name: /senaste 30 kalenderdagarna/i }).click();
    await expect(page.getByRole("table", { name: "Konton" })).toContainText("konto.b@example.test");
    await expect(page.getByRole("button", { name: "Rensa period" })).toBeVisible();
    await page.getByRole("heading", { level: 1, name: "Användare", exact: true }).scrollIntoViewIfNeeded();
    await verify(page, "actual-zoom-200-directory", zoom);
  } finally {
    try { await zoomContext?.close(); }
    finally {
      if (dirname(directory) !== temporaryRoot || !basename(directory).startsWith("admin-overview-zoom-"))
        throw new Error("Zoom cleanup target is outside its temporary root");
      rmSync(directory, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
    }
  }
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
    await expect(card(page, "Senaste händelser")).toContainText("00000000-0000-4000-8000-000000000021");
    const references = await card(page, "Senaste händelser").locator("li > span").evaluateAll(items => items.map(item => {
      const rect = item.getBoundingClientRect();
      const lineHeight = Number.parseFloat(getComputedStyle(item).lineHeight);
      return { width: rect.width, lines: rect.height / lineHeight };
    }));
    for (const reference of references) {
      expect(reference.width).toBeGreaterThanOrEqual(120);
      expect(reference.lines).toBeLessThanOrEqual(4);
    }
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
