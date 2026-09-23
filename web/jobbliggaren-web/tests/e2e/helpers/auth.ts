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
 * HAS an account (seed it first with `ensureConfirmedTestUser`), so it spends nothing of the
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
 * Registers the test account WITH A PASSWORD, straight through `POST /api/v1/auth/register`, and
 * deliberately not through the login flow's consent step: an address that already has an account
 * costs nothing of the global 20-per-24-h cap on mails to addresses without one, where seeding every
 * spec's user through the code flow would spend the cap within a couple of local runs (measured,
 * #1738).
 *
 * ⚠ Part 5a removes `/auth/register`. This helper must be re-seeded there.
 */
export async function ensureTestUser(baseURL: string, runId: number): Promise<void> {
  await registerAccount(baseURL, testEmail(runId));
}

async function registerAccount(baseURL: string, email: string): Promise<void> {
  assertSafeBaseURL(baseURL);
  const res = await fetch(`${baseURL}/api/v1/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password: TEST_PASSWORD, displayName: "E2E Testare", acceptTerms: true }),
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
  await confirmAccount(baseURL, testEmail(runId));
}

async function confirmAccount(baseURL: string, email: string): Promise<void> {
  assertSafeBaseURL(baseURL);
  const res = await fetch(`${baseURL}/api/v1/dev/confirm-email`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email }),
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
 * The confirm also matters for the code login: an UNCONFIRMED account's first passwordless
 * proof removes its password (ADR 0142 D10), and these accounts need theirs.
 */
export async function ensureConfirmedTestUser(baseURL: string, runId: number): Promise<void> {
  await ensureTestUser(baseURL, runId);
  await confirmTestUser(baseURL, runId);
}

/** The same seeding for an address the caller spells itself (it must be on a reserved domain). */
export async function ensureConfirmedAccount(baseURL: string, email: string): Promise<void> {
  await registerAccount(baseURL, email);
  await confirmAccount(baseURL, email);
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
 * the Next proxy owns the cookie), so this logs in against the backend directly rather than
 * borrowing the browser context's cookie.
 */
// One backend session per run, reused across seeds. NOT a micro-optimisation:
// `/auth/login` sits behind the AuthWrite rate-limit policy (20 per 60s per IP),
// and every `loginAs` in every spec shares that budget from the same IP. Logging
// in once per seed would add one write per seeded CV on top of the per-test UI
// logins — a rate-limit flake this helper would have introduced.
// Keyed on runId + baseURL, not a bare string: `auth.ts` is shared across lanes, and a
// second caller with a different runId would otherwise silently receive the first user's
// session. One caller today; the key costs nothing and removes the trap.
const cachedSessions = new Map<string, string>();

async function seedSession(baseURL: string, runId: number): Promise<string> {
  const key = `${baseURL}|${runId}`;
  const cachedSessionId = cachedSessions.get(key);
  if (cachedSessionId) return cachedSessionId;
  const login = await fetch(`${baseURL}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: testEmail(runId), password: TEST_PASSWORD }),
  });
  if (!login.ok) {
    throw new Error(`Failed to log in test user for seeding: ${login.status}`);
  }
  const { sessionId } = (await login.json()) as { sessionId: string };
  cachedSessions.set(key, sessionId);
  return sessionId;
}

export async function seedResumeViaApi(
  baseURL: string,
  runId: number,
  name: string,
  fullName: string,
): Promise<string> {
  assertSafeBaseURL(baseURL);

  const sessionId = await seedSession(baseURL, runId);

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
