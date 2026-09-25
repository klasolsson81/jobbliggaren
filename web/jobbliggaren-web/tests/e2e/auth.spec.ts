import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { test, expect, type Page } from "@playwright/test";
import {
  seedAccount,
  seedTestUser,
  takeLoginCode,
  testEmail,
} from "./helpers/auth";

/**
 * #1738 — the one auth page: an address, a mailed code, and for a new address the terms.
 *
 * Runs on the DEV seam: `POST /api/v1/dev/login-code` hands back the code the mail carried, for a
 * reserved recipient, in Development only. So the API must run in Development with registration
 * open (its Development default).
 *
 * ONE ADDRESS PER TEST. The login challenge's budgets are constants and silent: the same address
 * again inside 60 seconds gets a challenge with no record. And exactly ONE test here creates an
 * account through the flow, because every new address spends a slot of the global 20-per-24-hour
 * cap on mails to addresses without an account. The other tests seed their account through the
 * API first (`helpers/auth.ts` says why).
 *
 * Not covered here, and it cannot be: a login link that WORKS. The seam hands out the code only,
 * never the link's token. `challenge-actions.test.ts` covers that arm; this spec covers the
 * landing itself with a token that is dead.
 *
 * Copy is duplicated from `messages/sv/pages.json` on purpose. This job is observe-only
 * (e2e.yml), so a stale string here fails silently; keep them in step.
 */
const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";

const uniqueRunId = (): number => Date.now() + Math.floor(Math.random() * 1_000_000);

/**
 * A page on ANOTHER site carrying a link, as webmail does. `127.0.0.1` is not the same site as the
 * app's `localhost`, so a click on the link is a cross-site navigation.
 */
