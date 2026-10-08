import { expect, test, type Locator, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { join, win32 } from "node:path";
import { FEEDBACK_IDS, manyFeedback } from "./fixtures";
import { FEEDBACK_SCREENSHOT } from "./screenshot-fixture";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * `/admin/feedback` in a real browser against the production build (#1979, ADR 0150 D2): every region's
 * state rendered from a real response of the fixture backend, the notice mail's link at both widths, a
 * status change and both requeues with their receipts, where focus lands after every link that removes
 * itself, and each state at 1280 and 375 px with no sideways page scroll. Set
 * ADMIN_FEEDBACK_SCREENSHOT_DIR to an external C:/tmp directory to keep a screenshot of each state.
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
/** The phone the design review measured the notice mail's link on. */
const PHONE = { width: 375, height: 812 };

const list = (page: Page) => page.getByRole("region", { name: "Inskick", exact: true });
const rows = (page: Page) => list(page).locator(".jp-adminfeedback__list > li");
const firstRow = (page: Page) => list(page).locator(".jp-adminfeedback__list a").first();
const row = (page: Page, id: string) => list(page).locator(`[data-feedback-item="${id}"]`);
const detailRegion = (page: Page) => page.getByRole("region", { name: "Valt inskick", exact: true });
const summary = (page: Page) => page.getByRole("region", { name: "Betyg per sida", exact: true });
const statusNav = (page: Page) => page.getByRole("navigation", { name: "Filtrera på status" });
const windowNav = (page: Page) => page.getByRole("navigation", { name: "Period" });
const pager = (page: Page) => page.getByRole("navigation", { name: "Sidnavigering" });
const scopeLine = (page: Page) => page.locator(".jp-adminfeedback__scope");
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
  // A full-page capture draws the sticky header where the page is scrolled to, over the content there:
  // capture from the top, and put the page back where the test left it.
  const scrolled = await page.evaluate(() => window.scrollY);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: join(directory, `${state}-${page.viewportSize()?.width ?? 1280}.png`),
    animations: "disabled", fullPage: true });
  await page.evaluate((y) => window.scrollTo(0, y), scrolled);
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

/** A link's height against the touch floor: 32 px in the app, 44 px at 768 px and below (DESIGN.md §5). */
async function expectFloor(link: Locator, floor: 32 | 44) {
  const box = await link.boundingBox();
  expect(box, "the link is not rendered").not.toBeNull();
  expect(box?.height ?? 0, `${await link.textContent()} is under the ${floor} px floor`).toBeGreaterThanOrEqual(floor);
}

/** Presses the link from the keyboard, as the design review measured focus. */
async function pressEnter(link: Locator) {
  await link.focus();
  await link.press("Enter");
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

test("the notice mail's link opens its submission beside the list and leaves focus where the page starts; one opened from the list takes focus", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.failed}`);

  const detail = detailRegion(page);
  await expect(detail).toContainText("Hur länge sparas mitt uppladdade CV om jag inte loggar in på ett tag?");
  await expect(fact(detail, "Avsändare")).toHaveText("konto.g@example.test");
  await expect(fact(detail, "Betyg")).toHaveText("Inget betyg");
  await expect(fact(detail, "Läge")).toHaveText("Misslyckades");
  await expect(fact(detail, "Försök")).toHaveText("5");
  await expect(fact(detail, "Fönster")).toHaveText("1280 × 720");
  await expect(row(page, ids.failed)).toHaveAttribute("aria-current", "true");
  // At 1100 px and wider the list stands beside the submission, so there is no way back to offer.
  await expect(list(page)).toBeVisible();
  await expect(detail.getByRole("link", { name: "Alla inskick" })).toBeHidden();
  expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true);
  expect(harness.requests).toContain(`GET /api/v1/admin/feedback/${ids.failed}`);
  await atBothWidths(page, "mail-link");

  await list(page).getByRole("link", { name: /Sparade annonser/ }).click();
  await expect(page).toHaveURL(`${PAGE}?id=${ids.accepted}`);
  await expect(detail).toContainText("Det vore bra att kunna sortera sparade annonser på sista ansökningsdag.");
  await expect(detail).toBeFocused();
});

test("the open submission's facts, notice and browser report share one value column", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.queued}`);

  const detail = detailRegion(page);
  await expect(fact(detail, "Nästa försök")).toBeVisible();
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: 900 });
    const columns = await detail.locator("dl").evaluateAll((lists) =>
      lists.map((dl) => ({
        values: [...dl.querySelectorAll("dd")].map((dd) => Math.round(dd.getBoundingClientRect().left)),
        // A label that wraps, or runs past its column, is wider than the column.
        crowded: [...dl.querySelectorAll("dt")]
          .filter((dt) => {
            const words = document.createRange();
            words.selectNodeContents(dt);
            return words.getClientRects().length > 1 || dt.scrollWidth > dt.clientWidth;
          })
          .map((dt) => dt.textContent),
      })),
    );
    expect(columns, `${width}px: the facts, the notice and the report`).toHaveLength(3);
    expect(new Set(columns.flatMap((column) => column.values)).size, `${width}px: one value column`).toBe(1);
    expect(columns.flatMap((column) => column.crowded), `${width}px: a label wider than its column`).toEqual([]);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await atBothWidths(page, "label-column");
});

