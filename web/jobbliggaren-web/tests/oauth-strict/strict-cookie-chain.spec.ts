import { test, expect, type Page } from "@playwright/test";
import {
  APP_ORIGIN,
  CALLBACK_PATH,
  PROBE_COOKIE,
  REFUSED_EXTERNAL_PATH,
  SESSION_ID,
  CONTROL_CONSENT_URL,
  startHarness,
  type Harness,
  type Recorded,
} from "./servers";

// The premise under ADR 0142 D8's continuation document, measured per engine (#1744): a Strict cookie
// set on a 3xx inside a cross-site chain is not sent on the next hop, and one set on a 200 document
// is sent on that document's own navigation. Branches (ii) and (iii) are the controls: without them a
// same-site harness would make (i) pass as a no-op.

const STATE_COOKIE = "__Host-jobbliggaren_oauth";
const SESSION_COOKIE = "__Host-jobbliggaren_session";
const FLOW_COOKIE = "__Host-jobbliggaren_login";

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});
test.afterAll(async () => {
  await harness.stop();
});
test.beforeEach(() => harness.reset());
test.afterEach(() => {
  expect(requestTo(REFUSED_EXTERNAL_PATH), "a redirect tried to leave the machine").toBeUndefined();
});

const requestTo = (path: string): Recorded | undefined => harness.requests.find((r) => r.path.startsWith(path));

function afterCallback(): Recorded[] {
  const at = harness.requests.findIndex((r) => r.path.startsWith(CALLBACK_PATH));
  return at < 0 ? [] : harness.requests.slice(at + 1);
}

/** From the login page to the provider's consent page, whose button carries `code` back. */
async function startWithGoogle(page: Page, code: string): Promise<void> {
  harness.code = code;
  await page.goto("/logga-in");
  await page.getByRole("link", { name: "Fortsätt med Google" }).click();
  await page.locator("#approve").waitFor();
}

test("(i) the app's callback: the Strict session cookie rides the continuation hop", async ({ page }) => {
  await startWithGoogle(page, "signedIn");
  await page.locator("#approve").click();

  await expect.poll(() => afterCallback().length).toBeGreaterThan(0);
  const callback = requestTo(CALLBACK_PATH)!;
  expect(callback.cookies[STATE_COOKIE], "the Lax state cookie crossed the provider's hop").toBeDefined();
  expect(callback.setCookies.find((c) => c.startsWith(`${SESSION_COOKIE}=${SESSION_ID};`))).toMatch(
    /SameSite=Strict/i
  );
  const hop = afterCallback()[0]!;
  expect(hop.path).toBe("/oversikt");
  expect(hop.cookies[SESSION_COOKIE]).toBe(SESSION_ID);
});

test("(ii) control: in this harness a Strict cookie set on a 302 in the chain is NOT sent on the next hop", async ({
  page,
}) => {
  await page.goto(CONTROL_CONSENT_URL);
  await page.locator("#approve").click();

  await expect.poll(() => requestTo("/__control/landing")).toBeDefined();
  expect(requestTo("/__control/landing")!.cookies[PROBE_COOKIE]).toBeUndefined();
  // Stored all the same: the absence above is the hop's, not a refused cookie.
  harness.reset();
  await page.goto("/__control/landing");
  expect(requestTo("/__control/landing")!.cookies[PROBE_COOKIE]).toBe("1");
});

test("(iii) control: without the provider's hop the same 302 carries it, so the hop is the variable", async ({
  page,
}) => {
  await page.goto("/__control/same-site-page");
  await page.locator("#go").click();

  await expect.poll(() => requestTo("/__control/landing")).toBeDefined();
  expect(requestTo("/__control/landing")!.cookies[PROBE_COOKIE]).toBe("1");
});

test("(iv) mutant: a Strict state cookie never reaches the callback, which refuses", async ({ page, context }) => {
  harness.strictStateCookie = true;
  await startWithGoogle(page, "signedIn");
  expect((await context.cookies()).find((c) => c.name === STATE_COOKIE)?.sameSite).toBe("Strict");
  await page.locator("#approve").click();

  await expect.poll(() => afterCallback().length).toBeGreaterThan(0);
  expect(requestTo(CALLBACK_PATH)!.cookies[STATE_COOKIE]).toBeUndefined();
  expect(harness.callbacks).toEqual([]);
  expect(afterCallback()[0]!.path).toBe("/logga-in");
});

test("(v) consent: the Strict flow cookie rides the hop and the step renders", async ({ page }) => {
  await startWithGoogle(page, "consent");
  await page.locator("#approve").click();

  await expect(page.getByRole("heading", { level: 1, name: "Skapa ditt konto" })).toBeVisible();
  const hop = afterCallback()[0]!;
  expect(hop.path).toBe("/logga-in/villkor");
  expect(hop.cookies[FLOW_COOKIE]).toBeDefined();
});

test("(vi) nothing after the callback carries its code or state in a Referer, nor leaves the app", async ({
  page,
  context,
}) => {
  const elsewhere: string[] = [];
  let callbackSeen = false;
  context.on("request", (request) => {
    if (request.url().startsWith(`${APP_ORIGIN}${CALLBACK_PATH}`)) callbackSeen = true;
    else if (callbackSeen && !request.url().startsWith(APP_ORIGIN)) elsewhere.push(request.url());
  });
  await startWithGoogle(page, "signedIn");
  await page.locator("#approve").click();
  await expect.poll(() => afterCallback().length).toBeGreaterThan(0);
  await page.waitForLoadState("load");

  const state = requestTo(CALLBACK_PATH)!.cookies[STATE_COOKIE]!;
  for (const request of afterCallback()) {
    expect(request.referer ?? "", request.path).not.toContain("code=");
    expect(request.referer ?? "", request.path).not.toContain(state);
  }
  expect(elsewhere).toEqual([]);
});
