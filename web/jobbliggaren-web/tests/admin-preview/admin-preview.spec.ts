import { expect, test, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { join, win32 } from "node:path";
import { startHarness, type Harness } from "../admin/servers";
import { PREVIEW_PORTS } from "./ports";

/**
 * The local admin preview in a real browser (ADR 0150 D5): it stays inside its own route and reads
 * no backend; the band's state choice drives the account list; the account panel traps focus, layers
 * Escape and returns focus to its row; a destructive action asks first and confirms with a receipt;
 * an action on the administrator's own account is refused where it was asked; an address change goes
 * through a step-up over fixtures to a pending change and its cancel (#1975); an unbuilt action does
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
/** The account panel, named by its account's address; the step-up it opens is another dialog. */
const panel = (page: Page) => page.getByRole("dialog", { name: /@forhandsvisning\.invalid$/ });
const stepUp = (page: Page) => page.getByRole("dialog", { name: "Ändra e-postadress" });
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
  await page.goto(`${ROOT}/jobb`);
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
  const summary = page.getByRole("status").filter({ hasText: /\d+ av \d+ konto/ });
  await expect(summary).toHaveText("15 av 15 konton");
  await expect(page.getByRole("navigation", { name: "Sidnavigering" })).toContainText("Sida 1 av 2");

  await page.getByRole("searchbox", { name: "Sök på e-postadress" }).fill("konto.a");
  await expect(summary).toHaveText("1 av 1 konto");
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
  await expect(panel(page).getByText("Raderas slutgiltigt")).toBeVisible();
  await expect(panel(page)).toContainText("Tidigast 2026-11-03");
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
});

test("the administrator's own account offers no address change, and points to Mina sidor", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("admin"));

  const actions = panel(page).getByRole("region", { name: "Åtgärder" });
  await expect(actions.getByRole("button", { name: /e-postadress/ })).toHaveCount(0);
  await expect(actions).toContainText("Adressen på ett administratörskonto byts inte här. Byt din på Mina sidor.");
});

test("an address change is refused when empty or unchanged, then goes through a step-up to a pending change and its cancel", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.a"));
  await panel(page).getByRole("button", { name: "Ändra e-postadress" }).click();
  const field = panel(page).getByLabel("Ny e-postadress");
  const proceed = panel(page).getByRole("button", { name: "Fortsätt" });

  await proceed.click();
  await expect(panel(page).getByRole("alert")).toHaveText("Skriv in den nya e-postadressen.");
  await expect(field).toBeFocused();
  await expect(stepUp(page)).toHaveCount(0);
  await field.fill(mail("konto.a").toUpperCase());
  await proceed.click();
  await expect(panel(page).getByRole("alert")).toContainText("samma som kontots nuvarande");
  await expect(stepUp(page)).toHaveCount(0);

  await field.fill(mail("ny.adress"));
  await proceed.click();
  await expect(stepUp(page)).toContainText(`En kod skickas till ${mail("ny.adress")} och ett meddelande till ${mail("konto.a")}.`);
  await expect(stepUp(page)).toContainText(`skickar vi en sexsiffrig kod till ${mail("admin")}`);
  await page.keyboard.press("Escape");
  await expect(stepUp(page)).toHaveCount(0);
  await expect(proceed).toBeFocused();

  await proceed.click();
  await stepUp(page).getByRole("button", { name: "Skicka kod" }).click();
  await stepUp(page).getByLabel("Sexsiffrig kod").fill("123456");
  await stepUp(page).getByRole("button", { name: "Bekräfta koden" }).click();

  await expect(stepUp(page)).toHaveCount(0);
  await expect(toast(page)).toContainText(`En kod har skickats till ${mail("ny.adress")}.`);
  await expect(panel(page).getByRole("heading", { name: mail("konto.a") })).toBeFocused();
  await expect(panel(page)).toContainText("Väntar på kontoägaren.");

  await panel(page).getByRole("button", { name: "Avbryt adressbytet" }).click();
  await expect(toast(page)).toContainText("Adressbytet är avbrutet.");
  await expect(panel(page).getByRole("button", { name: "Ändra e-postadress" })).toBeVisible();
});