async function serveMailWithLink(href: string): Promise<{ url: string; close: () => Promise<void> }> {
  const server = createServer((_request, response) => {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end(`<a href="${href}">Logga in via mejlet</a>`);
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return {
    url: `http://127.0.0.1:${port}/`,
    close: () => new Promise<void>((resolve) => void server.close(() => resolve())),
  };
}

async function submitAddress(page: Page, email: string, path = "/logga-in"): Promise<void> {
  await page.goto(path);
  await page.getByLabel("E-postadress").fill(email);
  // exact: every provider row is named "Fortsätt med …" and matches loosely.
  await page.getByRole("button", { name: "Fortsätt", exact: true }).click();
  await page.waitForURL("**/logga-in/kod");
}

async function submitCode(page: Page, code: string): Promise<void> {
  await page.getByLabel("Sexsiffrig kod").fill(code);
  await page.getByRole("button", { name: "Bekräfta koden" }).click();
}

test.describe("/logga-in — an address that has an account", () => {
  test("logs in in two steps, with a session that lasts, and the typed address leaves the device", async ({
    page,
    context,
    baseURL,
  }) => {
    const runId = uniqueRunId();
    await seedTestUser(BACKEND_URL, runId);

    await submitAddress(page, testEmail(runId));

    // The step rests on an instruction, never on a claim that a mail was sent or to whom.
    await expect(page.getByRole("heading", { level: 1, name: "Ange koden" })).toBeVisible();
    await expect(page.getByText(`Du angav ${testEmail(runId)}.`)).toBeVisible();
    await expect(page.getByText(/Vi har skickat/)).toHaveCount(0);
    // A resend right now would kill the code just mailed, so the button starts in cooldown.
    await expect(page.getByRole("button", { name: "Skicka ny kod" })).toBeDisabled();

    await submitCode(page, await takeLoginCode(testEmail(runId)));
    await page.waitForURL("**/oversikt");

    const cookies = await context.cookies();
    const session = cookies.find((c) => c.name === "__Host-jobbliggaren_session");
    expect(session).toBeDefined();
    // Persistent by default (ADR 0142 D4): Max-Age 15552000 s = 180 days, never a session cookie.
    const lifetimeSeconds = session!.expires - Date.now() / 1000;
    expect(lifetimeSeconds).toBeGreaterThan(15_552_000 - 120);
    expect(lifetimeSeconds).toBeLessThanOrEqual(15_552_000);
    expect(session).toMatchObject({ httpOnly: true, secure: true, sameSite: "Strict" });
    // The flow cookie carried the typed address. It goes in the same action as the login.
    expect(cookies.find((c) => c.name === "__Host-jobbliggaren_login")).toBeUndefined();

    // Logged in, a login link asks before it replaces the session: two controls, no address.
    await page.goto("/logga-in/lank?token=not-a-live-token");
    await expect(page.getByRole("heading", { level: 1, name: "Du är redan inloggad" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Fortsätt och logga in" })).toBeVisible();
    await page.getByRole("link", { name: "Stanna kvar som inloggad" }).click();
    await page.waitForURL("**/oversikt");

    // The same link CLICKED ON ANOTHER SITE. The Strict session cookie is not sent with that
    // navigation, so the GET cannot see the session and renders the one-button arm. The press is
    // same-site: it must ask, consume nothing and leave the session as it was.
    const mail = await serveMailWithLink(`${baseURL}/logga-in/lank?token=not-a-live-token`);
    try {
      await page.goto(mail.url);
      await page.getByRole("link", { name: "Logga in via mejlet" }).click();
      await expect(
        page.getByRole("heading", { level: 1, name: "Logga in på Jobbliggaren" })
      ).toBeVisible();

      await page.getByRole("button", { name: "Logga in", exact: true }).click();

      await expect(page.getByRole("heading", { level: 1, name: "Du är redan inloggad" })).toBeFocused();
      await expect(page.getByRole("button", { name: "Fortsätt och logga in" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Stanna kvar som inloggad" })).toBeVisible();
      const after = (await context.cookies()).find((c) => c.name === "__Host-jobbliggaren_session");
      expect(after?.value).toBe(session!.value);
    } finally {
      await mail.close();
    }
  });

  test("honours next, and answers a wrong code on the field without losing the step", async ({
    page,
  }) => {
    const runId = uniqueRunId();
    await seedTestUser(BACKEND_URL, runId);

    await submitAddress(page, testEmail(runId), "/logga-in?next=%2Fcv");
    const code = await takeLoginCode(testEmail(runId));
    const wrong = code === "000000" ? "000001" : "000000";

    await submitCode(page, wrong);
    const alert = page.getByRole("alert").filter({ hasText: "Koden stämmer inte." });
    await expect(alert).toBeVisible();
    await expect(page.getByLabel("Sexsiffrig kod")).toBeFocused();
    await expect(page).toHaveURL(/\/logga-in\/kod$/);

    await submitCode(page, code);
    await page.waitForURL("**/cv");
  });

  test("takes björn@ all the way in: the browser does not refuse what the backend admits", async ({
    page,
  }) => {
    // Seeded as an existing account, so it spends nothing of the cap on new addresses.
    const email = `björn-${uniqueRunId()}@e2e.jobbliggaren.test`;
    await seedAccount(BACKEND_URL, email);

    // With native validation on, Chromium stops this submit before it is sent: the HTML email
    // production is ASCII-only in the local part. The form is `noValidate` for that reason.
    await submitAddress(page, email);
    await submitCode(page, await takeLoginCode(email));
    await page.waitForURL("**/oversikt");
  });
});

test.describe("/logga-in — a new address", () => {
  test("creates the account in three steps: address, code, terms", async ({ page }) => {
    // THE test that spends a slot of the global cap on new addresses. Keep it the only one.
    const email = `new-${uniqueRunId()}@e2e.jobbliggaren.test`;

    await submitAddress(page, email);
    await submitCode(page, await takeLoginCode(email));
    await page.waitForURL("**/logga-in/villkor");

    await expect(page.getByRole("heading", { level: 1, name: "Skapa ditt konto" })).toBeVisible();
    // No address on this step: the account is created on what the code proved.
    await expect(page.getByText(email)).toHaveCount(0);

    // The Server Action refuses an unticked box; the grant survives it.
    await page.getByRole("button", { name: "Skapa konto" }).click();
    await expect(
      page.getByRole("alert").filter({ hasText: "Du behöver godkänna användarvillkoren" })
    ).toBeVisible();

    await page.getByRole("checkbox", { name: "Jag godkänner användarvillkoren." }).check();
    await page.getByRole("button", { name: "Skapa konto" }).click();
    await page.waitForURL("**/oversikt");
  });
});

test.describe("/registrera", () => {
  test("answers 308 to /logga-in and keeps the query", async ({ request }) => {
    const res = await request.get("/registrera?next=%2Fcv", { maxRedirects: 0 });

    expect(res.status()).toBe(308);
    expect(res.headers()["location"]).toBe("/logga-in?next=%2Fcv");
  });
});

test.describe("/logga-in/lank", () => {
  test("consumes nothing on the GET, and says one sentence for a link that cannot be used", async ({
    page,
  }) => {
    await page.goto("/logga-in/lank?token=not-a-live-token");

    await expect(
      page.getByRole("heading", { level: 1, name: "Logga in på Jobbliggaren" })
    ).toBeVisible();
    await page.getByRole("button", { name: "Logga in" }).click();

    await expect(
      page.getByText("Länken går inte att använda. Begär en ny kod på inloggningssidan.")
    ).toBeVisible();
    // A dead link has no retry: the button, and the token with it, are gone.
    await expect(page.getByRole("button", { name: "Logga in" })).toHaveCount(0);
    await expect(page.locator('input[name="token"]')).toHaveCount(0);
  });

  test("works with JavaScript OFF: the form POST reaches the Server Action", async ({ browser }) => {
    // The measurement behind `LOGIN_LINK_REFERRER_POLICY`. A no-JS form POST is a navigate-mode
    // request. Under `Referrer-Policy: no-referrer` it carries `Origin: null`, and Next refuses a
    // Server Action whose Origin does not match the host, so the page would answer with Next's
    // error instead of the sentence below. It is `same-origin` so that this passes.
    const context = await browser.newContext({ javaScriptEnabled: false });
    const page = await context.newPage();
    try {
      await page.goto("/logga-in/lank?token=not-a-live-token");
      await page.getByRole("button", { name: "Logga in" }).click();

      await expect(
        page.getByText("Länken går inte att använda. Begär en ny kod på inloggningssidan.")
      ).toBeVisible();
    } finally {
      await context.close();
    }
  });
});
