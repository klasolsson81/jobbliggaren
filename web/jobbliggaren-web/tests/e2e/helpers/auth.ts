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
  await page.getByLabel("Lösenord").fill(TEST_PASSWORD);
  await page.getByRole("button", { name: "Logga in" }).click();
  await page.waitForURL("**/mig");
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
