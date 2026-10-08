import { chromium, expect, test, type Locator, type Page, type Request, type Response } from "@playwright/test";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import { pathToFileURL } from "node:url";
import { ADMIN, DELETION_AFTER_04, DELETION_BEFORE_04, DELETION_TIMING } from "./fixtures";
import { APP_ORIGIN, HARNESS_PORTS, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
const TARGET = "00000000-0000-4000-8000-000000000005";
const EMAIL = "konto.e@example.test";
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");
type AxeResult = { violations: { id: string; impact: string | null }[] };
type LighthouseReport = {
  readonly requestedUrl: string;
  readonly finalDisplayedUrl: string;
  readonly runtimeError?: unknown;
  readonly categories: Readonly<Record<string, { readonly score: number | null }>>;
  readonly audits: Readonly<Record<string, {
    readonly numericValue?: number;
    readonly details?: { readonly items?: ReadonlyArray<Record<string, unknown>> };
  }>>;
};
const median = (values: ReadonlyArray<number>) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)] ?? 0;

function outputDirectory() {
  const supplied = process.env.ADMIN_DELETION_SCREENSHOT_DIR;
  if (!supplied) return null;
  const directory = win32.resolve(supplied);
  const repository = win32.resolve(__dirname, "../../../..").toLowerCase();
  if (!win32.isAbsolute(supplied) || !/^c:\\tmp\\/i.test(directory)
    || directory.toLowerCase() === repository || directory.toLowerCase().startsWith(`${repository}\\`))
    throw new Error("Deletion screenshots require an absolute external C:/tmp directory.");
  mkdirSync(directory, { recursive: true });
  return directory;
}

async function inspect(page: Page, state: string) {
  const directory = outputDirectory();
  if (directory !== null) await page.screenshot({ path: join(directory, `${state}-${page.viewportSize()?.width}.png`), fullPage: true, animations: "disabled" });
  await page.addScriptTag({ path: axePath });
  const result = await page.evaluate(async () => {
    const axe = (window as unknown as { axe: { run: (root: Document, options: unknown) => Promise<AxeResult> } }).axe;
    return axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
  });
  expect(result.violations, state).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

async function open(page: Page) {
  await page.goto("/admin/anvandare");
  await page.getByRole("button", { name: EMAIL, exact: true }).click();
  const panel = page.getByRole("dialog", { name: EMAIL, exact: true });
  await expect(panel.getByRole("region", { name: "Åtgärder", exact: true })).toBeVisible();
  return panel;
}

async function begin(page: Page, panel: Locator) {
  await panel.getByRole("button", { name: "Radera konto", exact: true }).click();
  return page.getByRole("dialog", { name: `Radera ${EMAIL}?`, exact: true });
}

async function prove(dialog: Locator) {
  await dialog.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true }).click();
  await expect(dialog.getByLabel(`Kod till ${ADMIN.email}`)).toBeFocused();
  await dialog.getByLabel(`Kod till ${ADMIN.email}`).fill("123456");
}

test.beforeAll(async () => { harness = await startHarness(); });
test.afterAll(async () => { if (harness !== undefined) await harness.stop(); });
test.beforeEach(async ({ context }) => {
  harness.reset();
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" }]);
});
test.afterEach(() => { expect(harness.misses).toEqual([]); });

