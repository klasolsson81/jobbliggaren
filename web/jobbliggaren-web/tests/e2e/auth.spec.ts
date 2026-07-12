import { test, expect } from "@playwright/test";
import { TEST_PASSWORD, ensureTestUser, testEmail } from "./helpers/auth";

/**
 * #791 / #733 — email-confirmation resend affordance (register-202 panel + login-403 gate).
 *
 * REQUIRES a backend with `Auth:RequireEmailConfirmation` = true (the dev default). With the flag
 * OFF, register returns 200 + instant login and login succeeds, so neither the register-202
 * "check inbox" panel nor the login-403 gate renders and the resend button under test never
 * appears. The existing `loginAs`-based specs (delete-account / applications / cv) conversely
 * assume the flag OFF (they wait for `/mig`). The two sets therefore cannot run against the same
 * backend instance; reconciling that (per-test flag toggle / separate backend) is the
 * Playwright-in-CI infra issue, not this spec.
 *
 * The login-403 test is the regression for #791: before the fix, the resend button read the live
 * (React-19-reset, empty) email input and silently no-op'd. It now reads the submitted email from
 * the action state, so clicking it produces the uniform "sent" confirmation.
 */

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";

const RESEND_BUTTON = "Skicka en ny bekräftelselänk";
const RESEND_SENT =
  "Om adressen behöver bekräftas har vi skickat en ny länk. Kontrollera inkorgen och skräpposten.";
const LOGIN_403_COPY =
  "Bekräfta din e-postadress för att logga in. Vi har skickat en länk till din inkorg.";

test.describe("auth email-confirmation resend (flag ON)", () => {
  test("register-202 shows the check-inbox panel with a working resend button", async ({
    page,
  }) => {
    // A fresh address each run — register is uniform 202 (fresh or taken), so re-runs are harmless.
    const email = `test-e2e-${Date.now()}-reg@e2e.jobbliggaren.test`;

    await page.goto("/registrera");
    await page.getByLabel("Namn").fill("E2E Testare");
    await page.getByLabel("E-postadress").fill(email);
    // exact: the PasswordInput's "Visa lösenord" toggle also matches a loose "Lösenord" label.
    await page.getByLabel("Lösenord", { exact: true }).fill(TEST_PASSWORD);
    await page.getByRole("button", { name: "Skapa konto" }).click();

    // #714: uniform 202 -> check-inbox panel replaces the form; NO auto-login.
    await expect(
      page.getByRole("heading", { name: "Kontrollera din inkorg" })
    ).toBeVisible();

    const resend = page.getByRole("button", { name: RESEND_BUTTON });
    await expect(resend).toBeVisible();

    await resend.click();
    // #733: uniform "sent" confirmation (anti-enum: identical regardless of account existence).
    await expect(page.getByText(RESEND_SENT)).toBeVisible();
  });

  test("login-403 gates an unconfirmed account and the resend button works (#791 regression)", async ({
    page,
  }) => {
    // Seed an UNCONFIRMED account via the API (register 202 under the flag; never confirmed).
    const runId = Date.now();
    await ensureTestUser(BACKEND_URL, runId);

    await page.goto("/logga-in");
    await page.getByLabel("E-postadress").fill(testEmail(runId));
    // exact: the PasswordInput's "Visa lösenord" toggle also matches a loose "Lösenord" label.
    await page.getByLabel("Lösenord", { exact: true }).fill(TEST_PASSWORD);
    await page.getByRole("button", { name: "Logga in" }).click();

    // #714: correct password + unconfirmed -> distinct 403 with actionable copy (no redirect).
    await expect(page.getByText(LOGIN_403_COPY)).toBeVisible();

    const resend = page.getByRole("button", { name: RESEND_BUTTON });
    await expect(resend).toBeVisible();

    // #791: pre-fix this was a silent no-op (the React-reset email input read ""). Post-fix it reads
    // the submitted email from the action state and produces the uniform "sent" confirmation.
    await resend.click();
    await expect(page.getByText(RESEND_SENT)).toBeVisible();
  });
});