test("a change waiting for its owner, and one whose code was entered wrongly too often, show as facts", async ({ page }) => {
  await page.goto(`${ROOT}/anvandare`);
  await openAccount(page, mail("konto.f"));
  await expect(panel(page)).toContainText(
    "Väntar på kontoägaren. Koden kan användas från 2026-10-06 12:00 till 2026-10-07 12:00.",
  );
  await expect(panel(page).getByRole("button", { name: "Avbryt adressbytet" })).toBeVisible();
  await page.keyboard.press("Escape");

  await openAccount(page, mail("konto.j"));
  await expect(panel(page)).toContainText(
    "Koden har skrivits fel för många gånger och kan inte användas. Bytet upphör 2026-10-05 04:00.",
  );
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

const state = (page: Page, label: string) =>
  page.getByRole("radiogroup", { name: "Visa läge" }).getByRole("radio", { name: label, exact: true }).click();

test("the overview shows its regions with fixtures, and fails, loads and goes unbuilt with the band", async ({ page }) => {
  await page.goto(ROOT);
  const card = (name: string) => page.getByRole("region", { name, exact: true });

  await expect(card("Användare totalt")).toContainText("15konton2 suspenderade · 1 under radering");
  await expect(card("Nya användare och inloggningar")).toContainText("de senaste 30 dygnen.");
  await expect(card("Kräver uppmärksamhet")).toHaveAttribute("data-state", "raised");
  await expect(card("Kräver uppmärksamhet").getByRole("link", { name: "1 bakgrundsjobb har misslyckats" })).toHaveAttribute(
    "href",
    `${ROOT}/jobb`,
  );

  await card("Nya användare och inloggningar").getByRole("radio", { name: "7 dygn" }).click();
  await expect(card("Nya användare och inloggningar")).toContainText("de senaste 7 dygnen.");

  await state(page, "Fel");
  await expect(page.locator("main").getByRole("alert")).toHaveCount(1);
  await state(page, "Laddar");
  await expect(page.locator("main").getByRole("status")).toHaveCount(1);
  await state(page, "Tom");
  await expect(card("Användare totalt")).toContainText("0konton");
  await expect(card("Kräver uppmärksamhet")).toHaveAttribute("data-state", "clear");
  await state(page, "Kommer snart");
  await expect(card("Användare totalt")).toContainText("–Uppgift saknasKommer snart");
});

/** A preview submission's id, as the fixtures number them. */
const submission = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

test("feedback opens a submission by its link, saves a status with a receipt, and asks before resending an unknown notice", async ({ page }) => {
  await page.goto(`${ROOT}/feedback`);
  const list = page.getByRole("region", { name: "Inskick", exact: true });
  const detail = page.getByRole("region", { name: "Valt inskick", exact: true });
  const filter = page.getByRole("navigation", { name: "Filtrera på status" });

  await expect(filter.getByRole("link", { name: "Ny (4)" })).toBeVisible();
  await expect(detail).toHaveCount(0);
  await list.getByRole("link", { name: /Hur länge sparas/ }).click();
  await expect(page).toHaveURL(`${ROOT}/feedback?id=${submission(503)}`);
  await expect(detail).toContainText(mail("konto.g"));
  await expect(detail).toBeFocused();
  await expect(list.getByRole("link", { name: /Hur länge sparas/ })).toHaveAttribute("aria-current", "true");

  await detail.getByRole("combobox", { name: "Status" }).selectOption({ label: "Pågår" });
  await detail.getByRole("button", { name: "Spara status" }).click();
  await expect(toast(page)).toContainText("Statusen är ändrad till Pågår.");
  await expect(filter.getByRole("link", { name: "Pågår (3)" })).toBeVisible();
  await expect(filter.getByRole("link", { name: "Ny (3)" })).toBeVisible();

  await page.goto(`${ROOT}/feedback?id=${submission(504)}`);
  await detail.getByRole("button", { name: "Skicka avisering igen" }).click();
  await expect(confirmation(page)).toContainText("Aviseringen kan redan ha kommit fram. Ett nytt utskick kan ge en dubblett.");
  await confirmation(page).getByRole("button", { name: "Skicka avisering igen" }).click();
  await expect(toast(page)).toContainText("Aviseringen är köad igen.");
  await expect(detail.getByRole("heading", { name: "Avisering", exact: true })).toBeFocused();

  await state(page, "Tom");
  await expect(list).toContainText("Inga inskick.");
  await expect(detail).toContainText("Inskicket finns inte.");
});

test("the three log views show their rows and every view's count", async ({ page }) => {
  await page.goto(`${ROOT}/loggar`);
  const subnav = page.getByRole("navigation", { name: "Loggvyer" });
  await expect(subnav.getByRole("link")).toHaveText(["Säkerhet (5)", "Applikationsfel (3)", "Platsbanken-import (4)"]);
  await expect(page.getByRole("table", { name: "Säkerhetshändelser" }).getByRole("row")).toHaveCount(6);

  await subnav.getByRole("link", { name: "Applikationsfel (3)" }).click();
  await expect(page.getByRole("table", { name: "Applikationsfel" })).toContainText("SyncPlatsbankenSnapshotWorker");
  await subnav.getByRole("link", { name: "Platsbanken-import (4)" }).click();
  await expect(page.getByRole("table", { name: "Platsbanken-importer" })).toContainText("Misslyckades");

  await state(page, "Fel");
  await expect(page.getByRole("table", { name: "Platsbanken-importer" }).getByRole("alert")).toBeVisible();
  await expect(subnav.getByRole("link")).toHaveText(["Säkerhet", "Applikationsfel", "Platsbanken-import"]);
});

test("Leverans reads its period from the URL, as the live page does, and shows each scenario (#1981 PR 2)", async ({ page }) => {
  await page.goto(`${ROOT}/e-post`);
  const leverans = page.getByRole("region", { name: "Leverans", exact: true });
  const rows = page.getByRole("table", { name: /^Misslyckade utskick/ }).locator("tbody tr");
  await expect(leverans.getByRole("definition")).toHaveText(["384", "5"]);
  await expect(leverans.getByRole("link", { name: "7 dygn", exact: true })).toHaveAttribute("aria-current", "true");

  await leverans.getByRole("link", { name: "24 tim", exact: true }).click();
  await expect(page).toHaveURL(/\/e-post\?period=h24$/);
  await expect(leverans.getByRole("definition")).toHaveText(["57", "2"]);
  await expect(rows).toHaveCount(2);

  const scenario = page.getByLabel("Visa leverans");
  await scenario.selectOption({ label: "Fler misslyckade än listan visar" });
  await expect(rows).toHaveCount(10);
  await expect(leverans).toContainText("Visar de 10 senaste.");
  await scenario.selectOption({ label: "Läsnyckeln ser inga utskick" });
  await expect(leverans).toContainText("Scaleways leveransstatistik visar inga utskick de senaste 32 dygnen");
  await expect(rows).toHaveText(["Uppgift saknas"]);
  // Our own count is its own region and its own choice: the provider's state leaves it as it was.
  await expect(page.getByRole("region", { name: "Utskick per mejltyp", exact: true }).getByRole("rowheader")).toHaveCount(7);
  await expect(page.locator("main")).not.toContainText("Kommer snart");

  await state(page, "Fel");
  await expect(leverans.getByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.", { exact: true })).toHaveCount(2);
  await expect(rows).toHaveText(["Uppgift saknas"]);
});

test("the count per mail kind shows each fixture state for the URL's period, and follows the band only when it fails or loads (#1981 PR 3)", async ({ page }) => {
  await page.goto(`${ROOT}/e-post?period=h24`);
  const kinds = page.getByRole("region", { name: "Utskick per mejltyp", exact: true });
  const table = page.getByRole("table", { name: /^(Antal u|U)tskick per mejltyp/ });
  const rows = table.locator("tbody tr");
  const scenario = page.getByLabel("Visa utskick per mejltyp");
  await expect(table.getByRole("rowheader")).toHaveText([
    "Inloggning och koder", "Adressbyte begärt", "Adressbyte genomfört", "Matchningar", "Bevakade företag", "Ny feedback",
    "Svar på feedback",
  ]);
  await expect(kinds).toContainText("från och med 2026-10-03.");
  await expect(rows.first()).toContainText("2026-10-04: avvisat av Scaleway och inget svar i tid");
  await expect(rows.nth(3)).toContainText("2026-10-04: Scaleway nåddes inte och fel hos Scaleway");

  await expect(kinds.getByRole("link")).toHaveCount(0);
  await page.getByRole("link", { name: "7 dygn", exact: true }).click();
  await expect(page).toHaveURL(/\/e-post\?period=d7$/);
  await expect(kinds).toContainText("från och med 2026-09-27.");

  await scenario.selectOption({ label: "Inga fel" });
  await expect(rows.locator("td:last-child")).toHaveText(Array(7).fill("Inget"));
  await scenario.selectOption({ label: "Räkningen började inom perioden" });
  await expect(kinds).toContainText("sedan räkningen började 2026-10-03 15:12.");
  await scenario.selectOption({ label: "Räknas inte i miljön" });
  await expect(rows).toHaveText(["Räknas inte i den här miljön."]);
  await scenario.selectOption({ label: "Räkningen visar inga utskick" });
  await expect(rows).toHaveText([
    "Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat. Se runbooken för admin-e-post.",
  ]);

  await state(page, "Fel");
  await expect(rows).toHaveText(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
});

/** With ADMIN_PREVIEW_SCREENSHOT_DIR set to an absolute C:/tmp directory, the renders below are written into it. */
function previewScreenshotDirectory(): string | null {
  const requested = process.env.ADMIN_PREVIEW_SCREENSHOT_DIR;
  if (!requested) return null;
  const directory = win32.resolve(requested);
  if (!win32.isAbsolute(requested) || !/^c:\\tmp\\/i.test(directory)) {
    throw new Error("ADMIN_PREVIEW_SCREENSHOT_DIR must be an absolute C:/tmp directory");
  }
  return directory;
}

test("renders every state of the count per mail kind at 1280 and 375 px in both themes (#1981 PR 3)", async ({ page }) => {
  const directory = previewScreenshotDirectory();
  test.skip(directory === null, "renders only with ADMIN_PREVIEW_SCREENSHOT_DIR");
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  const kinds = page.getByRole("region", { name: "Utskick per mejltyp", exact: true });
  const shoot = async (name: string, width: number) => {
    for (const theme of ["light", "dark"] as const) {
      await page.evaluate(async (dark) => {
        if (dark) document.documentElement.dataset.theme = "dark";
        else delete document.documentElement.dataset.theme;
        await new Promise((resolve) => requestAnimationFrame(resolve));
        await Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => undefined)));
      }, theme === "dark");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `${name} scrolls sideways`).toBe(true);
      await kinds.screenshot({ path: join(directory, `preview-kinds-${name}-${theme}-${width}.png`), animations: "disabled" });
    }
  };
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 900 });
    await page.goto(`${ROOT}/e-post?period=d7`);
    const scenario = page.getByLabel("Visa utskick per mejltyp");
    for (const [name, label] of [
      ["observed", "Fel finns"], ["none-failed", "Inga fel"], ["nothing-sent", "Inga utskick"],
      ["partial", "Räkningen började inom perioden"], ["not-configured", "Räknas inte i miljön"],
      ["not-observable", "Räkningen visar inga utskick"],
    ] as const) {
      await scenario.selectOption({ label });
      await expect(kinds.getByRole("table")).toBeVisible();
      await shoot(name, width);
    }
    for (const [name, band, line] of [
      ["failed", "Fel", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
      ["loading", "Laddar", "Hämtar uppgifter…"],
    ] as const) {
      await state(page, band);
      await expect(kinds.locator("tbody tr")).toHaveText([line]);
      await shoot(name, width);
    }
    await state(page, "Inkopplad");
  }
});

