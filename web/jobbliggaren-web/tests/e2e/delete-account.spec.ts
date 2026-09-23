import { test, expect, type Page } from "@playwright/test";
import { ensureConfirmedTestUser, loginAs, takeLoginCode, testEmail } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const SESSION_COOKIE = "__Host-jobbliggaren_session";

/**
 * Deleting the account on /mina-sidor, re-authenticated by a code to the account's own address (#1740,
 * ADR 0142 D5). Each test seeds its own address: a deletion is destructive, and an address logs in once
 * per cooldown (`helpers/session.ts`).
 */

const newRunId = () => Date.now() + Math.floor(Math.random() * 1_000_000);

/** A six-digit code that is certainly not `code`. */
const otherThan = (code: string) => String((Number(code) + 1) % 1_000_000).padStart(6, "0");

async function requestCode(page: Page, typed: string) {
  await page.goto("/mina-sidor");
  await page.getByRole("button", { name: "Radera konto" }).click();
  const dialog = page.getByRole("dialog", { name: "Radera ditt konto" });
  await dialog.getByLabel("Skriv din e-postadress för att bekräfta").fill(typed);
  await dialog.getByRole("button", { name: "Skicka kod" }).click();
  return dialog;
}

test.describe("Radera konto (/mina-sidor)", () => {
  test("checks the typed address when Skicka kod is pressed, and sends no code until it matches", async ({
    page,
  }) => {
    const runId = newRunId();
    await ensureConfirmedTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);

    const dialog = await requestCode(page, "fel@example.se");

    await expect(dialog).toContainText("I 30 dagar kan du få kontot återställt");
    await expect(dialog.getByRole("alert")).toHaveText(
      "Skriv din e-postadress som den står under fältet."
    );
    await expect(dialog.getByLabel("Sexsiffrig kod")).toHaveCount(0);

    await dialog.getByLabel("Skriv din e-postadress för att bekräfta").fill(testEmail(runId));
    await dialog.getByRole("button", { name: "Skicka kod" }).click();

    await expect(dialog.getByLabel("Sexsiffrig kod")).toBeFocused();
  });

  test("deletes on the code: the login page says so, and the old session is dead in the backend", async ({
    page,
  }) => {
    const runId = newRunId();
    await ensureConfirmedTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);
    const session = (await page.context().cookies()).find((c) => c.name === SESSION_COOKIE)?.value;
    expect(session).toBeTruthy();

    const dialog = await requestCode(page, testEmail(runId));
    await dialog.getByLabel("Sexsiffrig kod").fill(await takeLoginCode(testEmail(runId)));
    await dialog.getByRole("button", { name: "Radera mitt konto" }).click();

    await page.waitForURL("**/logga-in");
    await expect(page.getByRole("heading", { name: "Ditt konto är raderat" })).toBeVisible();
    // ADR 0024 D4, GDPR Art. 17: the session is gone in Redis, not only its cookie in this browser.
    const me = await fetch(`${BACKEND_URL}/api/v1/me`, {
      headers: { Authorization: `Bearer ${session}` },
    });
    expect(me.status).toBe(401);
    await page.goto("/mina-sidor");
    await expect(page).toHaveURL(/\/logga-in/);
  });

  test("refuses a wrong code on the field, and deletes nothing", async ({ page }) => {
    const runId = newRunId();
    await ensureConfirmedTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);

    const dialog = await requestCode(page, testEmail(runId));
    await dialog.getByLabel("Sexsiffrig kod").fill(otherThan(await takeLoginCode(testEmail(runId))));
    await dialog.getByRole("button", { name: "Radera mitt konto" }).click();

    await expect(dialog.getByRole("alert")).toHaveText(
      "Koden stämmer inte. Kontrollera siffrorna och försök igen."
    );
    await page.goto("/mina-sidor");
    await expect(page).toHaveURL(/\/mina-sidor$/);
  });
});
