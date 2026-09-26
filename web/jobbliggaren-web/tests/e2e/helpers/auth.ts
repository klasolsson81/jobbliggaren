import { type Page } from "@playwright/test";
import { SESSION_COOKIE_NAME } from "../../../src/lib/auth/cookie-names";

/**
 * Säkerhetsguards för E2E-test-helpers (TD-11).
 *
 * - Test-domänen är `e2e.jobbliggaren.test` — RFC 6761 reserverar `.test` TLD
 *   som non-resolvable för testning. Eliminerar risken att test-konton
 *   skapas mot riktiga email-adresser eller produktionsdomäner.
 * - `assertSafeBaseURL` kastar om någon försöker peka helpers mot ett
 *   icke-localhost / icke-staging-URL. Skyddar mot misskonfigurerade
 *   CI-pipelines som råkar köra E2E mot prod.
 */
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

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";

/**
 * Reads the login code the DEV-ONLY seam captured for a reserved recipient
 * (`POST /api/v1/dev/login-code`, mapped under IsDevelopment() only; ADR 0142 D10).
 *
 * It POLLS, because the 202 from `/auth/challenge` returns BEFORE the challenge is written and the
 * mail sent: a background consumer does both. Same cadence as the backend's own tests.
 *
 * A timeout names its causes, because none of them looks like a broken test from the outside: the
 * budgets are constants in `LoginChallengePolicy` and they are silent by design.
 */
export async function takeLoginCode(email: string): Promise<string> {
  assertSafeBaseURL(BACKEND_URL);
  const deadline = Date.now() + 30_000;
  let lastStatus = 0;
  while (Date.now() < deadline) {
    const res = await fetch(`${BACKEND_URL}/api/v1/dev/login-code`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });
    lastStatus = res.status;
    if (res.ok) {
      const { code } = (await res.json()) as { code: string };
      return code;
    }
    if (res.status !== 404) break;
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  throw new Error(
    `No login code for ${email} (last status ${lastStatus}). One of: ` +
      `(1) the dispatch consumer has not written the challenge yet; ` +
      `(2) this address asked again inside the 60 s cooldown, or more than 3 times in 10 minutes, ` +
      `and such a request gets a challenge id with NO record and no mail, so log in ONCE per ` +
      `address (see helpers/session.ts); ` +
      `(3) a NEW address, and the 20-per-24-h cap on mails to addresses without an account is ` +
      `spent in this Redis (locally: \`docker compose restart redis-volatile\` resets it); ` +
      `(4) the recipient's domain is not reserved, or the API is not running in Development.`
  );
}

/**
 * Logs in through the real UI: address, then the code from the dev seam. For an address that
 * HAS an account (seed it first with `seedTestUser`), so it spends nothing of the
 * global cap on new addresses.
 *
 * ONCE per address. A second login for the same address inside the cooldown can never succeed
 * (see `takeLoginCode`), which is why the data specs log in once per FILE through
 * `helpers/session.ts` instead of once per test.
 */
export async function loginAs(page: Page, runId: number): Promise<void> {
  await page.goto("/logga-in");
  // Playwright resolverar page.goto mot config-baseURL — guard:a efter navigation
  // för att fånga felkonfigurerade baseURL (CI mot prod) innan adressen fylls i.
  assertSafeBaseURL(page.url());
  await page.getByLabel("E-postadress").fill(testEmail(runId));
  // exact: the three provider rows are buttons named "Fortsätt med …" and match loosely.
  await page.getByRole("button", { name: "Fortsätt", exact: true }).click();
  await page.waitForURL("**/logga-in/kod");
  await page.getByLabel("Sexsiffrig kod").fill(await takeLoginCode(testEmail(runId)));
  await page.getByRole("button", { name: "Bekräfta koden" }).click();
  // A login with no `next` lands on /oversikt (`safeRedirectPath`'s default).
  await page.waitForURL("**/oversikt");
}

/**
 * Seeds the test account through the DEV-ONLY seed seam (`POST /api/v1/dev/accounts`, mapped under
 * IsDevelopment() only; ADR 0142 part 5a). The seam opens the account with the writer registration
 * uses, so it is the state a real registration produces, and it spends nothing of the global
 * 20-per-24-h cap on mails to addresses without an account, where seeding every spec's user through
 * the code flow would spend the cap within a couple of local runs (measured, #1738). It hands out no
 * credential: `loginAs` still logs in through the code flow.
 */
export async function seedTestUser(baseURL: string, runId: number): Promise<void> {
  await seedAccount(baseURL, testEmail(runId));
}

/** The same seeding for an address the caller spells itself (it must be on a reserved domain). */
export async function seedAccount(baseURL: string, email: string): Promise<void> {
  assertSafeBaseURL(baseURL);
  const res = await fetch(`${baseURL}/api/v1/dev/accounts`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email }),
  });
  if (res.status === 204) return;
  throw new Error(
    `Could not seed ${email} (status ${res.status}). One of: ` +
      `(404) the address is not on a reserved domain, or the API is not running in Development; ` +
      `(409) the address already has an account the login cannot sign in to (pending deletion, or ` +
      `an Identity row without a profile).`
  );
}

/**
 * Seeds a saved CV straight through the backend API and returns its id.
 *
 * Why this exists (#1061): the edit specs used to seed by driving `/cv/ny`. That route is now
 * a session-gated 404 — create-from-scratch is deferred from the MVP — so the UI can no longer
 * produce the precondition those specs need. `/cv/[id]` itself is still live, so the coverage
 * is kept and only the seeding path moves.
 *
 * ⚠ This helper calls `POST /api/v1/resumes`, which #1371 is slated to REMOVE as an
 * unreachable authenticated endpoint. When that lands, this helper must be re-seeded too —
 * it is named as a consumer in that issue. Do not read its existence as a sign the endpoint
 * is meant to stay.
 *
 * The API takes the session id as a Bearer token (ADR 0018 — the backend is cookie-agnostic;
 * the Next proxy owns the cookie), so the id is read from the logged-in context's cookie.
 */
export async function seedResumeViaApi(
  page: Page,
  baseURL: string,
  name: string,
  fullName: string,
): Promise<string> {
  assertSafeBaseURL(baseURL);

  const sessionId = await sessionIdOf(page);

  const created = await fetch(`${baseURL}/api/v1/resumes`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${sessionId}`,
    },
    body: JSON.stringify({ name, fullName }),
  });
  if (!created.ok) {
    throw new Error(`Failed to seed resume "${name}": ${created.status}`);
  }
  const { id } = (await created.json()) as { id: string };
  return id;
}

/**
 * The session id of the logged-in context, read from its session cookie. A second login for the same
 * address inside the challenge cooldown gets a challenge with no record (see `takeLoginCode`), so the
 * seeding borrows the session the spec already holds instead of opening another.
 */
async function sessionIdOf(page: Page): Promise<string> {
  const cookie = (await page.context().cookies()).find((c) => c.name === SESSION_COOKIE_NAME);
  if (!cookie) throw new Error(`The context holds no ${SESSION_COOKIE_NAME}: log in first (helpers/session.ts).`);
  return cookie.value;
}