for (const width of [1280, 3440]) {
  test(`lazy account panel announces loading before its dialog at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/admin/anvandare", { waitUntil: "networkidle" });
    let release: (() => void) | undefined;
    const released = new Promise<void>(resolve => { release = resolve; });
    const requested: string[] = [];
    const chunks = "**/_next/static/chunks/*.js";
    await page.route(chunks, async route => {
      requested.push(route.request().url());
      await released;
      await route.continue();
    });
    try {
      await page.getByRole("button", { name: EMAIL, exact: true }).click();
      await expect(page.getByRole("status").filter({ hasText: "Hämtar kontots uppgifter…" })).toBeVisible();
      expect(requested.length).toBeGreaterThan(0);
      await inspect(page, "deletion-panel-module-loading");
      release?.();
      await expect(page.getByRole("dialog", { name: EMAIL, exact: true })).toBeVisible();
    } finally {
      release?.();
      await page.unroute(chunks);
    }
  });

  test(`failed panel script keeps the real error frame and focus then recovers at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/admin/anvandare", { waitUntil: "networkidle" });
    const failedScripts: string[] = [];
    await page.route("**/_next/static/chunks/*.js", async route => {
      failedScripts.push(route.request().url());
      await route.abort("failed");
    });
    await page.getByRole("button", { name: EMAIL, exact: true }).click();
    const heading = page.getByRole("heading", { name: "Sidan kunde inte visas", exact: true });
    await expect(heading).toBeVisible();
    expect(failedScripts.length).toBeGreaterThan(0);
    await expect(heading).toBeFocused();
    await expect(page.getByRole("banner")).toBeVisible();
    await expect(page.getByRole("contentinfo")).toBeVisible();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Försök igen", exact: true })).toBeFocused();
    await page.unroute("**/_next/static/chunks/*.js");
    await inspect(page, "deletion-panel-module-error");
    await page.getByRole("button", { name: "Försök igen", exact: true }).click();
    await expect(page.getByRole("table", { name: "Konton" })).toContainText(EMAIL);
    await expect(heading).not.toBeVisible();
    await page.getByRole("button", { name: EMAIL, exact: true }).click();
    const panel = page.getByRole("dialog", { name: EMAIL, exact: true });
    await expect(panel.getByRole("region", { name: "Åtgärder", exact: true })).toBeVisible();
    await inspect(page, "deletion-panel-module-recovered");
    const dialog = await begin(page, panel);
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveAccessibleDescription(/30 dagars respit/);
    await page.keyboard.press("Escape");
    await expect(dialog).not.toBeVisible();
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeFocused();
    expect(harness.deletionRequests).toHaveLength(0);
  });

  test(`deletion preview rereads after 04 UTC, gates codes and fails closed on reopening at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.deletionTiming = DELETION_BEFORE_04;
    const panel = await open(page);
    harness.holdAccountDetails = true;
    const abortedReads: string[] = [];
    const observeAbort = (request: Request) => {
      if (new URL(request.url()).pathname === "/api/admin/konton/detalj")
        abortedReads.push(request.failure()?.errorText ?? "Missing request failure");
    };
    page.on("requestfailed", observeAbort);
    try {
      const dialog = await begin(page, panel);
      const send = dialog.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true });
      await expect.poll(() => harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(2);
      // The HTTP answer has captured the pre-04 calculation; advancing the clock cannot rewrite it on release.
      harness.deletionTiming = DELETION_AFTER_04;
      await expect(dialog).toHaveAccessibleDescription(/Läser in kontots aktuella status/);
      await expect(send).toBeDisabled();
      await expect(dialog.getByRole("button", { name: "Avbryt", exact: true })).toBeFocused();
      await inspect(page, "deletion-preview-loading");
      await page.keyboard.press("Enter");
      await expect(dialog).not.toBeVisible();
      await expect.poll(() => abortedReads).toEqual(["net::ERR_ABORTED"]);
      await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeFocused();
      expect(harness.requests.filter(path => path === "POST /api/v1/auth/reauth")).toHaveLength(0);
      // A close aborts the old read. The next opening must make its own successful live read.
      harness.releaseAccountDetails();
      await expect(page.getByRole("dialog", { name: `Radera ${EMAIL}?`, exact: true })).toHaveCount(0);
      expect(harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(2);
      const reopened = await begin(page, panel);
      await expect(reopened).toHaveAccessibleDescription(/beräknades av servern 2026-10-08 06:01/);
      await expect(reopened).toHaveAccessibleDescription(/2026-11-08 05:00/);
      await expect(reopened).not.toHaveAccessibleDescription(/2026-11-07 05:00/);
      await expect(reopened).toHaveAccessibleDescription(/de faktiska tiderna visas i kvittot/);
      await expect(reopened.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true })).toBeEnabled();
      expect(harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(3);
      await inspect(page, "deletion-preview-current");
      await prove(reopened);
      await page.keyboard.press("Escape");
      await expect(reopened).not.toBeVisible();
      harness.mode = "error";
      const failed = await begin(page, panel);
      const alert = failed.getByRole("alert");
      await expect(alert).toContainText("Kontots uppgifter kunde inte hämtas.");
      await expect(alert).toBeFocused();
      await expect(failed.getByLabel(`Kod till ${ADMIN.email}`)).toBeDisabled();
      await expect(failed.getByRole("button", { name: "Radera konto", exact: true })).toBeDisabled();
      await inspect(page, "deletion-preview-refused");
      expect(harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(4);
      expect(harness.requests.filter(path => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
      expect(harness.reauthVerifications).toHaveLength(0);
      expect(harness.deletionRequests).toHaveLength(0);
      await page.keyboard.press("Escape");
      await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeFocused();
    } finally {
      harness.releaseAccountDetails();
      page.off("requestfailed", observeAbort);
    }
  });

  test(`deletion confirmation, loading, receipt and actual pending data at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.deletionTiming = DELETION_BEFORE_04;
    harness.emailChanges.add(TARGET);
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await expect(dialog).toHaveAccessibleDescription(/30 dagars respit/);
    await expect(dialog).toHaveAccessibleDescription(/adressbyte avbryts permanent/);
    await expect(dialog).toHaveAccessibleDescription(/ingen garanti/);
    await expect(dialog).toHaveAccessibleDescription(/beräknades av servern 2026-10-08 05:59/);
    await expect(dialog).toHaveAccessibleDescription(/2026-11-07 05:00/);
    await inspect(page, "deletion-confirmation");
    harness.holdCodeRequests = true;
    await dialog.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true }).click();
    await expect(dialog.getByRole("button", { name: `Skickar kod till ${ADMIN.email}…` })).toBeDisabled();
    await inspect(page, "deletion-code-loading");
    // The real injected-clock producer advances while the administrator waits for their inbox code.
    harness.deletionTiming = DELETION_AFTER_04;
    harness.releaseCodeRequests();
    await expect(dialog.getByLabel(`Kod till ${ADMIN.email}`)).toBeFocused();
    await expect(dialog).toHaveAccessibleDescription(/beräknades av servern 2026-10-08 05:59/);
    await expect(dialog).toHaveAccessibleDescription(/2026-11-07 05:00/);
    expect(harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(2);
    await dialog.getByLabel(`Kod till ${ADMIN.email}`).fill("123456");
    harness.holdAccessWrites = true;
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    await expect(dialog.getByRole("button", { name: "Schemalägger…" })).toBeDisabled();
    await inspect(page, "deletion-write-loading");
    harness.releaseAccessWrites();
    const receipt = panel.getByRole("region", { name: "Åtgärder", exact: true }).getByRole("status");
    await expect(receipt).toContainText(`Radering av ${EMAIL} schemalagd`);
    await expect(receipt).toContainText("schemalagd 2026-10-08 06:01");
    await expect(receipt).toContainText("Respiten slutar 2026-11-07 05:01");
    await expect(receipt).toContainText("Första planerade körning 2026-11-08 05:00");
    await expect(panel).toContainText("Raderingen är ännu inte genomförd.");
    await expect(receipt).toBeFocused();
    await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
    await expect(panel.getByText("Adressbyte", { exact: true })).toHaveCount(0);
    expect(harness.deletionRequests).toHaveLength(1);
    expect(harness.deletions.get(TARGET)).toEqual(DELETION_AFTER_04);
    await inspect(page, "deletion-receipt");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("radio", { name: "Under radering (2)" })).toBeVisible();
    await expect(page.getByRole("table", { name: "Konton" })).toContainText("Planerad körning");
    await inspect(page, "deletion-pending-directory");
  });

  test(`known deletion receipt stays pending when its automatic detail refresh fails at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.accountDetailFailuresAfterDeletion.add(TARGET);
    harness.emailChanges.add(TARGET);
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await prove(dialog);
    harness.holdAccountDetails = true;
    const detailResponses: Response[] = [];
    const observeDetailResponse = (response: Response) => {
      if (new URL(response.url()).pathname === "/api/admin/konton/detalj")
        detailResponses.push(response);
    };
    await page.evaluate(() => {
      const witnessedWindow = window as Window & {
        deletionDetailFetchWitness?: { readonly statuses: number[]; readonly restore: () => void };
      };
      const originalFetch = window.fetch;
      const realFetch = originalFetch.bind(window);
      const statuses: number[] = [];
      const observedFetch: typeof window.fetch = async (...args) => {
        const response = await realFetch(...args);
        if (new URL(response.url).pathname === "/api/admin/konton/detalj") statuses.push(response.status);
        return response;
      };
      window.fetch = observedFetch;
      witnessedWindow.deletionDetailFetchWitness = {
        statuses,
        restore: () => { window.fetch = originalFetch; delete witnessedWindow.deletionDetailFetchWitness; },
      };
    });
    page.on("response", observeDetailResponse);
    try {
      await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
      const receipt = panel.getByRole("region", { name: "Åtgärder", exact: true }).getByRole("status");
      await expect(receipt).toContainText(`Radering av ${EMAIL} schemalagd`);
      await expect(receipt).toContainText("schemalagd 2026-10-08 14:00");
      await expect(receipt).toContainText("Respiten slutar 2026-11-07 13:00");
      await expect(receipt).toContainText("Första planerade körning 2026-11-08 05:00");
      await expect(receipt).toBeFocused();
      await expect.poll(() => harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(3);
      expect(detailResponses).toHaveLength(0);
      await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
      await expect(panel.getByText("Aktiv", { exact: true })).toHaveCount(0);
      await expect(panel).toContainText("Respiten slutar 2026-11-07 13:00. Första planerade körning 2026-11-08 05:00, svensk tid. Raderingen är ännu inte genomförd.");
      await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toHaveCount(0);
      await expect(panel.getByText("Adressbyte", { exact: true })).toHaveCount(0);
      expect(harness.deletions.get(TARGET)).toEqual(DELETION_TIMING);
      expect(harness.deletionRequests).toHaveLength(1);
      await inspect(page, "deletion-known-receipt-refresh-held");

      await test.step("the production detail fetch resolves with the real BFF failure", async () => {
        harness.releaseAccountDetails();
        await expect.poll(() => detailResponses.length).toBe(1);
        const failedRefresh = detailResponses[0];
        if (failedRefresh === undefined) throw new Error("The automatic detail refresh must answer its held HTTP failure.");
        expect(failedRefresh.status()).toBe(502);
        // The production non-ok path settles at status without consuming the error body.
        await expect.poll(() => page.evaluate(() => {
          const witnessedWindow = window as Window & {
            deletionDetailFetchWitness?: { readonly statuses: number[] };
          };
          return witnessedWindow.deletionDetailFetchWitness?.statuses ?? [];
        }), { timeout: 5000 }).toEqual([502]);
      });
      await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
      await expect(panel.getByText("Aktiv", { exact: true })).toHaveCount(0);
      await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toHaveCount(0);
      await expect(receipt).toContainText(`Radering av ${EMAIL} schemalagd`);
      await expect(receipt).toContainText("Raderingen är ännu inte genomförd.");
      expect(harness.requests.filter(path => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
      expect(harness.reauthVerifications).toHaveLength(1);
      expect(harness.deletionRequests).toHaveLength(1);
      expect(harness.requests.filter(path => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(3);
      await inspect(page, "deletion-known-receipt-refresh-failed");
      await page.keyboard.press("Escape");
      await expect(page.getByRole("radio", { name: "Under radering (2)" })).toBeVisible();
      await expect(page.getByRole("radio", { name: "Aktiva (2)" })).toBeVisible();
      const row = page.getByRole("table", { name: "Konton" }).getByRole("row").filter({
        has: page.getByRole("button", { name: EMAIL, exact: true }),
      });
      await expect(row).toHaveCount(1);
      await expect(row).toContainText("Under radering");
      await expect(row).toContainText("2026-11-08 05:00");
      expect(harness.requests.filter(path => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
      expect(harness.reauthVerifications).toHaveLength(1);
      expect(harness.deletionRequests).toHaveLength(1);
    } finally {
      harness.releaseAccountDetails();
      page.off("response", observeDetailResponse);
      await page.evaluate(() => {
        const witnessedWindow = window as Window & {
          deletionDetailFetchWitness?: { readonly restore: () => void };
        };
        witnessedWindow.deletionDetailFetchWitness?.restore();
      });
    }
  });

  test(`known deletion refusal receives focus without a success receipt at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.deletionMode = "alreadyPending";
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await prove(dialog);
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    const alert = panel.getByRole("alert");
    await expect(alert).toContainText("Datumet har inte ändrats.");
    await expect(alert).toBeFocused();
    expect(harness.deletions.size).toBe(0);
    await inspect(page, "deletion-noop-refusal");
  });

  test(`unknown committed deletion waits for explicit reread with no replay at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.deletionMode = "unknownAfterCommit";
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await prove(dialog);
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    const status = panel.getByRole("region", { name: "Åtgärder", exact: true }).getByRole("status");
    await expect(status).toContainText("Det går inte att bekräfta om raderingen schemalades.");
    await expect(status).toBeFocused();
    await expect(panel.getByText("Under radering", { exact: true })).toHaveCount(0);
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(2);
    const trigger = panel.getByRole("button", { name: "Radera konto", exact: true });
    await expect(trigger).toBeDisabled();
    // Send a real pointer press to the unavailable trigger: it must not open another code flow.
    await trigger.click({ force: true });
    await expect(page.getByRole("dialog", { name: `Radera ${EMAIL}?`, exact: true })).toHaveCount(0);
    expect(harness.requests.filter((path) => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.deletionRequests).toHaveLength(1);
    await inspect(page, "deletion-outcome-unknown");

    harness.mode = "error";
    await panel.getByRole("button", { name: "Läs in kontots status", exact: true }).click();
    await expect(panel.getByRole("alert")).toContainText("Kontots uppgifter kunde inte hämtas.");
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toHaveCount(0);
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(3);
    expect(harness.requests.filter((path) => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.deletionRequests).toHaveLength(1);
    await inspect(page, "deletion-unknown-reread-failed");

    harness.mode = "ok";
    await panel.getByRole("button", { name: "Försök igen", exact: true }).click();
    await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
    await expect(panel).toContainText("Respiten slutar 2026-11-07 13:00. Första planerade körning 2026-11-08 05:00, svensk tid. Raderingen är ännu inte genomförd.");
    expect(harness.deletions.get(TARGET)).toEqual(DELETION_TIMING);
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toHaveCount(0);
    await expect(panel.getByRole("button", { name: "Läs in kontots status", exact: true })).toHaveCount(0);
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(4);
    expect(harness.requests.filter((path) => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.deletionRequests).toHaveLength(1);
    await inspect(page, "deletion-reread-pending");
  });

  test(`unknown deletion before commit unlocks only after a successful explicit reread at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    // The harness returns 503 before its deletion transition; unknownAfterCommit covers the other outcome.
    harness.deletionMode = "unknown";
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await prove(dialog);
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    const status = panel.getByRole("region", { name: "Åtgärder", exact: true }).getByRole("status");
    await expect(status).toContainText("Det går inte att bekräfta om raderingen schemalades.");
    await expect(status).toBeFocused();
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeDisabled();
    expect(harness.deletions.size).toBe(0);
    expect(harness.deletionRequests).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(2);

    await panel.getByRole("button", { name: "Läs in kontots status", exact: true }).click();
    await expect(panel.getByText("Aktiv", { exact: true })).toBeVisible();
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeEnabled();
    await expect(panel.getByRole("button", { name: "Läs in kontots status", exact: true })).toHaveCount(0);
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(3);
    expect(harness.requests.filter((path) => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.deletionRequests).toHaveLength(1);
    await inspect(page, "deletion-reread-active");

    const retryDialog = await begin(page, panel);
    await expect(retryDialog.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true })).toBeEnabled();
    expect(harness.requests.filter((path) => path === "POST /api/v1/auth/reauth")).toHaveLength(1);
    expect(harness.reauthVerifications).toHaveLength(1);
    expect(harness.deletionRequests).toHaveLength(1);
    await page.keyboard.press("Escape");
    await expect(panel.getByRole("button", { name: "Radera konto", exact: true })).toBeFocused();
  });

  test(`code refusal remains in the real dialog at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.codeMode = "rateLimited";
    const dialog = await begin(page, await open(page));
    await prove(dialog);
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    await expect(dialog).toContainText("För många försök");
    expect(harness.deletionRequests).toHaveLength(0);
    await inspect(page, "deletion-code-refusal");
  });
}

test("keyboard opens deletion, traps dialog focus and returns to its trigger on cancel", async ({ page }) => {
  const panel = await open(page);
  const trigger = panel.getByRole("button", { name: "Radera konto", exact: true });
  for (let count = 0; count < 25 && !await trigger.evaluate((node) => node === document.activeElement); count++)
    await page.keyboard.press("Tab");
  await expect(trigger).toBeFocused();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: `Radera ${EMAIL}?`, exact: true });
  for (let count = 0; count < 8; count++) {
    await page.keyboard.press("Tab");
    expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
  expect(harness.deletionRequests).toHaveLength(0);
});

test("scoped authenticated account directory Lighthouse", async () => {
  test.setTimeout(180_000);
  const directory = outputDirectory();
  test.skip(directory === null, "Lighthouse evidence needs the explicitly configured external artifact directory.");
  const lighthouse = (await import(pathToFileURL(cliRequire.resolve("lighthouse")).href) as {
    default: (url: string, options: unknown, config: unknown) => Promise<{ lhr: LighthouseReport }>;
  }).default;
  const desktopConfig = (await import(pathToFileURL(lighthouseRequire.resolve("./config/desktop-config.js")).href) as {
    default: unknown;
  }).default;
  const port = HARNESS_PORTS.backend + 1;
  const browser = await chromium.launch({ headless: true,
    args: ["--ignore-certificate-errors", `--remote-debugging-port=${port}`] });
  const reports: LighthouseReport[] = [];
  const observations: unknown[] = [];
  try {
    const connected = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
    const context = connected.contexts()[0];
    if (!context) throw new Error("Lighthouse requires the default Chromium context.");
    await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
      secure: true, httpOnly: true, sameSite: "Strict" }]);
    const control = await context.newPage();
    const url = `${APP_ORIGIN}/admin/anvandare`;
    await control.goto(url);
    await expect(control.getByRole("table", { name: "Konton" })).toContainText(EMAIL);
    await control.goto("about:blank");
    for (let run = 0; run < 3; run++) {
      const requestStart = harness.requests.length;
      const report = (await lighthouse(url, {
        port,
        onlyCategories: ["performance", "accessibility", "best-practices"],
        formFactor: "desktop",
        screenEmulation: { mobile: false, width: 1280, height: 900, deviceScaleFactor: 1, disabled: false },
        throttlingMethod: "simulate",
        disableStorageReset: false,
        clearStorageTypes: ["file_systems", "shader_cache", "service_workers", "cache_storage"],
        logLevel: "error",
      }, desktopConfig)).lhr;
      reports.push(report);
      const read = "POST /api/v1/admin/accounts/search";
      const witnessed = harness.requests.slice(requestStart).includes(read);
      observations.push({ run, normalContentPreflight: true, requestedUrl: report.requestedUrl,
        finalDisplayedUrl: report.finalDisplayedUrl, requiredBackendRead: witnessed });
      expect(report.runtimeError ?? null).toBeNull();
      expect(report.requestedUrl).toBe(url);
      expect(report.finalDisplayedUrl).toBe(url);
      expect(witnessed).toBe(true);
    }
    for (const key of ["performance", "accessibility", "best-practices"])
      expect(median(reports.map(report => report.categories[key]?.score ?? 0))).toBeGreaterThan(0.9);
    const config = JSON.parse(readFileSync(join(__dirname, "../../lighthouserc.json"), "utf8")) as {
      ci: { assert: { assertions: Record<string, unknown> } };
    };
    for (const [id, rule] of Object.entries(config.ci.assert.assertions)) {
      if (!Array.isArray(rule)) continue;
      if (!id.startsWith("resource-summary:")
        && id !== "largest-contentful-paint" && id !== "cumulative-layout-shift") continue;
      const [, resourceType, field] = id.split(":");
      const budget = rule[1] as { maxNumericValue: number };
      const values = reports.map(report => {
        if (!id.startsWith("resource-summary:")) {
          const observed = report.audits[id]?.numericValue;
          if (typeof observed !== "number" || !Number.isFinite(observed) || observed < 0)
            throw new Error(`Missing or invalid Lighthouse timing observation: ${id}`);
          return observed;
        }
        const row = report.audits["resource-summary"]?.details?.items?.find(item => item.resourceType === resourceType);
        const observed = row?.[field === "size" ? "transferSize" : "requestCount"];
        if (typeof observed !== "number" || !Number.isFinite(observed) || observed < 0)
          throw new Error(`Missing or invalid Lighthouse resource observation: ${id}`);
        return observed;
      });
      expect(median(values), id).toBeLessThanOrEqual(budget.maxNumericValue);
    }
  } finally {
    try {
      writeFileSync(join(directory!, "deletion-lighthouse.json"), JSON.stringify({
        method: "secure synthetic cookie in default Chromium context; populated preflight; audited-target cache/origin reset retaining cookies; per-run backend-read and exact-URL witnesses; desktop1280x900; three-run medians",
        observations, reports,
      }, null, 2));
    } finally { await browser.close(); }
  }
});
