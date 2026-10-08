import { expect, test, type Locator, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import { STEP_UP_CHALLENGE, STEP_UP_GRANT } from "./fixtures";
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

test.beforeEach(async ({ context, page }) => {
  harness.reset();
  await page.setViewportSize({ width: 1280, height: 900 });
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

// Feedback is built (#1979) and has a spec of its own, admin-feedback.spec.ts.
const UNBUILT = [
  "/admin",
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
const ACCOUNT_D = "00000000-0000-4000-8000-000000000004";
const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const lighthouseRequire = createRequire(cliRequire.resolve("lighthouse"));
const axePath = lighthouseRequire.resolve("axe-core/axe.min.js");

function screenshotDirectory() {
  const requested = process.env.ADMIN_ACCESS_SCREENSHOT_DIR;
  if (!requested) return null;
  if (!win32.isAbsolute(requested)) throw new Error("ADMIN_ACCESS_SCREENSHOT_DIR must be absolute.");
  const directory = win32.resolve(requested);
  const repository = win32.resolve(__dirname, "../../../..");
  if (!/^c:\\tmp\\/i.test(directory)
    || directory.toLowerCase() === repository.toLowerCase()
    || directory.toLowerCase().startsWith(`${repository.toLowerCase()}\\`)) {
    throw new Error("ADMIN_ACCESS_SCREENSHOT_DIR must be an external C:/tmp directory outside the repository.");
  }
  return directory;
}

async function captureAccessState(page: Page, state: string) {
  const directory = screenshotDirectory();
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  await page.screenshot({ path: join(directory, `${state}-${page.viewportSize()?.width ?? 1280}.png`),
    animations: "disabled", fullPage: true });
}

type AxeResult = { violations: { id: string; impact: string | null; nodes: { target: string[] }[] }[] };

async function expectAccessAxe(page: Page, state: string) {
  await page.addScriptTag({ path: axePath });
  const result = await page.evaluate(async () => {
    const engine = (window as unknown as {
      axe: { run: (target: Document, options: unknown) => Promise<AxeResult> };
    }).axe;
    return engine.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
  });
  expect(result.violations, `${state} has accessibility violations`).toEqual([]);
}

async function openAccount(page: Page, email = "konto.e@example.test") {
  await page.goto("/admin/anvandare");
  await page.getByRole("button", { name: email, exact: true }).click();
  const panel = page.getByRole("dialog", { name: email, exact: true });
  await expect(panel.getByRole("region", { name: "Åtgärder", exact: true })).toBeVisible();
  return panel;
}

const accessLabels = {
  suspend: { button: "Stäng av åtkomst", title: "Stäng av åtkomsten för" },
  reinstate: { button: "Återaktivera åtkomst", title: "Återaktivera åtkomsten för" },
} as const;

async function beginAccess(page: Page, panel: Locator, operation: keyof typeof accessLabels,
  email = "konto.e@example.test") {
  await panel.getByRole("button", { name: accessLabels[operation].button, exact: true }).click();
  const dialog = page.getByRole("dialog", { name: `${accessLabels[operation].title} ${email}?`, exact: true });
  await expect(dialog).toContainText("skickar vi en sexsiffrig kod till admin@example.test");
  return dialog;
}

async function enterAccessCode(dialog: Locator) {
  await dialog.getByRole("button", { name: "Skicka kod", exact: true }).click();
  await expect(dialog).toContainText("Vi har skickat en kod till admin@example.test.");
  await expect(dialog.getByLabel("Sexsiffrig kod")).toBeFocused();
  await dialog.getByLabel("Sexsiffrig kod").fill("123456");
}

async function submitAccessCode(page: Page, dialog: Locator, operation: keyof typeof accessLabels) {
  const response = page.waitForResponse(answer => answer.request().method() === "POST"
    && answer.request().headers()["next-action"] !== undefined).then(answer => answer.text());
  await dialog.getByRole("button", { name: accessLabels[operation].button, exact: true }).click();
  const flight = await response;
  expect(flight).not.toContain(STEP_UP_GRANT);
  expect(flight).not.toContain("123456");
}

test("suspend cancels the pending address change, refreshes counts/filter and reinstatement requires a fresh code", async ({ page }) => {
  harness.emailChanges.add(ACCOUNT_E);
  const panel = await openAccount(page);
  const suspend = await beginAccess(page, panel, "suspend");
  await expect(suspend).toContainText("Det väntande adressbytet avbryts.");
  await captureAccessState(page, "suspend-confirmation");
  await expectAccessAxe(page, "suspend confirmation");
  await enterAccessCode(suspend);
  await captureAccessState(page, "suspend-code");
  await submitAccessCode(page, suspend, "suspend");

  await expect(suspend).toBeHidden();
  await expect(panel.getByRole("heading", { name: "konto.e@example.test", exact: true })).toBeFocused();
  await expect(panel.getByRole("button", { name: "Återaktivera åtkomst", exact: true })).toBeVisible();
  await expect(panel.getByText("Adressbyte", { exact: true })).toHaveCount(0);
  await expect(page.locator(".jp-toast")).toContainText("Åtkomsten för konto.e@example.test är avstängd.");
  expect(harness.emailChanges.has(ACCOUNT_E)).toBe(false);
  await captureAccessState(page, "suspend-receipt");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("radio", { name: "Avstängda (1)", exact: true })).toBeVisible();
  await expect(page.getByRole("radio", { name: "Aktiva (2)", exact: true })).toBeVisible();
  await page.getByRole("radio", { name: "Avstängda (1)", exact: true }).click();
  await expect(accountRows(page)).toHaveCount(1);
  await expect(accountRows(page).first()).toContainText("Avstängd");
  expect(JSON.parse(harness.searches.at(-1) ?? "{}")).toMatchObject({ status: "Suspended" });
  await page.getByRole("button", { name: "konto.e@example.test", exact: true }).click();
  const reinstate = await beginAccess(page, panel, "reinstate");
  await expect(reinstate).toContainText("Tidigare sessioner återaktiveras inte.");
  await captureAccessState(page, "reinstate-confirmation");
  await enterAccessCode(reinstate);
  await submitAccessCode(page, reinstate, "reinstate");

  await expect(reinstate).toBeHidden();
  await expect(panel.getByRole("button", { name: "Stäng av åtkomst", exact: true })).toBeVisible();
  await expect(page.locator(".jp-toast").filter({ hasText: "är återaktiverad" }))
    .toContainText("Kontoägaren behöver logga in igen.");
  await captureAccessState(page, "reinstate-receipt");
  expect(harness.access.get(ACCOUNT_E)).toEqual({ isSuspended: false, accessRevision: 2 });
  expect(harness.emailChanges.has(ACCOUNT_E)).toBe(false);
  expect(harness.reauthVerifications.map(body => JSON.parse(body))).toEqual([
    { challengeId: STEP_UP_CHALLENGE, code: "123456" }, { challengeId: STEP_UP_CHALLENGE, code: "123456" },
  ]);
  expect(harness.accessRequests.map(({ operation, body }) => ({ operation, body: JSON.parse(body) }))).toEqual([
    { operation: "suspend", body: { reauthGrant: STEP_UP_GRANT } },
    { operation: "reinstate", body: { reauthGrant: STEP_UP_GRANT } },
  ]);
  await page.keyboard.press("Escape");
  await expect(accountRows(page)).toHaveCount(1); // The empty-region row, with the Suspended filter retained.
  await expect(page.getByRole("radio", { name: "Avstängda (0)", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Konton", exact: true })).toBeFocused();
});

test("reinstate under pending deletion leaves the deletion date and explicitly receipts its continuation", async ({ page }) => {
  harness.access.set(ACCOUNT_D, { isSuspended: true, accessRevision: 1 });
  const panel = await openAccount(page, "konto.d@example.test");
  await expect(panel.getByText("Avstängd", { exact: true })).toBeVisible();
  const dialog = await beginAccess(page, panel, "reinstate", "konto.d@example.test");
  await expect(dialog).toContainText("Raderingen fortsätter.");
  await enterAccessCode(dialog);
  await submitAccessCode(page, dialog, "reinstate");

  await expect(dialog).toBeHidden();
  await expect(panel).toContainText("2026-10-30");
  await expect(panel.getByText("Under radering", { exact: true })).toBeVisible();
  await expect(page.locator(".jp-toast")).toContainText("Raderingen fortsätter.");
  expect(harness.access.get(ACCOUNT_D)).toEqual({ isSuspended: false, accessRevision: 2 });
  await captureAccessState(page, "reinstate-pending-deletion-receipt");
  await expectAccessAxe(page, "pending-deletion receipt");
});

for (const width of [1280, 3440]) {
  test(`request loading, code and protected-write loading render at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.holdCodeRequests = true;
    harness.holdAccessWrites = true;
    try {
      const panel = await openAccount(page);
      const dialog = await beginAccess(page, panel, "suspend");
      await dialog.getByRole("button", { name: "Skicka kod", exact: true }).click();
      await expect.poll(() => harness.requests.includes("POST /api/v1/auth/reauth")).toBe(true);
      await expect(dialog.getByRole("button", { name: "Skickar…", exact: true })).toBeDisabled();
      await captureAccessState(page, "code-request-loading");
      await expectAccessAxe(page, "code request loading");
      harness.releaseCodeRequests();
      await expect(dialog.getByLabel("Sexsiffrig kod")).toBeFocused();
      await captureAccessState(page, "code-entry");
      await expectAccessAxe(page, "code entry");
      await dialog.getByLabel("Sexsiffrig kod").fill("123456");
      await dialog.getByRole("button", { name: "Stäng av åtkomst", exact: true }).click();
      await expect.poll(() => harness.accessRequests.length).toBe(1);
      await expect(dialog.getByRole("button", { name: "Stänger av…", exact: true })).toBeDisabled();
      await captureAccessState(page, "protected-write-loading");
      await expectAccessAxe(page, "protected write loading");
      await expect(page.locator(".jp-toast")).toHaveCount(0);
      harness.releaseAccessWrites();
      await expect(dialog).toBeHidden();
      await expect(page.locator(".jp-toast")).toContainText("är avstängd.");
      await captureAccessState(page, "known-commit-success");
    } finally {
      harness.releaseCodeRequests();
      harness.releaseAccessWrites();
    }
  });

  for (const committed of [false, true]) {
    test(`an unknown 503 outcome claims no command receipt even after status ${committed ? "changed" : "stayed active"}, ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      harness.accessMode = committed ? "unknownAfterCommit" : "unknown";
      const panel = await openAccount(page);
      const dialog = await beginAccess(page, panel, "suspend");
      await enterAccessCode(dialog);
      await submitAccessCode(page, dialog, "suspend");

      await expect(dialog).toBeHidden();
      const notice = panel.getByRole("region", { name: "Åtgärder" }).getByRole("status");
      await expect(notice).toContainText("Det går inte att bekräfta om åtgärden genomfördes.");
      await expect(notice).toBeFocused();
      await expect(page.locator(".jp-toast")).toHaveCount(0);
      await expect(panel.getByRole("button", { name: committed ? "Återaktivera åtkomst" : "Stäng av åtkomst", exact: true }))
        .toBeVisible();
      expect(harness.accessRequests).toHaveLength(1);
      await captureAccessState(page, `unknown-${committed ? "committed" : "uncommitted"}`);
      await expectAccessAxe(page, "unknown command outcome");
    });
  }

  test(`a real no-op command refusal is focused in Åtgärder without a receipt, ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const panel = await openAccount(page);
    const dialog = await beginAccess(page, panel, "suspend");
    await enterAccessCode(dialog);
    // A second administrator committed suspension after this panel's read. The real command now refuses.
    harness.access.set(ACCOUNT_E, { isSuspended: true, accessRevision: 1 });
    await submitAccessCode(page, dialog, "suspend");

    await expect(dialog).toBeHidden();
    const refusal = panel.getByRole("region", { name: "Åtgärder" }).getByRole("alert");
    await expect(refusal).toContainText("Kontots åtkomst är redan avstängd.");
    await expect(refusal).toBeFocused();
    await expect(page.locator(".jp-toast")).toHaveCount(0);
    expect(harness.access.get(ACCOUNT_E)?.accessRevision).toBe(1);
    expect(harness.accessRequests).toHaveLength(1);
    await captureAccessState(page, "command-no-op-refusal");
    await expectAccessAxe(page, "real no-op refusal");
  });

  test(`a 429 after code verification spends no transition and gives a focused refusal, ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.accessMode = "rateLimited";
    const panel = await openAccount(page);
    const dialog = await beginAccess(page, panel, "suspend");
    await enterAccessCode(dialog);
    await submitAccessCode(page, dialog, "suspend");

    await expect(dialog).toBeHidden();
    const refusal = panel.getByRole("region", { name: "Åtgärder" }).getByRole("alert");
    await expect(refusal).toContainText("För många förfrågningar.");
    await expect(refusal).toBeFocused();
    await expect(page.locator(".jp-toast")).toHaveCount(0);
    expect(harness.access.has(ACCOUNT_E)).toBe(false);
    await captureAccessState(page, "command-rate-limit");
    await expectAccessAxe(page, "command rate limit");
  });

  test(`an expired session after verification replaces the code form with a login route, ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    harness.accessMode = "unauthorized";
    const panel = await openAccount(page);
    const dialog = await beginAccess(page, panel, "suspend");
    await enterAccessCode(dialog);
    await submitAccessCode(page, dialog, "suspend");

    const outcome = dialog.getByRole("status");
    await expect(outcome).toContainText("Du är inte inloggad längre. Logga in igen och börja om.");
    await expect(outcome).toBeFocused();
    await expect(dialog.getByRole("link", { name: "Logga in", exact: true })).toHaveAttribute("href", /\/logga-in\?next=.*admin/);
    await expect(dialog.getByLabel("Sexsiffrig kod")).toHaveCount(0);
    await expect(page.locator(".jp-toast")).toHaveCount(0);
    await captureAccessState(page, "session-expired");
    await expectAccessAxe(page, "expired-session replacement");
  });
}

test("the real direct cancel refusal (#1994) is shown and focused in Åtgärder without a success toast", async ({ page }) => {
  harness.emailChanges.add(ACCOUNT_E);
  const panel = await openAccount(page);
  await expect(panel.getByRole("button", { name: "Avbryt adressbytet", exact: true })).toBeVisible();
  // Another operator cancelled the pending record after this panel's read.
  harness.emailChanges.delete(ACCOUNT_E);
  await panel.getByRole("button", { name: "Avbryt adressbytet", exact: true }).click();

  const refusal = panel.getByRole("region", { name: "Åtgärder" }).getByRole("status");
  await expect(refusal).toContainText("Det finns inget adressbyte att avbryta längre.");
  await expect(refusal).toBeFocused();
  await expect(page.locator(".jp-toast")).toHaveCount(0);
  expect(harness.requests).toContain(`DELETE /api/v1/admin/accounts/${ACCOUNT_E}/email-change`);
  await captureAccessState(page, "direct-cancel-refusal");
  await expectAccessAxe(page, "direct command refusal");
});

for (const width of [1280, 1920, 3440]) {
  test(`the access surface rests without overflow at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/admin/anvandare");
    await expect(page.getByRole("radio", { name: "Avstängda (0)", exact: true })).toBeVisible();
    await captureAccessState(page, "directory-resting");
    await page.getByRole("button", { name: "konto.e@example.test", exact: true }).click();
    const panel = page.getByRole("dialog", { name: "konto.e@example.test", exact: true });
    await expect(panel.getByRole("button", { name: "Stäng av åtkomst", exact: true })).toBeVisible();
    await panel.evaluate(async element => {
      await Promise.all(element.getAnimations().map(animation => animation.finished));
    });
    await captureAccessState(page, "active-panel-resting");
    const box = await panel.boundingBox();
    expect(box).not.toBeNull();
    expect(box?.x).toBeGreaterThanOrEqual(0);
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(width);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(false);
    await expectAccessAxe(page, `resting access panel at ${width}px`);
  });
}

test("keyboard reaches the access dialog, traps focus, retains the challenge on reopen and returns focus on cancel", async ({ page }) => {
  const panel = await openAccount(page);
  const trigger = panel.getByRole("button", { name: "Stäng av åtkomst", exact: true });
  await trigger.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: "Stäng av åtkomsten för konto.e@example.test?", exact: true });
  for (let presses = 0; presses < 5; presses++) {
    await page.keyboard.press("Tab");
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  const send = dialog.getByRole("button", { name: "Skicka kod", exact: true });
  await send.focus();
  await page.keyboard.press("Enter");
  await expect(dialog.getByLabel("Sexsiffrig kod")).toBeFocused();
  await expect(dialog.getByRole("button", { name: "Stäng av åtkomst", exact: true })).toBeEnabled();
  await page.keyboard.type("123456");
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(trigger).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(dialog.getByLabel("Sexsiffrig kod")).toBeFocused();
  expect(harness.requests.filter(route => route === "POST /api/v1/auth/reauth")).toHaveLength(1);
  await dialog.getByRole("button", { name: "Avbryt", exact: true }).focus();
  await page.keyboard.press("Enter");
  await expect(trigger).toBeFocused();
  expect(harness.accessRequests).toHaveLength(0);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "konto.e@example.test", exact: true })).toBeFocused();
});

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
  await expect(panel.getByRole("button", { name: "Stäng", exact: true })).toBeFocused();
  await expect(panel.getByRole("button", { name: /Kommer snart$/ }).first()).toBeVisible();
  expect(harness.requests).toContain(`GET /api/v1/admin/accounts/${ACCOUNT_E}`);

  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden();
  await expect(open).toBeFocused();
});

