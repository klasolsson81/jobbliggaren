import { expect, test, type Page } from "@playwright/test";
import { startHarness, type Harness } from "../admin/servers";
import { PREVIEW_PORTS } from "./ports";

/**
 * The local admin preview in a real browser (ADR 0150 D5): it stays inside its own route and reads
 * no backend; the band's state choice drives the account list; the account panel traps focus, layers
 * Escape and returns focus to its row; a destructive action asks first and confirms with a receipt;
 * an action on the administrator's own account is refused where it was asked; an unbuilt action does
 * nothing; and no page scrolls sideways at phone width. Every account and address is fictional.
 */

const ROOT = "/admin/forhandsvisning";
const DOMAIN = "forhandsvisning.invalid";
const mail = (local: string) => `${local}@${DOMAIN}`;
const LONG = mail("konto.n.med.en.mycket.lang.adress.for.smala.skarmar");

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness(PREVIEW_PORTS);
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(() => {
  harness.reset();
});

test.afterEach(() => {
  expect(harness.requests, "the preview read the backend").toEqual([]);
});

const PAGES = [
  { label: "Översikt", path: ROOT, heading: "Översikt" },
  { label: "Användare", path: `${ROOT}/anvandare`, heading: "Användare" },
  { label: "Feedback", path: `${ROOT}/feedback`, heading: "Feedback" },
  { label: "Loggar", path: `${ROOT}/loggar`, heading: "Loggar" },
  { label: "E-postleverans", path: `${ROOT}/e-post`, heading: "E-postleverans" },
  { label: "Bakgrundsjobb", path: `${ROOT}/jobb`, heading: "Bakgrundsjobb" },
  { label: "Granskning", path: `${ROOT}/granskning`, heading: "Granskning" },
] as const;

const ALL_PATHS = [
  ...PAGES.map((p) => p.path),
  `${ROOT}/loggar/applikationsfel`,
  `${ROOT}/loggar/platsbanken-import`,
];

const adminNav = (page: Page) => page.getByRole("navigation", { name: "Admin-navigation" });
const panel = (page: Page) => page.getByRole("dialog");
const confirmation = (page: Page) => page.getByRole("alertdialog");
const toast = (page: Page) => page.locator(".jp-toast");

async function openAccount(page: Page, email: string) {
  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill(email);
  await page.getByRole("button", { name: email }).click();
  await expect(panel(page)).toBeVisible();
}

test("the preview's nav reaches every page inside the preview and marks the current one", async ({ page }) => {
  await page.goto(ROOT);
  await expect(adminNav(page).getByRole("link")).toHaveText(PAGES.map((p) => p.label));

  for (const target of PAGES) {
    await adminNav(page).getByRole("link", { name: target.label }).click();
    await expect(page).toHaveURL(target.path);
    await expect(page.getByRole("heading", { level: 1, name: target.heading })).toBeVisible();
    await expect(adminNav(page).locator('[aria-current="page"]')).toHaveText(target.label);
  }

  for (const path of ALL_PATHS) {
    await page.goto(path);
    const escaping = await page
      .locator('a[href^="/admin"]')
      .evaluateAll((links, root) => links.map((a) => a.getAttribute("href")).filter((href) => !href?.startsWith(root)), ROOT);
    expect(escaping, `${path} links out of the preview`).toEqual([]);
  }
});

test("every page says it is a fictional local preview and asks not to be indexed", async ({ page }) => {
  for (const path of ALL_PATHS) {
    await page.goto(path);
    await expect(page.getByText("Förhandsvisning med påhittade uppgifter. Körs bara lokalt.")).toBeVisible();
    await expect(page).toHaveTitle(/^Förhandsvisning: /);
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute("content", /noindex/);
  }
});

test("Bakgrundsjobb and Granskning render their tables over the fixtures", async ({ page }) => {
  await page.goto(`${ROOT}/jobb`);
  await expect(page.getByRole("cell", { name: "sync-platsbanken-snapshot" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "SyncPlatsbankenSnapshotWorker" })).toBeVisible();

  await page.goto(`${ROOT}/granskning`);
  await expect(page.getByRole("cell", { name: "SavedSearch.Created" })).toBeVisible();
});

