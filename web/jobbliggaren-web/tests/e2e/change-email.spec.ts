import { test, expect, type BrowserContext, type Page } from "@playwright/test";
import { ensureConfirmedTestUser, loginAs, takeLoginCode, testEmail } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const SESSION_COOKIE = "__Host-jobbliggaren_session";
const REFRESH_AFTER_COOKIE = "__Host-jobbliggaren_refresh_after";

/**
 * Changing the address on /mina-sidor by two codes (#1740, ADR 0142 D5): the dialog re-authenticates
 * with a code to the current address, and the card takes the code mailed to the new one. The confirm
 * ends every session of the account and re-issues this device's, so what the browser holds afterwards
 * is measured against the backend, not inferred from the page.
 */

const newRunId = () => Date.now() + Math.floor(Math.random() * 1_000_000);
const newAddress = (runId: number) => `test-e2e-${runId}-ny@e2e.jobbliggaren.test`;

/** A six-digit code that is certainly not `code`. */
const otherThan = (code: string) => String((Number(code) + 1) % 1_000_000).padStart(6, "0");

async function sessionOf(context: BrowserContext) {
  return (await context.cookies()).find((c) => c.name === SESSION_COOKIE)?.value;
}

/** The address a session belongs to in the backend, or the status when it belongs to none. */
async function addressOf(session: string | undefined) {
  const res = await fetch(`${BACKEND_URL}/api/v1/me`, {
    headers: { Authorization: `Bearer ${session}` },
  });
  return res.ok ? ((await res.json()) as { email: string }).email : res.status;
}

/** Types the new address, re-authenticates in the dialog, and returns the card's code field. */
async function toCodeStep(page: Page, runId: number) {
  await page.goto("/mina-sidor");
  await page.getByLabel("Ny e-postadress").fill(newAddress(runId));
  await page.getByRole("button", { name: "Fortsätt" }).click();
  const dialog = page.getByRole("dialog", { name: "Byt e-postadress" });
  await dialog.getByRole("button", { name: "Skicka kod" }).click();
  await dialog.getByLabel("Sexsiffrig kod").fill(await takeLoginCode(testEmail(runId)));
  await dialog.getByRole("button", { name: "Bekräfta koden" }).click();
  const field = page.getByLabel("Kod till den nya adressen");
  await expect(field).toBeFocused();
  return field;
}

test.describe("Byt e-postadress (/mina-sidor)", () => {
  test("the retired /installningar answers a permanent redirect straight to /mina-sidor", async ({
    request,
  }) => {
    const res = await request.get("/installningar", { maxRedirects: 0 });

    expect(res.status()).toBe(308);
    expect(res.headers()["location"]).toBe("/mina-sidor");
  });

  test("changes the address on two codes, and this device stays logged in on a new session", async ({
    page,
  }) => {
    const runId = newRunId();
    await ensureConfirmedTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);
    const before = await sessionOf(page.context());

    const field = await toCodeStep(page, runId);
    await expect(
      page.getByText(`Vi har skickat en sexsiffrig kod till ${newAddress(runId)}.`, { exact: false })
    ).toBeVisible();
    await field.fill(await takeLoginCode(newAddress(runId)));
    await page.getByRole("button", { name: "Byt adress" }).click();

    await expect(
      page.getByText(`Adressen är bytt, och du loggar in med ${newAddress(runId)} från och med nu.`, {
        exact: false,
      })
    ).toBeFocused();
    // No reload: the action re-set the session cookie, and Next re-rendered the page on it.
    await expect(page.getByText(`Din e-postadress är ${newAddress(runId)}.`)).toBeVisible();
    await page.getByRole("button", { name: "Mina sidor" }).click();
    await expect(page.getByRole("dialog", { name: "Mina sidor" })).toContainText(newAddress(runId));

    const after = await sessionOf(page.context());
    expect(after).not.toBe(before);
    expect(await addressOf(before)).toBe(401);
    expect(await addressOf(after)).toBe(newAddress(runId));
    await page.reload();
    await expect(page).toHaveURL(/\/mina-sidor$/);
    await expect(page.getByText(`Din e-postadress är ${newAddress(runId)}.`)).toBeVisible();
  });

  // security-auditor, #1740 Minor 2: the proxy may rotate the session on the confirm's own request, and
  // its Set-Cookie then competes with the re-issued one. A rotation needs a backend whose
  // `Session__Persistent__RotationInterval` is short, and the environment declares it in seconds with
  // E2E_ROTATION_INTERVAL_SECONDS; there the premise is measured, and a missing rotation fails the test.
  // The interval must outlast the login: a shorter one rotates the fresh session on the login's own
  // redirect, where the browser never receives the new id (measured with 1 ms, 2026-09-23).
  test("keeps the re-issued session when the proxy rotates on the confirm, and every other id sent is dead", async ({
    page,
  }) => {
    const interval = Number(process.env.E2E_ROTATION_INTERVAL_SECONDS);
    test.skip(!interval, "The backend does not force a session rotation here.");
    test.setTimeout(60_000 + 3 * interval * 1_000);
    const untilRotationIsDue = (since: number) =>
      page.waitForTimeout(Math.max(0, since + (interval + 1) * 1_000 - Date.now()));
    const runId = newRunId();
    await ensureConfirmedTestUser(BACKEND_URL, runId);
    await loginAs(page, runId);
    const loggedInAt = Date.now();
    const context = page.context();
    const atLogin = await sessionOf(context);

    const field = await toCodeStep(page, runId);
    const code = await takeLoginCode(newAddress(runId));

    // A wrong code is a request of the same action whose response sets no cookie, so a rotation the
    // proxy makes on it reaches the browser unmasked. On a slow run the walk to the code step may
    // itself have rotated, which restarts the interval.
    await untilRotationIsDue((await sessionOf(context)) === atLogin ? loggedInAt : Date.now());
    await context.clearCookies({ name: REFRESH_AFTER_COOKIE });
    const beforeWrong = await sessionOf(context);
    await field.fill(otherThan(code));
    await page.getByRole("button", { name: "Byt adress" }).click();
    await expect(
      page.getByRole("alert").filter({ hasText: "Använd koden från mejlet till den nya adressen" })
    ).toBeVisible();
    const afterWrong = await sessionOf(context);
    const rotatedAt = Date.now();
    expect(afterWrong).not.toBe(beforeWrong);

    await untilRotationIsDue(rotatedAt);
    await context.clearCookies({ name: REFRESH_AFTER_COOKIE });
    const confirm = page.waitForResponse(
      (res) => res.request().method() === "POST" && res.request().headers()["next-action"] !== undefined
    );
    await field.fill(code);
    await page.getByRole("button", { name: "Byt adress" }).click();
    const sent = (await (await confirm).headersArray())
      .filter(
        (header) =>
          header.name.toLowerCase() === "set-cookie" && header.value.startsWith(`${SESSION_COOKIE}=`)
      )
      .map((header) => header.value.slice(SESSION_COOKIE.length + 1).split(";")[0]);

    // Next 16.3.3 sends the proxy's rotated id AND the re-issued one, in that order, so the rotation
    // on this request is visible here; the browser keeps the last.
    expect(sent.length).toBeGreaterThan(1);
    const kept = await sessionOf(context);
    expect(sent.at(-1)).toBe(kept);
    expect(await addressOf(kept)).toBe(newAddress(runId));
    for (const id of [...sent.slice(0, -1), afterWrong]) {
      expect(await addressOf(id)).toBe(401);
    }
    await page.goto("/mina-sidor");
    await expect(page.getByText(`Din e-postadress är ${newAddress(runId)}.`)).toBeVisible();
  });
});