test("on a phone the notice mail's link lands on its submission as a step of its own, and Alla inskick leads back to its row", async ({ page }) => {
  for (const record of manyFeedback(30)) harness.feedback.set(record.id, record);
  await page.setViewportSize(PHONE);
  await page.goto(`${PAGE}?id=${ids.unknown}`);

  const detail = detailRegion(page);
  const back = detail.getByRole("link", { name: "Alla inskick" });
  // The filters and the 25 rows above it step aside: the submission is on the first screen.
  await expect(statusNav(page)).toBeHidden();
  await expect(list(page)).toBeHidden();
  await expect(back).toBeVisible();
  await expect(back).toBeInViewport();
  await expect(fact(detail, "Läge")).toHaveText("Utfall okänt");
  expect(await detail.evaluate((element) => element.getBoundingClientRect().top)).toBeLessThan(PHONE.height / 2);
  await expect(back).toHaveAttribute("href", PAGE);
  await expectFloor(back, 44);
  expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true);
  await capture(page, "phone-mail-link");

  // Back to the list it was opened from, with focus on the submission's own row.
  await pressEnter(back);
  await expect(page).toHaveURL(PAGE);
  await expect(detail).toHaveCount(0);
  await expect(statusNav(page)).toBeVisible();
  await expect(row(page, ids.unknown)).toBeFocused();
  await expect(row(page, ids.unknown)).toBeInViewport();
  await capture(page, "phone-back-to-list");

  // A row opened from the list is the step again, and the submission takes focus at its top.
  await row(page, ids.accepted).click();
  await expect(page).toHaveURL(`${PAGE}?id=${ids.accepted}`);
  await expect(detail).toBeFocused();
  await expect(list(page)).toBeHidden();
  await expect(back).toBeInViewport();
  await capture(page, "phone-opened-from-list");

  // A submission past the list's first page goes back to the first row of the list that is shown.
  await page.goto(`${PAGE}?id=${manyFeedback(30)[29]?.id ?? ""}`);
  await pressEnter(detail.getByRole("link", { name: "Alla inskick" }));
  await expect(page).toHaveURL(PAGE);
  await expect(firstRow(page)).toBeFocused();
});

test("the pager keeps both controls on every page, and the one pressed keeps focus at a bound", async ({ page }) => {
  for (const record of manyFeedback(30)) harness.feedback.set(record.id, record);
  await page.goto(PAGE);

  const position = pager(page).getByRole("status");
  const previous = pager(page).getByRole("link", { name: "Föregående" });
  const next = pager(page).getByRole("link", { name: "Nästa" });
  await expect(position).toHaveText("Sida 1 av 2");
  await expect(rows(page)).toHaveCount(25);
  await expect(previous).toHaveAttribute("aria-disabled", "true");
  await expect(next).not.toHaveAttribute("aria-disabled", "true");
  await atBothWidths(page, "pager-first-page");

  await pressEnter(next);
  await expect(page).toHaveURL(`${PAGE}?sidnr=2`);
  await expect(position).toHaveText("Sida 2 av 2");
  await expect(rows(page)).toHaveCount(11);
  await expect(next).toHaveAttribute("aria-disabled", "true");
  await expect(next).toBeFocused();
  await expect(next).toBeInViewport();

  // At its bound the control goes nowhere and keeps focus.
  await next.press("Enter");
  await expect(page).toHaveURL(`${PAGE}?sidnr=2`);
  await expect(next).toBeFocused();
  await atBothWidths(page, "pager-last-page");

  await pressEnter(previous);
  await expect(page).toHaveURL(PAGE);
  await expect(position).toHaveText("Sida 1 av 2");
  await expect(previous).toHaveAttribute("aria-disabled", "true");
  await expect(previous).toBeFocused();
  await expect(previous).toBeInViewport();
});