test("the band's state choice drives the account list, and is offered only where it does", async ({ page }) => {
  await page.goto(ROOT);
  await expect(page.getByRole("radiogroup", { name: "Visa läge" })).toHaveCount(0);

  await page.goto(`${ROOT}/anvandare`);
  const state = page.getByRole("radiogroup", { name: "Visa läge" });
  const table = page.getByRole("table", { name: "Konton" });
  const search = page.getByRole("searchbox", { name: "Sök på e-postadress" });
  await expect(table.getByRole("button", { name: mail("konto.a") })).toBeVisible();

  await state.getByRole("radio", { name: "Kommer snart" }).click();
  await expect(table).toContainText("Kommer snart");
  await expect(search).toBeDisabled();
  await expect(table.getByRole("button", { name: /@/ })).toHaveCount(0);

  await state.getByRole("radio", { name: "Tom" }).click();
  await expect(table).toContainText("Inga konton matchar sökningen eller filtret.");

  await state.getByRole("radio", { name: "Fel" }).click();
  await expect(table.getByRole("alert")).toHaveText("Kontona kunde inte hämtas. Försök igen om en stund.");

  await state.getByRole("radio", { name: "Laddar" }).click();
  await expect(table).toHaveAttribute("aria-busy", "true");
  await expect(table.getByRole("status")).toHaveText("Hämtar konton");

  await state.getByRole("radio", { name: "Inkopplad" }).click();
  await expect(table.getByRole("button", { name: mail("konto.a") })).toBeVisible();
});

test("search, filter, sort and pages work on the fixtures, and the search never reaches the URL", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  const summary = page.getByRole("status").filter({ hasText: "konton" });
  await expect(summary).toHaveText("15 av 15 konton");
  await expect(page.getByRole("navigation", { name: "Sidnavigering" })).toContainText("Sida 1 av 2");

  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill("konto.a");
  await expect(summary).toHaveText("1 av 15 konton");
  expect(new URL(page.url()).search).toBe("");
  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill("");

  await page.getByRole("radio", { name: "Suspenderade (2)" }).click();
  await expect(summary).toHaveText("2 av 15 konton");
  await page.getByRole("radio", { name: "Alla (15)" }).click();

  await page.getByRole("button", { name: "Konto", exact: true }).click();
  await expect(page.getByRole("columnheader", { name: "Konto", exact: true })).toHaveAttribute("aria-sort", "ascending");
  const firstRow = page.getByRole("table", { name: "Konton" }).getByRole("row").nth(1);
  await expect(firstRow).toContainText(mail("admin"));

  await page.getByRole("button", { name: "Nästa" }).click();
  await expect(page.getByRole("navigation", { name: "Sidnavigering" })).toContainText("Sida 2 av 2");
  await expect(page.getByRole("button", { name: "Nästa" })).toBeDisabled();
});

test("the panel starts on Stäng, traps focus, layers Escape and returns focus to its row", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  const row = page.getByRole("button", { name: mail("konto.a") });
  await row.click();
  await expect(panel(page)).toHaveAccessibleName(mail("konto.a"));
  await expect(panel(page).getByRole("button", { name: "Stäng" })).toBeFocused();

  for (let step = 0; step < 12; step += 1) {
    await page.keyboard.press("Tab");
    expect(await panel(page).evaluate((dialog) => dialog.contains(document.activeElement))).toBe(true);
  }

  await panel(page).getByRole("button", { name: "Ändra e-postadress" }).click();
  await expect(panel(page).getByLabel("Ny e-postadress")).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(panel(page).getByLabel("Ny e-postadress")).toHaveCount(0);
  await expect(panel(page).getByRole("button", { name: "Ändra e-postadress" })).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(panel(page)).toHaveCount(0);
  await expect(row).toBeFocused();
});

