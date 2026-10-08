import { expect, test, type Locator, type Page } from "@playwright/test";
import { execFile } from "node:child_process";
import { mkdirSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import { promisify } from "node:util";
import { ADMIN, DELETION_TIMING } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

let harness: Harness;
const TARGET = "00000000-0000-4000-8000-000000000005";
const EMAIL = "konto.e@example.test";
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");
const executeFile = promisify(execFile);
type AxeResult = { violations: { id: string; impact: string | null }[] };

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
  test(`deletion confirmation, loading, receipt and actual pending data at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.emailChanges.add(TARGET);
    const panel = await open(page);
    const dialog = await begin(page, panel);
    await expect(dialog).toHaveAccessibleDescription(/30 dagars respit/);
    await expect(dialog).toHaveAccessibleDescription(/adressbyte avbryts permanent/);
    await expect(dialog).toHaveAccessibleDescription(/ingen garanti/);
    await inspect(page, "deletion-confirmation");
    harness.holdCodeRequests = true;
    await dialog.getByRole("button", { name: `Skicka kod till ${ADMIN.email}`, exact: true }).click();
    await expect(dialog.getByRole("button", { name: `Skickar kod till ${ADMIN.email}…` })).toBeDisabled();
    await inspect(page, "deletion-code-loading");
    harness.releaseCodeRequests();
    await expect(dialog.getByLabel(`Kod till ${ADMIN.email}`)).toBeFocused();
    await dialog.getByLabel(`Kod till ${ADMIN.email}`).fill("123456");
    harness.holdAccessWrites = true;
    await dialog.getByRole("button", { name: "Radera konto", exact: true }).click();
    await expect(dialog.getByRole("button", { name: "Schemalägger…" })).toBeDisabled();
    await inspect(page, "deletion-write-loading");
    harness.releaseAccessWrites();
    const receipt = panel.getByRole("region", { name: "Åtgärder", exact: true }).getByRole("status");
    await expect(receipt).toContainText(`Radering av ${EMAIL} schemalagd`);
    await expect(panel).toContainText("Raderingen är ännu inte genomförd.");
    await expect(receipt).toBeFocused();
    await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
    await expect(panel.getByText("Adressbyte", { exact: true })).toHaveCount(0);
    expect(harness.deletionRequests).toHaveLength(1);
    expect(harness.deletions.get(TARGET)).toEqual(DELETION_TIMING);
    await inspect(page, "deletion-receipt");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("radio", { name: "Under radering (2)" })).toBeVisible();
    await expect(page.getByRole("table", { name: "Konton" })).toContainText("Planerad körning");
    await inspect(page, "deletion-pending-directory");
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
    expect(harness.requests.filter((path) => path === `GET /api/v1/admin/accounts/${TARGET}`)).toHaveLength(1);
    await inspect(page, "deletion-outcome-unknown");
    await panel.getByRole("button", { name: "Läs in kontots status", exact: true }).click();
    await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
    expect(harness.deletionRequests).toHaveLength(1);
    await inspect(page, "deletion-reread-pending");
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

test("scoped authenticated account directory Lighthouse", async ({ page }) => {
  test.setTimeout(120_000);
  const directory = outputDirectory();
  test.skip(directory === null, "Lighthouse evidence needs the explicitly configured external artifact directory.");
  await page.goto("/admin/anvandare");
  const output = join(directory!, "deletion-lighthouse.json");
  await executeFile(process.execPath, [cliRequire.resolve("lighthouse/cli/index.js"), `${APP_ORIGIN}/admin/anvandare`,
    "--output=json", `--output-path=${output}`, "--only-categories=performance,accessibility,best-practices",
    "--preset=desktop", "--screenEmulation.mobile=false", "--screenEmulation.width=1280",
    "--screenEmulation.height=900", "--chrome-flags=--headless --ignore-certificate-errors --no-sandbox",
    `--extra-headers=${JSON.stringify({ Cookie: `${SESSION_COOKIE}=${SESSION_ID}` })}`],
  { timeout: 110_000, windowsHide: true });
  const report = JSON.parse(readFileSync(output, "utf8")) as {
    finalDisplayedUrl: string; categories: Record<string, { score: number | null }>;
  };
  expect(new URL(report.finalDisplayedUrl).pathname).toBe("/admin/anvandare");
  for (const category of Object.values(report.categories)) expect(category.score).toBeGreaterThan(0.9);
});
