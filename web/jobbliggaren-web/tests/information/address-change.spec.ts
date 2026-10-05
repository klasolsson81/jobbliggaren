import { expect, test, type Browser, type Page } from "@playwright/test";
import { ADDRESS_CHANGE_CODES, APP_ORIGIN, startHarness, type Harness } from "./servers";

/**
 * #1975 — /adressbyte, where an account's owner completes the address change an administrator started, in a real
 * browser over the harness's backend, whose answer the code typed chooses. Without JavaScript every answer still
 * renders (design-reviewer R1): the form posts to its Server Action natively and the page comes back with the answer,
 * the addresses re-seeded and the code never echoed. With JavaScript the browser's own check sends nothing, and focus
 * follows the answer.
 */

let harness: Harness;
test.beforeAll(async () => {
  harness = await startHarness(true);
});
test.afterAll(async () => {
  await harness?.stop();
});
test.beforeEach(() => harness.reset());
test.afterEach(() => expect(harness.misses).toEqual([]));

const CURRENT = "anna@exempel.test";
const NEW = "anna.ny@exempel.test";
const COMPLETE = "POST /api/v1/auth/account-email-change/complete";
/** Any code the backend has no answer of its own for: the one refusal. */
const REFUSED = "999999";

async function withoutJavaScript(browser: Browser, run: (page: Page) => Promise<void>) {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL: APP_ORIGIN, ignoreHTTPSErrors: true });
  try {
    await run(await context.newPage());
  } finally {
    await context.close();
  }
}

async function send(page: Page, code: string) {
  await page.getByLabel("Kontots nuvarande e-postadress").fill(CURRENT);
  await page.getByLabel("Ny e-postadress").fill(NEW);
  await page.getByLabel("Sexsiffrig kod").fill(code);
  await page.getByRole("button", { name: "Byt adress" }).click();
}

test("answers without a session, says what it is for, and asks not to be indexed", async ({ browser }) => {
  await withoutJavaScript(browser, async (page) => {
    const response = await page.goto("/adressbyte");

    expect(response?.status()).toBe(200);
    await expect(page).toHaveTitle(/^Bekräfta ny e-postadress/);
    await expect(page.getByRole("heading", { level: 1, name: "Bekräfta ny e-postadress" })).toBeVisible();
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute("content", /noindex/);
    await expect(page.getByLabel("Sexsiffrig kod")).toBeVisible();
  });
});

const ANSWERS = [
  ["the one refusal", REFUSED, "Adressbytet gick inte att genomföra."],
  ["not yet", ADDRESS_CHANGE_CODES.notYet, "Adressen kan bytas tidigast 2026-10-08 kl 14:00. Försök igen då med samma kod."],
  ["too many attempts", ADDRESS_CHANGE_CODES.tooManyAttempts, "För många försök. Vänta en stund och försök igen."],
  ["no service", ADDRESS_CHANGE_CODES.unavailable, "Det går inte att byta adress just nu. Försök igen om några minuter."],
  [
    "the unknown outcome",
    ADDRESS_CHANGE_CODES.unknown,
    "Vi kan inte se om adressen byttes. Skriv till kontakt@jobbliggaren.se så tar vi reda på det.",
  ],
] as const;

for (const [label, code, copy] of ANSWERS) {
  test(`without JavaScript, renders ${label} with the addresses kept and the code never echoed`, async ({ browser }) => {
    await withoutJavaScript(browser, async (page) => {
      await page.goto("/adressbyte");
      await send(page, code);

      await expect(page.locator("main")).toContainText(copy);
      await expect(page.getByLabel("Kontots nuvarande e-postadress")).toHaveValue(CURRENT);
      await expect(page.getByLabel("Ny e-postadress")).toHaveValue(NEW);
      await expect(page.getByLabel("Sexsiffrig kod")).toHaveValue("");
      expect(await page.content()).not.toContain(code);
      expect(harness.requests).toContain(COMPLETE);
    });
  });
}

test("without JavaScript, a change done replaces the form with the receipt and a way to log in", async ({ browser }) => {
  await withoutJavaScript(browser, async (page) => {
    await page.goto("/adressbyte");
    await send(page, ADDRESS_CHANGE_CODES.done);

    const main = page.locator("main");
    await expect(main.getByRole("heading", { level: 2, name: "Adressen är bytt" })).toBeVisible();
    await expect(main).toContainText("Du är utloggad på alla enheter. Logga in med den nya adressen.");
    await expect(main.getByRole("link", { name: "Logga in", exact: true })).toHaveAttribute("href", "/logga-in");
    await expect(main.getByRole("button", { name: "Byt adress" })).toHaveCount(0);
  });
});

test("without JavaScript, the action checks the fields as the browser would", async ({ browser }) => {
  await withoutJavaScript(browser, async (page) => {
    await page.goto("/adressbyte");
    await page.getByRole("button", { name: "Byt adress" }).click();

    await expect(page.locator("main")).toContainText("Skriv in kontots nuvarande e-postadress.");
    await expect(page.getByLabel("Kontots nuvarande e-postadress")).toHaveAttribute("aria-invalid", "true");
    expect(harness.requests).not.toContain(COMPLETE);
  });
});

test("with JavaScript, the browser's own check sends nothing, and the one refusal takes focus and marks no field", async ({
  page,
}) => {
  await page.goto("/adressbyte");
  // The heading takes focus when the page has hydrated, so the presses below are the form's, not a native post.
  await expect(page.getByRole("heading", { level: 1, name: "Bekräfta ny e-postadress" })).toBeFocused();

  await page.getByRole("button", { name: "Byt adress" }).click();
  await expect(page.getByLabel("Kontots nuvarande e-postadress")).toBeFocused();
  expect(harness.requests).not.toContain(COMPLETE);

  await send(page, REFUSED);
  const alert = page.locator("main").getByRole("alert");
  await expect(alert).toContainText("Adressbytet gick inte att genomföra.");
  await expect(alert).toBeFocused();
  await expect(page.locator("main [aria-invalid]")).toHaveCount(0);
  await expect(page.getByLabel("Ny e-postadress")).toHaveValue(NEW);
});