test("suspending asks first, then confirms with a receipt, and the account can be reinstated", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.a"));

  await panel(page).getByRole("button", { name: "Suspendera konto" }).click();
  await expect(confirmation(page)).toHaveAccessibleName(`Suspendera ${mail("konto.a")}?`);
  await expect(confirmation(page).getByRole("button", { name: "Avbryt" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(confirmation(page)).toHaveCount(0);
  await expect(panel(page).getByRole("button", { name: "Suspendera konto" })).toBeFocused();

  await panel(page).getByRole("button", { name: "Suspendera konto" }).click();
  await confirmation(page).getByRole("button", { name: "Suspendera konto" }).click();

  await expect(confirmation(page)).toHaveCount(0);
  await expect(toast(page)).toContainText(`Kontot ${mail("konto.a")} är suspenderat.`);
  await expect(panel(page).getByRole("heading", { name: mail("konto.a") })).toBeFocused();
  await expect(panel(page)).toContainText("Suspenderad");

  await panel(page).getByRole("button", { name: "Häv suspendering" }).click();
  await expect(toast(page)).toContainText(`Suspenderingen av ${mail("konto.a")} är hävd.`);
  await expect(panel(page)).toContainText("Aktiv");
});

test("scheduling deletion states the earliest date and leaves nothing but unbuilt actions", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.b"));

  await panel(page).getByRole("button", { name: "Radera konto" }).click();
  await expect(confirmation(page)).toContainText("raderas slutgiltigt tidigast 3 nov. 2026");
  await confirmation(page).getByRole("button", { name: "Radera konto" }).click();

  await expect(toast(page)).toContainText(`Kontot ${mail("konto.b")} raderas slutgiltigt tidigast 3 nov. 2026.`);
  await expect(panel(page)).toContainText("Raderas tidigast 2026-11-03");
  for (const button of await panel(page).getByRole("region", { name: "Åtgärder" }).getByRole("button").all()) {
    await expect(button).toHaveAttribute("aria-disabled", "true");
  }
});

test("an action on the administrator's own account is refused where it was asked", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("admin"));

  await panel(page).getByRole("button", { name: "Suspendera konto" }).click();
  await confirmation(page).getByRole("button", { name: "Suspendera konto" }).click();
  await expect(confirmation(page).getByRole("alert")).toHaveText("Du kan inte suspendera ditt eget konto.");
  await expect(confirmation(page).getByRole("alert")).toBeFocused();
  await expect(toast(page)).toHaveCount(0);
  await confirmation(page).getByRole("button", { name: "Avbryt" }).click();

  await panel(page).getByRole("button", { name: "Ändra e-postadress" }).click();
  const field = panel(page).getByLabel("Ny e-postadress");
  await field.fill(mail("ny.adress"));
  await panel(page).getByRole("button", { name: "Skicka bekräftelse" }).click();
  await expect(panel(page).getByRole("alert")).toHaveText(
    "Du kan inte byta adress på ditt eget konto här. Byt den på Mina sidor.",
  );
  await expect(panel(page).getByRole("alert")).toBeFocused();
  await expect(field).toHaveValue(mail("ny.adress"));
  await expect(toast(page)).toHaveCount(0);
});

test("an address change is sent as a request and refused when empty or unchanged", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.a"));
  await panel(page).getByRole("button", { name: "Ändra e-postadress" }).click();
  const field = panel(page).getByLabel("Ny e-postadress");
  const submit = panel(page).getByRole("button", { name: "Skicka bekräftelse" });

  await submit.click();
  await expect(panel(page).getByRole("alert")).toHaveText("Skriv in den nya e-postadressen.");
  await expect(field).toBeFocused();
  await field.fill(mail("konto.a").toUpperCase());
  await submit.click();
  await expect(panel(page).getByRole("alert")).toContainText("samma som kontots nuvarande");

  await field.fill(mail("ny.adress"));
  await submit.click();
  await expect(toast(page)).toContainText(`En bekräftelse har skickats till ${mail("ny.adress")}.`);
  await expect(panel(page)).toHaveAccessibleName(mail("konto.a"));
});

