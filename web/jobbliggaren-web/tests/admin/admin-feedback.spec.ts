import { expect, test, type Locator, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { join, win32 } from "node:path";
import { FEEDBACK_IDS } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * `/admin/feedback` in a real browser against the production build (#1979, ADR 0150 D2): every region's
 * state rendered from a real response of the fixture backend, the notice mail's link, a status change and
 * both requeues with their receipts, and each state at 1280 and 375 px with no sideways page scroll.
 * Set ADMIN_FEEDBACK_SCREENSHOT_DIR to an external C:/tmp directory to keep a screenshot of each state.
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
  expect(harness.misses, "the page read a backend route the harness does not answer").toEqual([]);
});

const PAGE = "/admin/feedback";
const ids = FEEDBACK_IDS;

const list = (page: Page) => page.getByRole("region", { name: "Inskick", exact: true });
const detailRegion = (page: Page) => page.getByRole("region", { name: "Valt inskick", exact: true });
const summary = (page: Page) => page.getByRole("region", { name: "Betyg per sida", exact: true });
const statusNav = (page: Page) => page.getByRole("navigation", { name: "Filtrera på status" });
const windowNav = (page: Page) => page.getByRole("navigation", { name: "Period" });
const toast = (page: Page) => page.locator(".jp-toast");
/** The value a `<dt>` names in the open submission's facts. */
const fact = (scope: Locator, term: string) =>
  scope.locator("dt", { hasText: new RegExp(`^${term}$`) }).locator("xpath=following-sibling::dd[1]");
const commands = () =>
  harness.feedbackCommands.map(({ id, command, body }) => ({ id, command, body: JSON.parse(body) as unknown }));

function screenshotDirectory() {
  const requested = process.env.ADMIN_FEEDBACK_SCREENSHOT_DIR;
  if (!requested) return null;
  if (!win32.isAbsolute(requested)) throw new Error("ADMIN_FEEDBACK_SCREENSHOT_DIR must be absolute.");
  const directory = win32.resolve(requested);
  const repository = win32.resolve(__dirname, "../../../..");
  if (!/^c:\\tmp\\/i.test(directory)
    || directory.toLowerCase() === repository.toLowerCase()
    || directory.toLowerCase().startsWith(`${repository.toLowerCase()}\\`)) {
    throw new Error("ADMIN_FEEDBACK_SCREENSHOT_DIR must be an external C:/tmp directory outside the repository.");
  }
  return directory;
}

async function capture(page: Page, state: string) {
  const directory = screenshotDirectory();
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  await page.screenshot({ path: join(directory, `${state}-${page.viewportSize()?.width ?? 1280}.png`),
    animations: "disabled", fullPage: true });
}