test("an address change goes through the administrator's own step-up, above the panel, then shows as pending until cancelled", async ({ page }) => {
  await page.goto("/admin/anvandare");
  await page.getByRole("button", { name: "konto.e@example.test" }).click();
  const panel = page.getByRole("dialog", { name: "konto.e@example.test" });
  await expect(panel.getByText("CV:n")).toBeVisible();
  expect(harness.requests).toContain(`GET /api/v1/admin/accounts/${ACCOUNT_E}/email-change`);

  await panel.getByRole("button", { name: "Ändra e-postadress" }).click();
  await panel.getByLabel("Ny e-postadress").fill("ny.adress@example.test");
  await panel.getByRole("button", { name: "Fortsätt" }).click();
  const stepUp = page.getByRole("dialog", { name: "Ändra e-postadress" });
  await expect(stepUp).toContainText("En kod skickas till ny.adress@example.test och ett meddelande till konto.e@example.test.");
  await expect(stepUp).toContainText("skickar vi en sexsiffrig kod till admin@example.test");

  // The step-up sits above the panel, by the stylesheet's layers and not by portal order: what is drawn at the
  // panel's head is the step-up's overlay, and at the step-up's middle the step-up itself. The open step-up turns
  // the panel's pointer events off, and elementFromPoint skips such an element, so they are on for the probe:
  // otherwise a panel drawn above the overlay would pass unseen.
  const paint = await page.evaluate(() => {
    const content = document.querySelector<HTMLElement>(".jp-adminstepup");
    const overlay = content?.previousElementSibling as HTMLElement | null;
    const panelElement = document.querySelector<HTMLElement>(".jp-adminpanel");
    const head = panelElement?.querySelector<HTMLElement>(".jp-adminpanel__head");
    if (!content || !overlay || !panelElement || !head) return null;
    const middle = (element: HTMLElement) => {
      const box = element.getBoundingClientRect();
      return document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
    };
    const before = panelElement.style.pointerEvents;
    panelElement.style.pointerEvents = "auto";
    const result = {
      overHead: middle(head) === overlay,
      overContent: content.contains(middle(content)),
      z: [getComputedStyle(overlay).zIndex, getComputedStyle(content).zIndex],
    };
    panelElement.style.pointerEvents = before;
    return result;
  });
  expect(paint).toEqual({ overHead: true, overContent: true, z: ["119", "120"] });

  await stepUp.getByRole("button", { name: "Skicka kod" }).click();
  await stepUp.getByLabel("Sexsiffrig kod").fill("123456");
  await stepUp.getByRole("button", { name: "Bekräfta koden" }).click();

  await expect(stepUp).toBeHidden();
  await expect(panel.getByRole("heading", { name: "konto.e@example.test" })).toBeFocused();
  await expect(panel.getByText("Adressbyte", { exact: true })).toBeVisible();
  await expect(panel).toContainText("Koden kan användas från 2026-10-08 14:00 till 2026-10-09 14:00.");
  await expect(page.locator(".jp-toast")).toContainText("En kod har skickats till ny.adress@example.test.");
  // The grant went from the Server Action to the backend, and never through the browser.
  expect(harness.emailChangeRequests.map((body) => JSON.parse(body))).toEqual([
    { newEmail: "ny.adress@example.test", reauthGrant: STEP_UP_GRANT },
  ]);

  await panel.getByRole("button", { name: "Avbryt adressbytet" }).click();
  await expect(panel.getByText("Adressbyte", { exact: true })).toHaveCount(0);
  await expect(panel.getByRole("button", { name: "Ändra e-postadress" })).toBeVisible();
  expect(harness.requests).toContain(`DELETE /api/v1/admin/accounts/${ACCOUNT_E}/email-change`);
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
  for (let i = 0; i < 12; i++) {
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
    "Adminmeny",
    "Aviseringar",
    "Inställningar",
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
