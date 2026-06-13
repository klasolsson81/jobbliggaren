import { test, expect } from "@playwright/test";
import { loginAs, ensureTestUser, TEST_PASSWORD, testEmail } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";

/**
 * TD-65 — End-to-end-flow för konto-radering. Verifierar hela kedjan
 * från login → /mig → typed-confirmation + re-auth → backend delete →
 * cookie-borttagning + redirect.
 *
 * Varje test skapar egen runId så user:n inte återanvänds (destruktiv
 * operation — gammal user är borta efter delete-success).
 */

test.describe("Radera konto (/mig)", () => {
  test("öppnar modal med typed-confirmation och håller submit disabled tills email-match + password", async ({ page }) => {
    const runId = Date.now() + Math.floor(Math.random() * 1_000_000);
    await ensureTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);

    await page.goto("/mig");
    await expect(
      page.getByRole("heading", { name: "Farligt område" })
    ).toBeVisible();

    await page
      .getByRole("button", { name: "Radera konto permanent" })
      .first()
      .click();

    await expect(
      page.getByRole("heading", { name: "Radera konto permanent" })
    ).toBeVisible();
    await expect(
      page.getByText(/Den här åtgärden går inte att ångra/)
    ).toBeVisible();

    // Submit disabled från start (inget ifyllt)
    const submitBtn = page.getByRole("button", { name: "Radera mitt konto" });
    await expect(submitBtn).toBeDisabled();

    // Fel email → fortfarande disabled
    await page
      .getByLabel(/Skriv din e-postadress/)
      .fill("fel@example.se");
    await page.getByLabel("Lösenord").fill(TEST_PASSWORD);
    await expect(submitBtn).toBeDisabled();

    // Rätt email → submit aktiveras
    await page.getByLabel(/Skriv din e-postadress/).fill(testEmail(runId));
    await expect(submitBtn).toBeEnabled();
  });

  test("happy path: delete → redirect till /logga-in + session ogiltig", async ({ page }) => {
    const runId = Date.now() + Math.floor(Math.random() * 1_000_000);
    await ensureTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);

    // Fånga session-token FÖRE delete så vi kan verifiera Redis-revoke
    // efter delete. Browser-cookie-redirect ensam räcker inte —
    // ADR 0024 D4 kräver att ISessionStore.InvalidateAllForUserAsync
    // körs post-commit. Direkt backend-anrop med gamla token bevakar att
    // Redis-sessionen faktiskt är borta (skyddar mot cookie-stöld-scenario).
    const preCookies = await page.context().cookies();
    const sessionCookie = preCookies.find(
      (c) => c.name === "__Host-jobbliggaren_session"
    );
    expect(sessionCookie?.value).toBeTruthy();
    const sessionToken = sessionCookie!.value;

    await page.goto("/mig");
    await page
      .getByRole("button", { name: "Radera konto permanent" })
      .first()
      .click();

    await page.getByLabel(/Skriv din e-postadress/).fill(testEmail(runId));
    await page.getByLabel("Lösenord").fill(TEST_PASSWORD);
    await page.getByRole("button", { name: "Radera mitt konto" }).click();

    // Server action → deleteSessionCookie + redirect("/logga-in")
    await page.waitForURL("**/logga-in", { timeout: 10_000 });

    // Säkerhetsinvariant 1: middleware blockerar /mig efter cookie-borttagning
    await page.goto("/mig");
    await expect(page).toHaveURL(/\/logga-in/);

    // Säkerhetsinvariant 2 (ADR 0024 D4 + GDPR Art. 17): backend-session
    // i Redis är invaliderad. Direkt anrop med gamla token → 401, inte 200.
    const backendCheck = await fetch(`${BACKEND_URL}/api/v1/me`, {
      headers: { Authorization: `Bearer ${sessionToken}` },
    });
    expect(backendCheck.status).toBe(401);
  });

  test("fel lösenord → form-error och session intakt", async ({ page }) => {
    const runId = Date.now() + Math.floor(Math.random() * 1_000_000);
    await ensureTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);

    await page.goto("/mig");
    await page
      .getByRole("button", { name: "Radera konto permanent" })
      .first()
      .click();

    await page.getByLabel(/Skriv din e-postadress/).fill(testEmail(runId));
    await page.getByLabel("Lösenord").fill("WrongPassword!");
    await page.getByRole("button", { name: "Radera mitt konto" }).click();

    // POST /auth/verify → 401 → action returnerar { success:false, error:"Lösenordet är felaktigt." }
    await expect(page.getByRole("alert")).toContainText(
      "Lösenordet är felaktigt"
    );

    // Session intakt: vi är fortfarande på /mig
    await expect(page).toHaveURL(/\/mig/);
  });
});