/** The state as it stands, at 1280 and at 375 px, where the page must not scroll sideways. */
async function atBothWidths(page: Page, state: string) {
  await capture(page, state);
  await page.setViewportSize({ width: 375, height: 900 });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${state} scrolls sideways at 375px`).toBeLessThanOrEqual(0);
  await capture(page, state);
  await page.setViewportSize({ width: 1280, height: 900 });
}

test("lists every submission newest first with its counts, and filters by a status", async ({ page }) => {
  await page.goto(PAGE);

  await expect(statusNav(page).getByRole("link")).toHaveText([
    "Alla (6)",
    "Ny (2)",
    "Pågår (2)",
    "Åtgärdad (1)",
    "Avstår (1)",
  ]);
  const items = list(page).getByRole("link");
  await expect(items).toHaveCount(6);
  await expect(items.first()).toContainText("Ansökningar2 av 5");
  await expect(items.first()).toContainText("Avisering: Köad");
  await expect(items.nth(1)).toContainText("CV-granskningInget betyg");
  // A submission with a rating and no text has no excerpt.
  await expect(items.nth(2)).toContainText("Jobbannons5 av 5");
  await expect(items.nth(2)).toContainText("Avisering: Utfall okänt");
  await expect(items.nth(2).locator(".jp-adminfeedback__excerpt")).toHaveCount(0);
  await expect(list(page).locator(".jp-adminfeedback__notice")).toHaveText([
    "Avisering: Köad",
    "Avisering: Misslyckades",
    "Avisering: Utfall okänt",
    "Avisering: Mottagen av e-posttjänsten",
    "Avisering: Skickas",
    "Avisering: Mottagen av e-posttjänsten",
  ]);
  // The list carries no address: an address is read one submission at a time.
  await expect(list(page)).not.toContainText("@example.test");
  await expect(summary(page).getByRole("table")).toBeVisible();
  await expect(page.locator(".jp-adminfeedback__availability")).toHaveCount(0);
  expect(harness.feedbackQueries).toContain("/api/v1/admin/feedback?pageNumber=1&pageSize=25");
  expect(harness.feedbackQueries).toContain("/api/v1/admin/feedback/summary?days=30");
  await atBothWidths(page, "list");

  await statusNav(page).getByRole("link", { name: "Ny (2)" }).click();
  await expect(page).toHaveURL(`${PAGE}?status=ny`);
  await expect(list(page).getByRole("link")).toHaveCount(2);
  await expect(statusNav(page).getByRole("link", { name: "Ny (2)" })).toHaveAttribute("aria-current", "true");
  expect(harness.feedbackQueries).toContain("/api/v1/admin/feedback?status=New&pageNumber=1&pageSize=25");
});

test("the notice mail's link opens its submission and leaves focus where the page starts; one opened from the list takes focus", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.failed}`);

  const detail = detailRegion(page);
  await expect(detail).toContainText("Hur länge sparas mitt uppladdade CV om jag inte loggar in på ett tag?");
  await expect(fact(detail, "Avsändare")).toHaveText("konto.g@example.test");
  await expect(fact(detail, "Betyg")).toHaveText("Inget betyg");
  await expect(fact(detail, "Läge")).toHaveText("Misslyckades");
  await expect(fact(detail, "Försök")).toHaveText("5");
  await expect(fact(detail, "Fönster")).toHaveText("1280 × 720");
  await expect(list(page).getByRole("link", { name: /CV-granskning/ })).toHaveAttribute("aria-current", "true");
  expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true);
  expect(harness.requests).toContain(`GET /api/v1/admin/feedback/${ids.failed}`);
  await atBothWidths(page, "mail-link");

  await list(page).getByRole("link", { name: /Sparade annonser/ }).click();
  await expect(page).toHaveURL(`${PAGE}?id=${ids.accepted}`);
  await expect(detail).toContainText("Det vore bra att kunna sortera sparade annonser på sista ansökningsdag.");
  await expect(detail).toBeFocused();
});

test("a submission whose reporter and browser are unknown shows en-dashes, never 0", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.unreported}`);

  const detail = detailRegion(page);
  await expect(fact(detail, "Avsändare")).toHaveText("–Uppgift saknas");
  for (const term of ["Fönster", "Skärm", "Pixeltäthet", "Tema", "Enhet", "Operativsystem", "Webbläsare", "Appversion"]) {
    await expect(fact(detail, term), term).toHaveText("–Uppgift saknas");
  }
  await expect(detail.locator(".jp-adminfeedback__text")).toHaveCSS("white-space", "pre-line");
  await atBothWidths(page, "unknown-values");
});

test("a status change goes through its Server Action, confirms with a receipt and moves the counts", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.queued}`);

  const detail = detailRegion(page);
  await detail.getByRole("combobox", { name: "Status" }).selectOption({ label: "Pågår" });
  const save = detail.getByRole("button", { name: "Spara status" });
  await save.click();

  await expect(toast(page)).toContainText("Statusen är ändrad till Pågår.");
  await expect(detail.locator(".jp-pill").first()).toHaveText("Pågår");
  await expect(statusNav(page).getByRole("link", { name: "Pågår (3)" })).toBeVisible();
  await expect(statusNav(page).getByRole("link", { name: "Ny (1)" })).toBeVisible();
  await expect(fact(detail, "Status ändrad")).toHaveText("2026-10-07 10:00");
  await expect(save).toBeFocused();
  expect(commands()).toEqual([{ id: ids.queued, command: "status", body: { status: "InProgress" } }]);
  await atBothWidths(page, "status-receipt");
});