test("a page name in the summary filters the list and focus moves to the line that says so; lifting it lands on the list", async ({ page }) => {
  await page.goto(PAGE);

  const jobs = summary(page).getByRole("link", { name: "Jobb", exact: true });
  await expectFloor(jobs, 32);
  await pressEnter(jobs);
  await expect(page).toHaveURL(`${PAGE}?sida=jobs`);
  await expect(scopeLine(page)).toContainText("Sida: Jobb");
  await expect(scopeLine(page)).toBeFocused();
  await expect(scopeLine(page)).toBeInViewport();
  await expect(rows(page)).toHaveCount(1);
  await expect(jobs).toHaveAttribute("aria-current", "true");
  const clear = scopeLine(page).getByRole("link", { name: "Visa inskick från alla sidor" });
  await expectFloor(clear, 32);
  await atBothWidths(page, "scope");

  await page.setViewportSize(PHONE);
  await expectFloor(clear, 44);
  await expectFloor(jobs, 44);
  await page.setViewportSize({ width: 1280, height: 900 });

  await pressEnter(clear);
  await expect(page).toHaveURL(PAGE);
  await expect(scopeLine(page)).toHaveCount(0);
  await expect(rows(page)).toHaveCount(6);
  await expect(firstRow(page)).toBeFocused();
});

test("a page chosen in the summary while a submission is open lands on the filtered list with focus on its line, at 375 and 1280 px", async ({ page }) => {
  await page.setViewportSize(PHONE);
  await page.goto(`${PAGE}?id=${ids.failed}`);
  await expect(list(page)).toBeHidden();
  const tapped = summary(page).getByRole("link", { name: "Jobb", exact: true });
  await tapped.scrollIntoViewIfNeeded();
  await tapped.click();

  await expect(page).toHaveURL(`${PAGE}?sida=jobs`);
  await expect(detailRegion(page)).toHaveCount(0);
  await expect(statusNav(page)).toBeVisible();
  await expect(list(page)).toBeVisible();
  await expect(rows(page)).toHaveCount(1);
  await expect(scopeLine(page)).toContainText("Sida: Jobb");
  await expect(scopeLine(page)).toBeFocused();
  await expect(scopeLine(page)).toBeInViewport();
  await capture(page, "phone-page-choice");

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`${PAGE}?id=${ids.failed}`);
  await expect(detailRegion(page)).toBeVisible();
  await pressEnter(summary(page).getByRole("link", { name: "Jobb", exact: true }));

  await expect(page).toHaveURL(`${PAGE}?sida=jobs`);
  await expect(detailRegion(page)).toHaveCount(0);
  await expect(rows(page)).toHaveCount(1);
  await expect(scopeLine(page)).toBeFocused();
  await expect(scopeLine(page)).toBeInViewport();
  await capture(page, "phone-page-choice");
});

