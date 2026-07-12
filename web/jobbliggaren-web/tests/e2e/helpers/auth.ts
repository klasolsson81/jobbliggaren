import { type Page } from "@playwright/test";

/**
 * Säkerhetsguards för E2E-test-helpers (TD-11).
 *
 * - `TEST_USER_PASSWORD` läses från env. Fallback till klart-test-lösenord
 *   för lokal utveckling. Får aldrig matcha riktigt prod-lösenord (BUILD.md
 *   §13.1 "Känsligt").
 * - Test-domänen är `e2e.jobbliggaren.test` — RFC 6761 reserverar `.test` TLD
 *   som non-resolvable för testning. Eliminerar risken att test-konton
 *   skapas mot riktiga email-adresser eller produktionsdomäner.
 * - `assertSafeBaseURL` kastar om någon försöker peka helpers mot ett
 *   icke-localhost / icke-staging-URL. Skyddar mot misskonfigurerade
 *   CI-pipelines som råkar köra E2E mot prod.
 */
export const TEST_PASSWORD =
  process.env.TEST_USER_PASSWORD ?? "E2eTestPass123!Dev";
const TEST_EMAIL_DOMAIN = "e2e.jobbliggaren.test";

export function testEmail(runId: number): string {
  return `test-e2e-${runId}@${TEST_EMAIL_DOMAIN}`;
}

function assertSafeBaseURL(url: string): void {
  // Hostname-parse i stället för substring-match — substring kan kringgås
  // av `https://localhost.evil.com/`, `https://prod.jobbliggaren.se/?path=staging` osv.
  let host: string;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    throw new Error(`E2E-helper avbruten: ogiltig URL "${url}". Se TD-11.`);
  }
  const allowed =
    host === "localhost" ||
    host === "127.0.0.1" ||
    host === "staging.jobbliggaren.se" ||
    host === "dev.jobbliggaren.se" ||
    host.endsWith(".staging.jobbliggaren.se") ||
    host.endsWith(".dev.jobbliggaren.se");
  if (!allowed) {
    throw new Error(
      `E2E-helper avbruten: misstänkt produktions-host "${host}" (URL: ${url}). ` +
        `Tillåtna hostnamn: localhost, 127.0.0.1, *.staging.jobbliggaren.se, *.dev.jobbliggaren.se. ` +
        `Se TD-11.`
    );
  }
}

export async function loginAs(page: Page, runId: number): Promise<void> {
  await page.goto("/logga-in");
  // Playwright resolverar page.goto mot config-baseURL — guard:a efter navigation
  // för att fånga felkonfigurerade baseURL (CI mot prod) innan credentials fylls i.
  assertSafeBaseURL(page.url());
  await page.getByLabel("E-postadress").fill(testEmail(runId));
  // exact: the shared PasswordInput's "Visa lösenord" toggle also matches a loose
  // "Lösenord" label (strict-mode violation → fill fails), same as auth.spec.ts.
  await page.getByLabel("Lösenord", { exact: true }).fill(TEST_PASSWORD);
  await page.getByRole("button", { name: "Logga in" }).click();
  // A successful login redirects to /oversikt (loginAction's safeRedirectPath default) —
  // the old "**/mig" wait could never match: /mig has been a 308 → /installningar since
  // ADR 0057, and it was never the post-login target. #813.
  await page.waitForURL("**/oversikt");
}

export async function ensureTestUser(baseURL: string, runId: number): Promise<void> {
  assertSafeBaseURL(baseURL);
  const res = await fetch(`${baseURL}/api/v1/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: testEmail(runId), password: TEST_PASSWORD, displayName: "E2E Testare" }),
  });
  if (!res.ok && res.status !== 409) {
    if (res.status === 400) {
      const body = await res.json().catch(() => ({}));
      if (!String(body?.title ?? "").includes("Duplicate")) {
        throw new Error(`Failed to create test user: ${res.status} ${JSON.stringify(body)}`);
      }
    } else {
      throw new Error(`Failed to create test user: ${res.status}`);
    }
  }
}

/**
 * Force-confirms the test account's email via the DEV-ONLY confirmed-login seam
 * (`POST /api/v1/dev/confirm-email`, #796). Only reachable in Development — the
 * endpoint is mapped and the impl DI-registered ONLY under IsDevelopment(). Lets the
 * loginAs specs obtain a CONFIRMED, login-capable user against a flag-ON backend
 * (Auth:RequireEmailConfirmation=true) without a real out-of-band email round-trip.
 * Tolerates 404 (account not found — treated as a no-op so callers can be defensive).
 */
export async function confirmTestUser(baseURL: string, runId: number): Promise<void> {
  assertSafeBaseURL(baseURL);
  const res = await fetch(`${baseURL}/api/v1/dev/confirm-email`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: testEmail(runId) }),
  });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Failed to confirm test user: ${res.status}`);
  }
}

/**
 * Register + confirm in one step: the seeding path for every `loginAs`-based spec.
 * Under the launch-representative flag ON, a bare register leaves the account
 * unconfirmed (→ login-403), so `loginAs` would time out waiting for /mig. This pairs
 * the register with the dev confirmed-login seam so the account can log in.
 *
 * NOTE: keep `ensureTestUser` (register-only) for `auth.spec.ts`, whose login-403
 * regression deliberately needs an UNCONFIRMED user — do not fold confirm into it.
 */
export async function ensureConfirmedTestUser(baseURL: string, runId: number): Promise<void> {
  await ensureTestUser(baseURL, runId);
  await confirmTestUser(baseURL, runId);
}