test("a failed notice is sent again at a press, with no question, and lands on the notice", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.failed}`);

  const detail = detailRegion(page);
  await detail.getByRole("button", { name: "Försök igen" }).click();

  await expect(toast(page)).toContainText("Aviseringen är köad igen.");
  await expect(page.getByRole("alertdialog")).toHaveCount(0);
  await expect(fact(detail, "Läge")).toHaveText("Köad");
  await expect(fact(detail, "Försök")).toHaveText("0");
  await expect(fact(detail, "Nästa försök")).toHaveText("2026-10-07 10:00");
  await expect(detail.getByRole("heading", { name: "Avisering", exact: true })).toBeFocused();
  await expect(detail.getByRole("button", { name: "Försök igen" })).toHaveCount(0);
  expect(commands()).toEqual([{ id: ids.failed, command: "requeue", body: { acknowledgeDuplicateRisk: false } }]);
  await atBothWidths(page, "requeue-failed-receipt");
});

test("a notice whose outcome is unknown is sent again only after the duplicate warning", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.unknown}`);

  const detail = detailRegion(page);
  await expect(fact(detail, "Läge")).toHaveText("Utfall okänt");
  const resend = detail.getByRole("button", { name: "Skicka avisering igen" });
  await resend.click();

  const question = page.getByRole("alertdialog", { name: "Skicka aviseringen igen?" });
  await expect(question).toContainText("Aviseringen kan redan ha kommit fram. Ett nytt utskick kan ge en dubblett.");
  await expect(question.getByRole("button", { name: "Avbryt" })).toBeFocused();
  await atBothWidths(page, "requeue-unknown-question");
  await question.getByRole("button", { name: "Avbryt" }).click();
  await expect(question).toBeHidden();
  await expect(resend).toBeFocused();
  expect(harness.feedbackCommands).toEqual([]);

  await resend.click();
  await question.getByRole("button", { name: "Skicka avisering igen" }).click();
  await expect(question).toBeHidden();
  await expect(toast(page)).toContainText("Aviseringen är köad igen.");
  await expect(fact(detail, "Läge")).toHaveText("Köad");
  await expect(detail.getByRole("heading", { name: "Avisering", exact: true })).toBeFocused();
  expect(commands()).toEqual([{ id: ids.unknown, command: "requeue", body: { acknowledgeDuplicateRisk: true } }]);
  await atBothWidths(page, "requeue-unknown-receipt");
});