/** The DMARC scenarios' labels (#1981 PR 4b), each with the first line it shows or none, as the live page says it. */
const DMARC_PREVIEW: ReadonlyArray<readonly [string, string, string | null]> = [
  ["observed", "Rapporter finns", null],
  ["capped", "Fler källor än tabellen visar", null],
  ["shared-address", "Samma adress, två domäner", null],
  ["none-failing", "Inga DMARC-underkända", null],
  ["mailbox", "Mejl som inte kunde läsas", "2 mejl till brevlådan de senaste 7 dygnen kunde inte läsas som rapport."],
  ["conflicting", "Rapport i flera versioner", "2 rapporter finns i flera versioner med olika innehåll"],
  ["observed-stale", "Inte läst på länge", "Brevlådan har inte lästs sedan 2026-10-04 05:23."],
  ["failed-after", "Senaste inläsningen misslyckades", "Brevlådans svar kunde inte läsas."],
  ["sign-in-unanswered", "Inloggningen besvarades inte", "Lösenordet skickades, men brevlådan gav inget svar. Nästa inloggningsförsök görs tidigast 2026-10-05 09:23."],
  ["no-reports", "Ingen rapport under perioden", "Ingen rapport täcker de senaste 7 dygnen."],
  ["no-reports-conflicting", "Ingen rapport som räknas", "En rapport finns i flera versioner"],
  ["no-reports-stale", "Ingen rapport, inte läst på länge", "Brevlådan har inte lästs sedan 2026-10-04 05:23."],
  ["not-observable", "Brevlådan inte läst än", "Brevlådan har inte lästs än."],
  ["not-configured", "Läses inte i miljön", "Läses inte i den här miljön."],
  ["misconfigured", "Lösenordet är tomt", "Lösenordet till brevlådan är tomt."],
  ["failed-unreachable", "Brevlådan nås inte", "Brevlådan gick inte att nå."],
  ["failed-timeout", "Brevlådan svarar inte i tid", "Brevlådan svarade inte i tid."],
  ["failed-tls", "Certifikatet kan inte verifieras", "Brevlådans certifikat kunde inte verifieras."],
  ["failed-auth-refused", "Inloggningen nekas", "Brevlådan nekade inloggningen. Nästa inloggningsförsök görs tidigast 2026-10-05 09:23."],
  ["failed-auth-unconfirmed", "Inloggningen besvaras inte", "Lösenordet skickades, men brevlådan gav inget svar."],
  ["failed-protocol", "Oläsbart svar från brevlådan", "Brevlådans svar kunde inte läsas."],
  ["failed-incomplete", "Första inläsningen har inte avslutats", "Den första inläsningen av brevlådan började 2026-10-04 09:23 och har inte avslutats."],
];