test("an unbuilt action stays in place, says Kommer snart and does nothing", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.a"));

  const unbuilt = panel(page).getByRole("button", { name: "Agera som användaren Kommer snart" });
  await expect(unbuilt).toHaveAttribute("aria-disabled", "true");
  // Playwright treats aria-disabled as disabled and would wait; a user's press is forced through.
  await unbuilt.click({ force: true });
  await expect(confirmation(page)).toHaveCount(0);
  await expect(toast(page)).toHaveCount(0);
  await expect(panel(page)).toBeVisible();
});

test("the impersonation banner turns on from the band and off from its own button", async ({ page }) => {
  await page.goto(ROOT);
  const toggle = page.getByRole("button", { name: "Visa läget Agera som användaren" });
  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("region", { name: "Agera som användare" })).toHaveText(/Du agerar som konto\.a@forhandsvisning\.invalid\./);

  await page.getByRole("button", { name: "Sluta agera som användaren" }).click();
  await expect(page.getByRole("region", { name: "Agera som användare" })).toHaveCount(0);
  await expect(toggle).toHaveAttribute("aria-pressed", "false");
});

for (const width of [320, 375]) {
  test(`no preview page scrolls sideways at ${width} px, and the panel fits`, async ({ page }) => {
    // Without motion the panel is measured where it rests, not mid-slide.
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.setViewportSize({ width, height: 800 });
    for (const path of ALL_PATHS) {
      await page.goto(path);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      expect(overflow, `${path} at ${width} px`).toBeLessThanOrEqual(0);
    }

    for (const path of [`${ROOT}/anvandare`, `${ROOT}/feedback`, `${ROOT}/e-post`]) {
      await page.goto(path);
      const clipped = await page
        .locator(".jp-adminsegment .jp-segment")
        .evaluateAll((groups) => groups.filter((group) => group.scrollWidth > group.clientWidth).length);
      expect(clipped, `${path}: a group hides options at ${width} px`).toBe(0);
    }

    await page.goto(`${ROOT}/anvandare`);
    await openAccount(page, LONG);
    const box = await panel(page).boundingBox();
    expect(box?.x ?? -1).toBeGreaterThanOrEqual(0);
    expect((box?.x ?? 0) + (box?.width ?? Infinity)).toBeLessThanOrEqual(width);
    await panel(page).getByRole("button", { name: "Ändra e-postadress" }).click();
    const sideways = await panel(page).evaluate((dialog) => dialog.scrollWidth - dialog.clientWidth);
    expect(sideways, `the panel scrolls sideways at ${width} px`).toBeLessThanOrEqual(0);
  });
}

test("a receipt published from the open panel waits for the panel to close", async ({ page }) => {
  await page.clock.install();
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.a"));

  await panel(page).getByRole("button", { name: "Suspendera konto" }).click();
  await confirmation(page).getByRole("button", { name: "Suspendera konto" }).click();
  await page.clock.runFor(500);
  await expect(toast(page)).toContainText(`Kontot ${mail("konto.a")} är suspenderat.`);

  await page.clock.runFor(30_000);
  await expect(toast(page)).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(panel(page)).toHaveCount(0);
  await page.clock.runFor(8_100);
  await expect(toast(page)).toHaveCount(0);
});

test("a refused deletion of the own account takes focus, and Avbryt returns to its button", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("admin"));

  await panel(page).getByRole("button", { name: "Radera konto" }).click();
  await confirmation(page).getByRole("button", { name: "Radera konto" }).click();
  await expect(confirmation(page).getByRole("alert")).toHaveText(
    "Du kan inte radera ditt eget konto här. Radera det på Mina sidor.",
  );
  await expect(confirmation(page).getByRole("alert")).toBeFocused();
  await confirmation(page).getByRole("button", { name: "Avbryt" }).click();
  await expect(panel(page).getByRole("button", { name: "Radera konto" })).toBeFocused();
});