test("a failed notice whose outcome turned unknown after the page read it is refused, and the page shows it as it is", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.failed}`);
  const detail = detailRegion(page);
  await expect(detail.getByRole("button", { name: "Försök igen" })).toBeVisible();

  // After this page read it: another administrator requeued it, the dispatch job claimed it, and the
  // provider's answer was lost, so it is Unknown (FeedbackNotification.Requeue, Claim, RecordUnknown).
  const record = harness.feedback.get(ids.failed);
  if (record === undefined || record.notification === null) throw new Error("the fixture has no failed notice");
  harness.feedback.set(ids.failed, { ...record, notification: { ...record.notification, state: "Unknown", attempts: 1 } });
  await detail.getByRole("button", { name: "Försök igen" }).click();

  const refusal = detail.getByRole("alert");
  await expect(refusal).toHaveText("Aviseringens läge har ändrats. Kontrollera det innan du skickar den igen.");
  await expect(refusal).toBeFocused();
  await expect(fact(detail, "Läge")).toHaveText("Utfall okänt");
  await expect(detail.getByRole("button", { name: "Skicka avisering igen" })).toBeVisible();
  await expect(toast(page)).toHaveCount(0);
  expect(commands()).toEqual([{ id: ids.failed, command: "requeue", body: { acknowledgeDuplicateRisk: false } }]);
  await atBothWidths(page, "requeue-refused");
});

test("an empty list says so with every count zero, and its summary has no submissions", async ({ page }) => {
  harness.feedback.clear();
  await page.goto(PAGE);

  await expect(list(page)).toContainText("Inga inskick.");
  await expect(statusNav(page).getByRole("link")).toHaveText([
    "Alla (0)",
    "Ny (0)",
    "Pågår (0)",
    "Åtgärdad (0)",
    "Avstår (0)",
  ]);
  await expect(summary(page)).toContainText("Inga inskick under perioden.");
  await atBothWidths(page, "list-empty");
});

test("a list read that fails says so in place of the list, with no count, while the rest of the page stands", async ({ page }) => {
  harness.feedbackReads.list = "error";
  await page.goto(`${PAGE}?id=${ids.accepted}`);

  await expect(list(page).getByRole("alert")).toHaveText("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
  await expect(statusNav(page).getByRole("link")).toHaveText(["Alla", "Ny", "Pågår", "Åtgärdad", "Avstår"]);
  await expect(detailRegion(page)).toContainText("Det vore bra att kunna sortera");
  await expect(summary(page).getByRole("table")).toBeVisible();
  await atBothWidths(page, "list-failed");
});

test("reads refused for want of the Admin role say so, not that trying again later helps", async ({ page }) => {
  harness.mode = "forbidden";
  await page.goto(`${PAGE}?id=${ids.accepted}`);

  await expect(list(page).getByRole("alert")).toHaveText("Din session saknar Admin-rollen.");
  await expect(detailRegion(page).getByRole("alert")).toHaveText("Din session saknar Admin-rollen.");
  await expect(summary(page).getByRole("alert")).toHaveText("Din session saknar Admin-rollen.");
  await expect(page.getByText("Det går inte att se om feedback är öppen.")).toBeVisible();
  await expect(statusNav(page).getByRole("link")).toHaveText(["Alla", "Ny", "Pågår", "Åtgärdad", "Avstår"]);
  await atBothWidths(page, "list-refused");
});

test("a summary with no submissions in its window is not a failed summary", async ({ page }) => {
  await page.goto(`${PAGE}?fonster=7`);

  const region = summary(page);
  await expect(region).toContainText("Inga inskick under perioden.");
  await expect(region.getByRole("alert")).toHaveCount(0);
  await expect(region.getByRole("table")).toHaveCount(0);
  await expect(windowNav(page).getByRole("link", { name: "7 dygn" })).toHaveAttribute("aria-current", "true");
  expect(harness.feedbackQueries).toContain("/api/v1/admin/feedback/summary?days=7");
  await atBothWidths(page, "summary-empty");

  harness.feedbackReads.summary = "error";
  await page.goto(`${PAGE}?fonster=7`);
  await expect(region.getByRole("alert")).toHaveText("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
  await expect(region).not.toContainText("Inga inskick under perioden.");
  await expect(list(page).getByRole("link")).toHaveCount(6);
  await atBothWidths(page, "summary-failed");
});

test("whether feedback is open: no line while open, the reason while closed, and an unknown while unreadable", async ({ page }) => {
  await page.goto(PAGE);
  await expect(page.locator(".jp-adminfeedback__availability")).toHaveCount(0);
  await atBothWidths(page, "availability-open");

  harness.feedbackAvailability = "NoRecipient";
  await page.goto(PAGE);
  await expect(page.locator(".jp-adminfeedback__availability")).toHaveText(
    "Feedback är stängd: ingen mottagare för aviseringar är inställd.",
  );
  await atBothWidths(page, "availability-no-recipient");

  harness.feedbackAvailability = "Open";
  harness.feedbackReads.availability = "error";
  await page.goto(PAGE);
  const unknown = page.locator(".jp-adminfeedback__availability");
  await expect(unknown).toHaveText("Det går inte att se om feedback är öppen.");
  await expect(unknown).toHaveAttribute("data-state", "unknown");
  await atBothWidths(page, "availability-unreadable");
});

test("an id that names no submission says so, and one of the wrong shape never reaches the backend", async ({ page }) => {
  const unknownId = "00000000-0000-4000-8000-000000000799";
  await page.goto(`${PAGE}?id=${unknownId}`);

  await expect(detailRegion(page)).toHaveText("Valt inskickInskicket finns inte.");
  await expect(list(page).getByRole("link")).toHaveCount(6);
  expect(harness.requests).toContain(`GET /api/v1/admin/feedback/${unknownId}`);
  await atBothWidths(page, "unknown-id");

  await page.goto(`${PAGE}?id=inte-ett-id`);
  await expect(detailRegion(page)).toHaveText("Valt inskickInskicket finns inte.");
  expect(harness.requests.filter((route) => route.includes("inte-ett-id"))).toEqual([]);
});