test("the DMARC reports show each fixture state, keep their own window, and follow the band only when it fails or loads (#1981 PR 4b)", async ({ page }) => {
  await page.goto(`${ROOT}/e-post?period=d7`);
  const dmarc = page.getByRole("region", { name: "DMARC-rapporter", exact: true });
  const scenario = page.getByLabel("Visa DMARC-rapporter");
  await expect(dmarc.getByRole("definition")).toHaveText(["51", "3"]);
  await expect(dmarc.getByRole("table").getByRole("rowheader")).toHaveCount(3);

  for (const [, label, line] of DMARC_PREVIEW) {
    await scenario.selectOption({ label });
    const lines = dmarc.locator(":scope > div > p.jp-adminsoon");
    if (line === null) await expect(lines).toHaveCount(0);
    else await expect(lines.first()).toContainText(line);
    await expect(dmarc).not.toContainText("Kommer snart");
  }
  await scenario.selectOption({ label: "Fler källor än tabellen visar" });
  await expect(dmarc.locator("tbody tr")).toHaveCount(25);
  await expect(dmarc.getByText("Visar 25 av 31 rader.", { exact: true })).toBeVisible();

  // Leverans's period never reaches the DMARC reports.
  const before = await dmarc.innerText();
  await page.getByRole("link", { name: "24 tim", exact: true }).click();
  await expect(page).toHaveURL(/\?period=h24$/);
  expect(await dmarc.innerText()).toBe(before);

  await state(page, "Kommer snart");
  await expect(dmarc.locator("tbody tr")).toHaveCount(25);
  await state(page, "Fel");
  await expect(dmarc.locator(":scope > div > p.jp-adminsoon")).toHaveText(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
  await state(page, "Laddar");
  await expect(dmarc.locator("tbody tr")).toHaveText(["Hämtar uppgifter…"]);
});

test("renders every DMARC state at 1280 and 375 px in both themes (#1981 PR 4b)", async ({ page }) => {
  const directory = previewScreenshotDirectory();
  test.skip(directory === null, "renders only with ADMIN_PREVIEW_SCREENSHOT_DIR");
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  const dmarc = page.getByRole("region", { name: "DMARC-rapporter", exact: true });
  const shoot = async (name: string, width: number) => {
    for (const theme of ["light", "dark"] as const) {
      await page.evaluate(async (dark) => {
        if (dark) document.documentElement.dataset.theme = "dark";
        else delete document.documentElement.dataset.theme;
        await new Promise((resolve) => requestAnimationFrame(resolve));
        await Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => undefined)));
      }, theme === "dark");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `${name} scrolls sideways`).toBe(true);
      await dmarc.screenshot({ path: join(directory, `preview-dmarc-${name}-${theme}-${width}.png`), animations: "disabled" });
    }
  };
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 900 });
    await page.goto(`${ROOT}/e-post?period=d7`);
    const scenario = page.getByLabel("Visa DMARC-rapporter");
    for (const [name, label] of DMARC_PREVIEW) {
      await scenario.selectOption({ label });
      await expect(dmarc.getByRole("table")).toBeVisible();
      await shoot(name, width);
    }
    for (const [name, band] of [["failed", "Fel"], ["loading", "Laddar"]] as const) {
      await state(page, band);
      await shoot(name, width);
    }
    await state(page, "Inkopplad");
  }
});

test("the month against the free allowance shows each fixture state, and follows the band only when it fails or loads", async ({ page }) => {
  await page.goto(`${ROOT}/e-post`);
  const allowance = page.getByRole("region", { name: /^Utskick (i |denna)/ });
  const scenario = page.getByLabel("Visa utskick mot gratiskvoten");
  await expect(allowance.getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);

  await scenario.selectOption({ label: "Över gratiskvoten" });
  await expect(allowance.getByRole("definition")).toHaveText([/^1\s301$/, "300", /^3,49\skr$/]);
  await scenario.selectOption({ label: "Annat abonnemang" });
  await expect(allowance).toContainText("Scaleway anger abonnemanget Scale, men villkoren gäller Essential.");
  await scenario.selectOption({ label: "Räknas inte i miljön" });
  await expect(allowance).toContainText("Räknas inte i den här miljön.");
  await expect(allowance).not.toContainText("Kommer snart");

  await state(page, "Kommer snart");
  await expect(allowance).toContainText("Räknas inte i den här miljön.");
  await state(page, "Fel");
  await expect(allowance).toContainText("Uppgifterna kunde inte hämtas.");
});