test("a page past the last says so, and its link to the first page lands focus on the list's first submission", async ({ page }) => {
  await page.goto(`${PAGE}?sidnr=9`);

  await expect(list(page)).toContainText("Sidan finns inte.");
  const firstPage = list(page).getByRole("link", { name: "Till första sidan" });
  await expectFloor(firstPage, 32);
  await atBothWidths(page, "page-missing");
  await page.setViewportSize(PHONE);
  await expectFloor(firstPage, 44);
  await page.setViewportSize({ width: 1280, height: 900 });

  await pressEnter(firstPage);
  await expect(page).toHaveURL(PAGE);
  await expect(rows(page)).toHaveCount(6);
  await expect(firstRow(page)).toBeFocused();
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

test("the status a submission already has is refused on the field; a refused command is said under it and leaves the field unmarked", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.queued}`);

  const detail = detailRegion(page);
  const select = detail.getByRole("combobox", { name: "Status" });
  const save = detail.getByRole("button", { name: "Spara status" });
  const refusal = detail.locator(".jp-adminfeedback__status").getByRole("alert");
  await save.click();

  await expect(refusal).toHaveText("Inskicket har redan den statusen.");
  await expect(select).toHaveAttribute("aria-invalid", "true");
  const refusalId = await refusal.getAttribute("id");
  expect(refusalId).not.toBeNull();
  await expect(select).toHaveAttribute("aria-describedby", refusalId ?? "");
  await expect(select).toBeFocused();
  const [field, words] = [await select.boundingBox(), await refusal.boundingBox()];
  expect(words?.y ?? 0, "the refusal stands under its field").toBeGreaterThanOrEqual((field?.y ?? 0) + (field?.height ?? 0));
  expect(harness.feedbackCommands).toEqual([]);
  await atBothWidths(page, "status-unchanged");

  // The backend refuses the value too, as it does when another administrator set it first.
  const record = harness.feedback.get(ids.queued);
  if (record === undefined) throw new Error("the fixture has no queued submission");
  harness.feedback.set(ids.queued, { ...record, status: "InProgress" });
  await select.selectOption({ label: "Pågår" });
  await save.click();
  await expect(refusal).toHaveText("Inskicket har redan den statusen.");
  await expect(select).toHaveAttribute("aria-invalid", "true");
  await expect(select).toBeFocused();
  expect(commands()).toEqual([{ id: ids.queued, command: "status", body: { status: "InProgress" } }]);

  // A refusal of the command, not of the value: said under the field, which stays unmarked.
  harness.mode = "rateLimited";
  await select.selectOption({ label: "Åtgärdad" });
  await save.click();
  await expect(refusal).toHaveText("För många förfrågningar. Försök igen om 6 sekunder.");
  await expect(select).not.toHaveAttribute("aria-invalid", "true");
  await expect(select).toHaveAttribute("aria-describedby", (await refusal.getAttribute("id")) ?? "");
  await expect(refusal).toBeFocused();
  await expect(toast(page)).toHaveCount(0);
  await atBothWidths(page, "status-refused");
});

test("on a phone the status row keeps its place when a refusal shows under it", async ({ page }) => {
  await page.setViewportSize(PHONE);
  await page.goto(`${PAGE}?id=${ids.queued}`);

  const detail = detailRegion(page);
  const pill = detail.locator(".jp-adminfeedback__detailhead > .jp-pill");
  const select = detail.getByRole("combobox", { name: "Status" });
  const save = detail.getByRole("button", { name: "Spara status" });
  const refusal = detail.locator(".jp-adminfeedback__status").getByRole("alert");
  const place = (target: Locator) =>
    target.evaluate((element) => {
      const box = element.getBoundingClientRect();
      return { x: Math.round(box.left), y: Math.round(box.top + window.scrollY), width: Math.round(box.width) };
    });
  const row = { pill: await place(pill), select: await place(select), save: await place(save) };
  expect(row.pill.y, "the pill and the status field share a line").toBeGreaterThanOrEqual(row.select.y - 8);

  await save.click();
  await expect(refusal).toHaveText("Inskicket har redan den statusen.");
  expect({ pill: await place(pill), select: await place(select), save: await place(save) }).toEqual(row);
  expect((await place(refusal)).y).toBeGreaterThan(row.select.y);

  harness.mode = "rateLimited";
  await select.selectOption({ label: "Åtgärdad" });
  await save.click();
  await expect(refusal).toHaveText("För många förfrågningar. Försök igen om 6 sekunder.");
  expect({ pill: await place(pill), select: await place(select), save: await place(save) }).toEqual(row);
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

test("a notice whose outcome is unknown is sent again only after the duplicate warning, asked in the neutral tone", async ({ page }) => {
  await page.goto(`${PAGE}?id=${ids.unknown}`);

  const detail = detailRegion(page);
  await expect(fact(detail, "Läge")).toHaveText("Utfall okänt");
  const resend = detail.getByRole("button", { name: "Skicka avisering igen" });
  await resend.click();

  const question = page.getByRole("alertdialog", { name: "Skicka aviseringen igen?" });
  await expect(question).toContainText("Aviseringen kan redan ha kommit fram. Ett nytt utskick kan ge en dubblett.");
  await expect(question.getByRole("button", { name: "Avbryt" })).toBeFocused();
  // Nothing is destroyed and the body names the risk: the dialog's one primary, never danger.
  const confirm = question.getByRole("button", { name: "Skicka avisering igen" });
  await expect(confirm).toHaveClass(/\bjp-btn--primary\b/);
  await expect(confirm).not.toHaveClass(/\bjp-btn--danger\b/);
  await expect(question.locator(".jp-btn--primary")).toHaveCount(1);
  await atBothWidths(page, "requeue-unknown-question");
  await question.getByRole("button", { name: "Avbryt" }).click();
  await expect(question).toBeHidden();
  await expect(resend).toBeFocused();
  expect(harness.feedbackCommands).toEqual([]);

  await resend.click();
  await confirm.click();
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

test("an id that names no submission says so; one of the wrong shape opens nothing, reaches no backend and no link", async ({ page }) => {
  const unknownId = "00000000-0000-4000-8000-000000000799";
  await page.goto(`${PAGE}?id=${unknownId}`);

  await expect(detailRegion(page)).toContainText("Inskicket finns inte.");
  await expect(list(page).getByRole("link")).toHaveCount(6);
  expect(harness.requests).toContain(`GET /api/v1/admin/feedback/${unknownId}`);
  await atBothWidths(page, "unknown-id");

  await page.goto(`${PAGE}?id=inte-ett-id`);
  await expect(detailRegion(page)).toHaveCount(0);
  await expect(list(page).getByRole("link")).toHaveCount(6);
  expect(harness.requests.filter((route) => route.includes("inte-ett-id"))).toEqual([]);
  const hrefs = await page.locator("a[href^='/admin/feedback']").evaluateAll((links) =>
    links.map((link) => link.getAttribute("href") ?? ""),
  );
  expect(hrefs.length).toBeGreaterThan(0);
  expect(hrefs.filter((href) => href.includes("inte-ett-id"))).toEqual([]);
});

const screenshotSection = (page: Page) => detailRegion(page).getByRole("region", { name: "Skärmbild", exact: true });
const imagePane = (page: Page) =>
  screenshotSection(page).getByRole("region", { name: "Skärmbild som bifogats feedbacken", exact: true });
const screenshotImage = (page: Page) =>
  screenshotSection(page).getByRole("img", { name: "Skärmbild som bifogats feedbacken", exact: true });

function attachScreenshot(id: string) {
  const record = harness.feedback.get(id);
  if (record === undefined) throw new Error("The fixture has no submission to attach the screenshot to.");
  harness.feedbackScreenshots.set(id, FEEDBACK_SCREENSHOT);
  harness.feedback.set(id, {
    ...record,
    screenshot: { width: FEEDBACK_SCREENSHOT.width, height: FEEDBACK_SCREENSHOT.height },
  });
}

async function pageFits(page: Page, state: string) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, state + " scrolls the page sideways").toBeLessThanOrEqual(0);
}

function screenshotReply(page: Page) {
  return page.waitForResponse((reply) =>
    new URL(reply.url()).pathname === "/api/admin/feedback/screenshot" && reply.request().method() === "POST");
}

type ScreenshotAudit = { created: string[]; revoked: string[]; aborted: string[] };

async function installScreenshotAudit(page: Page) {
  await page.addInitScript(() => {
    const audit: ScreenshotAudit = { created: [], revoked: [], aborted: [] };
    (window as typeof window & { __feedbackScreenshotAudit: ScreenshotAudit }).__feedbackScreenshotAudit = audit;
    const create = URL.createObjectURL.bind(URL);
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.createObjectURL = (object) => {
      const url = create(object);
      audit.created.push(url);
      return url;
    };
    URL.revokeObjectURL = (url) => {
      audit.revoked.push(url);
      revoke(url);
    };
    const fetchOriginal = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const address = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
      if (new URL(address, window.location.href).pathname === "/api/admin/feedback/screenshot") {
        let id = "";
        if (typeof init?.body === "string") {
          const body: unknown = JSON.parse(init.body);
          if (body !== null && typeof body === "object" && "id" in body && typeof body.id === "string")
            id = body.id;
        }
        const signal = init?.signal ?? (input instanceof Request ? input.signal : null);
        signal?.addEventListener("abort", () => audit.aborted.push(id), { once: true });
      }
      return fetchOriginal(input, init);
    };
  });
}

async function screenshotAudit(page: Page) {
  return page.evaluate(() =>
    (window as typeof window & { __feedbackScreenshotAudit: ScreenshotAudit }).__feedbackScreenshotAudit);
}

for (const width of [1280, 1920, 3440, 375]) {
  test("a real PNG has a thumbnail and a keyboard-scrollable full size contained in the detail at " + width + " px", async ({ page }) => {
    await page.setViewportSize({ width, height: width === 375 ? PHONE.height : 900 });
    attachScreenshot(ids.queued);
    const replied = screenshotReply(page);
    await page.goto(PAGE + "?id=" + ids.queued);
    const response = await replied;
    expect(response.status()).toBe(200);
    expect(response.headers()["content-type"]).toContain("image/png");
    expect(response.headers()["cache-control"]).toContain("no-store");
    expect(response.headers()["x-content-type-options"]).toBe("nosniff");

    const section = screenshotSection(page);
    const image = screenshotImage(page);
    await expect(image).toBeVisible();
    await expect.poll(() => image.evaluate((element) => {
      const image = element as HTMLImageElement;
      return { width: image.naturalWidth, height: image.naturalHeight, complete: image.complete };
    })).toEqual({ width: FEEDBACK_SCREENSHOT.width, height: FEEDBACK_SCREENSHOT.height, complete: true });
    const thumbnail = await image.boundingBox();
    expect(thumbnail).not.toBeNull();
    expect(thumbnail?.width ?? Infinity).toBeLessThan(FEEDBACK_SCREENSHOT.width);
    expect(thumbnail?.height ?? Infinity).toBeLessThan(FEEDBACK_SCREENSHOT.height);
    expect(thumbnail?.width ?? Infinity).toBeLessThanOrEqual((await section.boundingBox())?.width ?? 0);
    await pageFits(page, "screenshot thumbnail at " + width);
    await capture(page, "screenshot-thumbnail");

    const toggle = section.getByRole("button");
    await expectFloor(toggle, width === 375 ? 44 : 32);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await pressEnter(toggle);
    await expect(toggle).toHaveAttribute("aria-expanded", "true");
    await expect(toggle).toHaveText("Visa miniatyr");
    await expect(toggle).toBeFocused();
    const pane = imagePane(page);
    await expect(pane).toHaveAttribute("data-full-size", "true");
    await expect(pane).toHaveAttribute("tabindex", "0");
    const paneId = await pane.getAttribute("id");
    expect(paneId).not.toBeNull();
    await expect(toggle).toHaveAttribute("aria-controls", paneId ?? "");
    await toggle.press("Tab");
    await expect(pane).toBeFocused();
    const bounds = await pane.evaluate((element) => ({
      scrollWidth: element.scrollWidth, width: element.clientWidth,
      scrollHeight: element.scrollHeight, height: element.clientHeight,
    }));
    expect(bounds.scrollWidth).toBeGreaterThan(bounds.width);
    expect(bounds.scrollHeight).toBeGreaterThan(bounds.height);
    await expect.poll(() => image.evaluate((element) => Math.round(element.getBoundingClientRect().width)))
      .toBe(FEEDBACK_SCREENSHOT.width);
    await pageFits(page, "screenshot full size at " + width);
    await capture(page, "screenshot-full-size");

    const windowPosition = await page.evaluate(() => ({ x: window.scrollX, y: window.scrollY }));
    await pane.press("ArrowRight");
    await expect.poll(() => pane.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
    await pane.press("PageDown");
    await expect.poll(() => pane.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    expect(await page.evaluate(() => ({ x: window.scrollX, y: window.scrollY }))).toEqual(windowPosition);
    await pane.press("Shift+Tab");
    await expect(toggle).toBeFocused();
    await toggle.press(" ");
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await expect(toggle).toHaveText("Visa full storlek");
    await expect(pane).not.toHaveAttribute("tabindex", "0");
    expect(harness.feedbackScreenshotRequests).toEqual([ids.queued]);
    if (width === 375) await expect(list(page)).toBeHidden();
    else await expect(list(page)).toBeVisible();
  });
}

for (const width of [1280, 3440, 375]) {
  for (const state of ["absent", "notFound", "error", "loading"] as const) {
    test("the screenshot's " + state + " state is rendered truthfully at " + width + " px", async ({ page }) => {
      await page.setViewportSize({ width, height: width === 375 ? PHONE.height : 900 });
      if (state !== "absent") {
        attachScreenshot(ids.queued);
        // A 404 models the documented operator deleting only the image after the detail metadata was read.
        harness.feedbackScreenshotReads.set(ids.queued, state);
      }
      const reply = state === "error" || state === "notFound" ? screenshotReply(page) : null;
      try {
        await page.goto(PAGE + "?id=" + ids.queued);
        const section = screenshotSection(page);
        if (state === "loading") {
          await expect(section.getByRole("status")).toHaveText("Hämtar skärmbilden…");
          await expect.poll(() => harness.feedbackScreenshotRequests).toEqual([ids.queued]);
          expect(harness.feedbackScreenshotAnswers).toEqual([]);
        } else {
          const message = state === "error" ? "Skärmbilden kunde inte hämtas. Ladda om sidan." : "Det finns ingen skärmbild.";
          await expect(section.getByText(message, { exact: true })).toBeVisible();
          await expect(section.getByRole("status")).toHaveText(message);
        }
        await expect(section.getByRole("img")).toHaveCount(0);
        await expect(section.getByRole("button", { name: "Visa full storlek", exact: true })).toHaveCount(0);
        if (reply !== null) {
          const response = await reply;
          expect(response.status()).toBe(state === "notFound" ? 404 : 502);
          expect(response.headers()["content-type"]).toContain("application/json");
          expect(response.headers()["cache-control"]).toContain("no-store");
          expect(response.headers()["x-content-type-options"]).toBe("nosniff");
        }
        if (state === "absent") expect(harness.feedbackScreenshotRequests).toEqual([]);
        await pageFits(page, "screenshot " + state + " at " + width);
        await capture(page, "screenshot-" + state);
      } finally {
        harness.releaseFeedbackScreenshots();
      }
    });
  }
}

test("changing details revokes the real blob and aborts a held image read without showing a stale image", async ({ page }) => {
  attachScreenshot(ids.queued);
  await installScreenshotAudit(page);
  await page.goto(PAGE + "?id=" + ids.queued);
  await expect(screenshotImage(page)).toBeVisible();
  const firstUrl = await screenshotImage(page).getAttribute("src");
  expect(firstUrl).not.toBeNull();
  await expect.poll(async () => (await screenshotAudit(page)).created).toEqual([firstUrl]);

  await pressEnter(row(page, ids.accepted));
  await expect(page).toHaveURL(PAGE + "?id=" + ids.accepted);
  await expect(screenshotSection(page)).toContainText("Det finns ingen skärmbild.");
  await expect(screenshotSection(page).getByRole("img")).toHaveCount(0);
  await expect.poll(async () => (await screenshotAudit(page)).revoked).toEqual([firstUrl]);
  expect(await page.evaluate(async (url) => {
    try { return (await fetch(url)).ok; } catch { return false; }
  }, firstUrl ?? "")).toBe(false);
  expect(harness.feedbackScreenshotRequests).toEqual([ids.queued]);

  harness.feedbackScreenshotReads.set(ids.queued, "loading");
  try {
    await pressEnter(row(page, ids.queued));
    await expect(screenshotSection(page).getByRole("status")).toHaveText("Hämtar skärmbilden…");
    await expect.poll(() => harness.feedbackScreenshotRequests).toEqual([ids.queued, ids.queued]);
    await pressEnter(row(page, ids.accepted));
    await expect(screenshotSection(page)).toContainText("Det finns ingen skärmbild.");
    await expect.poll(async () => (await screenshotAudit(page)).aborted).toEqual([ids.queued, ids.queued]);
  } finally {
    harness.releaseFeedbackScreenshots();
  }
  await expect.poll(() => harness.feedbackScreenshotAnswers).toEqual([ids.queued, ids.queued]);
  expect((await screenshotAudit(page)).created).toEqual([firstUrl]);
  expect((await screenshotAudit(page)).revoked).toEqual([firstUrl]);
  await expect(screenshotSection(page).getByRole("img")).toHaveCount(0);
  await expect(screenshotSection(page).getByRole("status")).toHaveText("Det finns ingen skärmbild.");
  await pageFits(page, "screenshot detail cleanup");
  await capture(page, "screenshot-detail-cleanup");
});
