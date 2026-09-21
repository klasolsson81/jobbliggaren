import { beforeEach, describe, expect, it, vi } from "vitest";
import type { LoginFlow } from "./login-flow";

const NOW = 1_800_000_000;

const mocks = vi.hoisted(() => ({
  redirect: vi.fn((path: string) => {
    throw new Error(`REDIRECT:${path}`);
  }),
  setSessionCookie: vi.fn(),
  readLoginFlow: vi.fn(),
  writeLoginFlow: vi.fn(),
  clearLoginFlow: vi.fn(),
  fetch: vi.fn(),
}));

vi.mock("next/navigation", () => ({ redirect: mocks.redirect }));
vi.mock("next-intl/server", () => ({ getTranslations: async () => (key: string) => key }));
vi.mock("@/lib/auth/session", () => ({ setSessionCookie: mocks.setSessionCookie }));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("@/lib/http/forwarded-headers", () => ({
  forwardedHeaders: async () => ({ "x-forwarded-for": "203.0.113.7" }),
}));
vi.mock("@/lib/auth/login-flow-cookie", () => ({
  readLoginFlow: mocks.readLoginFlow,
  writeLoginFlow: mocks.writeLoginFlow,
  clearLoginFlow: mocks.clearLoginFlow,
  nowEpochSeconds: () => NOW,
}));

import {
  changeEmail,
  completeRegistration,
  consumeLink,
  requestCode,
  resendCode,
  verifyCode,
} from "./challenge-actions";

const K = "auth.passwordless";

const liveCode: Extract<LoginFlow, { phase: "code" }> = {
  phase: "code",
  challengeId: "challenge-1",
  email: "anna@example.com",
  next: "/ansokningar",
  sentAt: NOW - 120,
};
const consent: LoginFlow = { phase: "consent", grantToken: "grant-1", next: "/cv" };

const form = (fields: Record<string, string>): FormData => {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) data.set(name, value);
  return data;
};

const json = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
const problem = (status: number, title: string): Response =>
  json(status, { type: "about:blank", title, status, detail: "Backend text, never rendered." });

/** Runs an action and says how it ended: with a state, or by redirecting. */
async function run<T>(action: () => Promise<T>): Promise<{ state?: T; redirectedTo?: string }> {
  try {
    return { state: await action() };
  } catch (error) {
    const match = /^REDIRECT:(.*)$/.exec((error as Error).message);
    if (!match) throw error;
    return { redirectedTo: match[1] };
  }
}

const sentBody = (): unknown => JSON.parse(mocks.fetch.mock.lastCall?.[1]?.body as string);

beforeEach(() => {
  for (const mock of Object.values(mocks)) mock.mockClear();
  mocks.readLoginFlow.mockResolvedValue(null);
  mocks.fetch.mockReset();
  vi.stubGlobal("fetch", mocks.fetch);
});

describe("requestCode", () => {
  it("mints, stores the code phase and moves to the code step", async () => {
    mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

    const result = await run(() =>
      requestCode(null, form({ email: "  anna@example.com ", next: "/ansokningar/abc" }))
    );

    expect(result.redirectedTo).toBe("/logga-in/kod");
    expect(mocks.fetch).toHaveBeenCalledWith(
      "http://backend.test/api/v1/auth/challenge",
      expect.objectContaining({
        method: "POST",
        cache: "no-store",
        headers: { "x-forwarded-for": "203.0.113.7", "Content-Type": "application/json" },
      })
    );
    expect(sentBody()).toEqual({ email: "anna@example.com" });
    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({
      phase: "code",
      challengeId: "challenge-2",
      email: "anna@example.com",
      next: "/ansokningar/abc",
      sentAt: NOW,
    });
  });

  it("stores the start page for a next that would leave the site", async () => {
    mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

    await run(() => requestCode(null, form({ email: "anna@example.com", next: "//evil.example" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledWith(expect.objectContaining({ next: "/oversikt" }));
  });

  it("sends an address outside ASCII to the backend unchanged: what an address is, is not decided here", async () => {
    mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

    await run(() => requestCode(null, form({ email: "björn@example.se" })));

    expect(sentBody()).toEqual({ email: "björn@example.se" });
  });

  it("refuses an empty address without calling the backend", async () => {
    const result = await run(() => requestCode(null, form({ email: "   " })));

    expect(result.state).toEqual({
      error: `${K}.entry.emailRequired`,
      channel: "field",
      values: { email: "   " },
    });
    expect(mocks.fetch).not.toHaveBeenCalled();
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  // Both arms below redirect to the code step, so the redirect cannot tell them apart. The
  // observables are the fetch and the write.
  describe("the same address while its code is still live", () => {
    it("mints nothing and leaves the cookie exactly as it is", async () => {
      mocks.readLoginFlow.mockResolvedValue(liveCode);

      const result = await run(() => requestCode(null, form({ email: " anna@example.com " })));

      expect(result.redirectedTo).toBe("/logga-in/kod");
      expect(mocks.fetch).not.toHaveBeenCalled();
      expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
    });

    it("does NOT fold case: another spelling mints, because the backend's fold is not mirrored here", async () => {
      mocks.readLoginFlow.mockResolvedValue(liveCode);
      mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

      await run(() => requestCode(null, form({ email: "Anna@example.com" })));

      expect(mocks.fetch).toHaveBeenCalledTimes(1);
      expect(mocks.writeLoginFlow).toHaveBeenCalledWith(
        expect.objectContaining({ challengeId: "challenge-2", email: "Anna@example.com" })
      );
    });

    it.each<[string, LoginFlow | null]>([
      ["another address", { ...liveCode, email: "bo@example.com" }],
      ["a code already dead", { ...liveCode, dead: "expired" }],
      ["a consent phase", consent],
      ["no cookie", null],
    ])("mints for %s", async (_label, flow) => {
      mocks.readLoginFlow.mockResolvedValue(flow);
      mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

      await run(() => requestCode(null, form({ email: "anna@example.com" })));

      expect(mocks.fetch).toHaveBeenCalledTimes(1);
      expect(mocks.writeLoginFlow).toHaveBeenCalledWith(
        expect.objectContaining({ challengeId: "challenge-2" })
      );
    });
  });

  it.each<[string, () => Response | Promise<never>, string, string]>([
    ["a 400", () => json(400, { errors: { Email: ["Ogiltig."] } }), "field", `${K}.entry.emailRequired`],
    ["a 429", () => json(429, {}), "status", `${K}.errors.tooManyAttempts`],
    ["the mail-delivery 503", () => problem(503, "Auth.EmailDeliveryUnavailable"), "status", `${K}.errors.unavailable`],
    ["the store's 503, which has no title", () => json(503, { error: "Tjänsten är inte tillgänglig." }), "status", `${K}.errors.unavailable`],
    ["a 202 whose body is not the contract", () => json(202, { id: "x" }), "status", `${K}.errors.unavailable`],
    ["a transport failure", () => Promise.reject(new Error("ECONNREFUSED")), "status", `${K}.errors.unavailable`],
  ])("answers %s in the right channel and writes no cookie", async (_label, respond, channel, error) => {
    mocks.fetch.mockImplementation(async () => respond());

    const result = await run(() => requestCode(null, form({ email: "anna@example.com" })));

    expect(result.state).toEqual({ error, channel, values: { email: "anna@example.com" } });
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });
});

describe("verifyCode", () => {
  beforeEach(() => mocks.readLoginFlow.mockResolvedValue(liveCode));

  it("sends the cookie's challenge id and the typed code, and nothing else", async () => {
    mocks.fetch.mockResolvedValue(json(200, { outcome: "accountUnavailable" }));

    await run(() => verifyCode(null, form({ code: " 123456 ", challengeId: "forged", email: "x@y.z" })));

    expect(mocks.fetch.mock.lastCall?.[0]).toBe("http://backend.test/api/v1/auth/challenge/verify");
    expect(sentBody()).toEqual({ challengeId: "challenge-1", code: "123456" });
  });

  it("logs in: a persistent session, the flow cookie gone, and the cookie's next", async () => {
    mocks.fetch.mockResolvedValue(json(200, { outcome: "signedIn", sessionId: "session-1" }));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(mocks.setSessionCookie).toHaveBeenCalledExactlyOnceWith("session-1", true);
    expect(mocks.clearLoginFlow).toHaveBeenCalledTimes(1);
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
    expect(result.redirectedTo).toBe("/ansokningar");
  });

  it("re-validates next when it is READ: the cookie is unsigned and editable", async () => {
    mocks.readLoginFlow.mockResolvedValue({ ...liveCode, next: "//evil.example" });
    mocks.fetch.mockResolvedValue(json(200, { outcome: "signedIn", sessionId: "session-1" }));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(result.redirectedTo).toBe("/oversikt");
  });

  it("moves a new address to the consent step with the grant and WITHOUT the address", async () => {
    mocks.fetch.mockResolvedValue(json(200, { outcome: "consentRequired", grantToken: "grant-9" }));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({
      phase: "consent",
      grantToken: "grant-9",
      next: "/ansokningar",
    });
    expect(mocks.setSessionCookie).not.toHaveBeenCalled();
    expect(result.redirectedTo).toBe("/logga-in/villkor");
  });

  it.each([
    [{ outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" }],
    [{ outcome: "registrationClosed" }],
    [{ outcome: "accountUnavailable" }],
  ])("stores the terminal outcome %o with no address and stays on the code step", async (body) => {
    mocks.fetch.mockResolvedValue(json(200, body));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ phase: "outcome", result: body });
    expect(mocks.setSessionCookie).not.toHaveBeenCalled();
    expect(result.redirectedTo).toBe("/logga-in/kod");
  });

  it.each<[string, string, "burned" | "expired"]>([
    ["burned", "Auth.LoginCodeBurned", "burned"],
    ["expired, used, replaced or never written", "Auth.LoginCodeExpired", "expired"],
    ["an unknown 410", "Auth.SomethingNew", "expired"],
  ])("marks the code dead when it is %s, so a reload shows the same panel", async (_label, title, dead) => {
    mocks.fetch.mockResolvedValue(problem(410, title));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ ...liveCode, dead });
    expect(result.redirectedTo).toBe("/logga-in/kod");
  });

  it("answers a wrong code in the field channel and leaves the cookie alone", async () => {
    mocks.fetch.mockResolvedValue(problem(400, "Auth.LoginCodeWrong"));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(result.state).toEqual({ error: `${K}.code.wrongCode`, channel: "field" });
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it("warns before the last attempt", async () => {
    mocks.fetch.mockResolvedValue(problem(400, "Auth.LoginCodeWrongLastAttempt"));

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(result.state).toEqual({ error: `${K}.code.wrongCode`, channel: "field", lastAttempt: true });
  });

  it.each(["12345", "1234567", "12345a", ""])(
    "refuses the malformed code %j without spending an attempt",
    async (code) => {
      const result = await run(() => verifyCode(null, form({ code })));

      expect(result.state).toEqual({ error: `${K}.code.wrongCode`, channel: "field" });
      expect(mocks.fetch).not.toHaveBeenCalled();
    }
  );

  it("sends a code submitted after the cookie ran out to the entry page with a notice", async () => {
    mocks.readLoginFlow.mockResolvedValue(null);

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ phase: "notice", notice: "codeExpired" });
    expect(result.redirectedTo).toBe("/logga-in");
    expect(mocks.fetch).not.toHaveBeenCalled();
  });

  it.each<[string, LoginFlow]>([
    ["a dead code", { ...liveCode, dead: "burned" }],
    ["another phase", consent],
  ])("never verifies against %s", async (_label, flow) => {
    mocks.readLoginFlow.mockResolvedValue(flow);

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(result.redirectedTo).toBe("/logga-in/kod");
    expect(mocks.fetch).not.toHaveBeenCalled();
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it.each<[string, () => Response | Promise<never>, string]>([
    ["a 429", () => json(429, {}), `${K}.errors.tooManyAttempts`],
    ["a 503", () => json(503, { error: "Tjänsten är inte tillgänglig." }), `${K}.errors.unavailable`],
    ["a 200 that is not the contract", () => json(200, { sessionId: "session-1" }), `${K}.errors.unavailable`],
    ["a transport failure", () => Promise.reject(new Error("ECONNREFUSED")), `${K}.errors.unavailable`],
  ])("answers %s in the status channel, with no session and no cookie write", async (_label, respond, error) => {
    mocks.fetch.mockImplementation(async () => respond());

    const result = await run(() => verifyCode(null, form({ code: "123456" })));

    expect(result.state).toEqual({ error, channel: "status" });
    expect(mocks.setSessionCookie).not.toHaveBeenCalled();
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });
});

describe("resendCode", () => {
  it("writes a fresh code phase and RETURNS: the one state return that writes the cookie", async () => {
    mocks.readLoginFlow.mockResolvedValue({ ...liveCode, dead: "burned" });
    mocks.fetch.mockResolvedValue(json(202, { challengeId: "challenge-2" }));

    const result = await run(() => resendCode());

    expect(result).toEqual({ state: { status: "sent" } });
    expect(sentBody()).toEqual({ email: "anna@example.com" });
    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({
      phase: "code",
      challengeId: "challenge-2",
      email: "anna@example.com",
      next: "/ansokningar",
      sentAt: NOW,
    });
  });

  it.each<[string, LoginFlow]>([
    ["a live code", { ...liveCode, sentAt: NOW - 59 }],
    ["a burned code", { ...liveCode, sentAt: NOW - 10, dead: "burned" }],
  ])("refuses inside the cooldown for %s, without calling the backend", async (_label, flow) => {
    mocks.readLoginFlow.mockResolvedValue(flow);

    const result = await run(() => resendCode());

    expect(result).toEqual({ state: { status: "cooling" } });
    expect(mocks.fetch).not.toHaveBeenCalled();
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it.each<[string, () => Response | Promise<never>, string]>([
    ["a 429", () => json(429, {}), `${K}.errors.tooManyAttempts`],
    ["a 503", () => problem(503, "Auth.EmailDeliveryUnavailable"), `${K}.errors.unavailable`],
    ["a transport failure", () => Promise.reject(new Error("ECONNREFUSED")), `${K}.errors.unavailable`],
  ])("keeps the cookie on %s, so the code already mailed stays verifiable", async (_label, respond, error) => {
    mocks.readLoginFlow.mockResolvedValue(liveCode);
    mocks.fetch.mockImplementation(async () => respond());

    const result = await run(() => resendCode());

    expect(result).toEqual({ state: { status: "error", error } });
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it("has nothing to resend to once the cookie is gone", async () => {
    const result = await run(() => resendCode());

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ phase: "notice", notice: "codeExpired" });
    expect(result.redirectedTo).toBe("/logga-in");
    expect(mocks.fetch).not.toHaveBeenCalled();
  });
});

describe("changeEmail", () => {
  it("clears the typed address and returns to the entry page", async () => {
    const result = await run(() => changeEmail());

    expect(mocks.clearLoginFlow).toHaveBeenCalledTimes(1);
    expect(result.redirectedTo).toBe("/logga-in");
  });
});

describe("completeRegistration", () => {
  beforeEach(() => mocks.readLoginFlow.mockResolvedValue(consent));

  it("takes the grant from the cookie only, and logs the new account in", async () => {
    mocks.fetch.mockResolvedValue(json(200, { outcome: "signedIn", sessionId: "session-2" }));

    const result = await run(() =>
      completeRegistration(null, form({ acceptTerms: "on", grantToken: "forged" }))
    );

    expect(mocks.fetch.mock.lastCall?.[0]).toBe("http://backend.test/api/v1/auth/challenge/complete");
    expect(sentBody()).toEqual({ grantToken: "grant-1", acceptTerms: true });
    expect(mocks.setSessionCookie).toHaveBeenCalledExactlyOnceWith("session-2", true);
    expect(mocks.clearLoginFlow).toHaveBeenCalledTimes(1);
    expect(result.redirectedTo).toBe("/cv");
  });

  it("refuses an unticked box before the fetch, so no account is asked for without the acceptance", async () => {
    const result = await run(() => completeRegistration(null, form({})));

    expect(result.state).toEqual({ error: `${K}.consent.termsRequired`, channel: "field" });
    expect(mocks.fetch).not.toHaveBeenCalled();
  });

  it.each(["AcceptTerms", "acceptTerms"])(
    "reads the backend's refusal under the key %s and keeps the grant",
    async (key) => {
      mocks.fetch.mockResolvedValue(json(400, { errors: { [key]: ["Du måste godkänna."] } }));

      const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

      expect(result.state).toEqual({ error: `${K}.consent.termsRequired`, channel: "field" });
      expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
      expect(mocks.clearLoginFlow).not.toHaveBeenCalled();
    }
  );

  it("sends an unusable grant back to the entry page with its notice", async () => {
    mocks.fetch.mockResolvedValue(problem(410, "Auth.LoginGrantUnusable"));

    const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ phase: "notice", notice: "grantUnusable" });
    expect(result.redirectedTo).toBe("/logga-in");
  });

  it("treats a consent cookie that ran out like an unusable grant: they live equally long", async () => {
    mocks.readLoginFlow.mockResolvedValue(null);

    const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({ phase: "notice", notice: "grantUnusable" });
    expect(result.redirectedTo).toBe("/logga-in");
    expect(mocks.fetch).not.toHaveBeenCalled();
  });

  // Three 503 bodies reach this route. Only one of them is a terminal outcome.
  it("reads closed registration off the TITLE, as an outcome", async () => {
    mocks.fetch.mockResolvedValue(problem(503, "Auth.RegistrationsClosed"));

    const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({
      phase: "outcome",
      result: { outcome: "registrationClosed" },
    });
    expect(result.redirectedTo).toBe("/logga-in/villkor");
  });

  it.each<[string, Response]>([
    ["the store's 503, which has no title", json(503, { error: "Tjänsten är inte tillgänglig." })],
    ["a 503 with another title", problem(503, "Auth.EmailDeliveryUnavailable")],
    ["a 503 that is not JSON", new Response("<html>502</html>", { status: 503 })],
  ])("never says registration is closed for %s", async (_label, response) => {
    mocks.fetch.mockResolvedValue(response);

    const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

    expect(result.state).toEqual({ error: `${K}.errors.unavailable`, channel: "status" });
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it("stores a terminal outcome and stays on the consent step", async () => {
    mocks.fetch.mockResolvedValue(json(200, { outcome: "accountUnavailable" }));

    const result = await run(() => completeRegistration(null, form({ acceptTerms: "on" })));

    expect(mocks.writeLoginFlow).toHaveBeenCalledExactlyOnceWith({
      phase: "outcome",
      result: { outcome: "accountUnavailable" },
    });
    expect(result.redirectedTo).toBe("/logga-in/villkor");
  });
});

describe("consumeLink", () => {
  it("reads the token from the form body and logs in to a FIXED path", async () => {
    mocks.readLoginFlow.mockResolvedValue(liveCode);
    mocks.fetch.mockResolvedValue(json(200, { outcome: "signedIn", sessionId: "session-3" }));

    const result = await run(() => consumeLink(null, form({ token: " link-token ", next: "/cv" })));

    expect(mocks.fetch.mock.lastCall?.[0]).toBe("http://backend.test/api/v1/auth/link");
    expect(sentBody()).toEqual({ token: "link-token" });
    expect(mocks.setSessionCookie).toHaveBeenCalledExactlyOnceWith("session-3", true);
    // A code login for another address may be half-way in the same browser.
    expect(mocks.clearLoginFlow).toHaveBeenCalledTimes(1);
    expect(result.redirectedTo).toBe("/oversikt");
  });

  it.each<[string, Response]>([
    ["an unusable link", problem(410, "Auth.LoginLinkUnusable")],
    ["a token the backend's validator refuses", json(400, { errors: { Token: ["För lång."] } })],
    ["a consent outcome, which a link never leads to", json(200, { outcome: "consentRequired", grantToken: "g" })],
  ])("says the one sentence for %s", async (_label, response) => {
    mocks.fetch.mockResolvedValue(response);

    const result = await run(() => consumeLink(null, form({ token: "link-token" })));

    expect(result.state).toEqual({ kind: "unusable" });
    expect(mocks.setSessionCookie).not.toHaveBeenCalled();
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
  });

  it.each(["", "   ", "x".repeat(129)])("never sends the token %j", async (token) => {
    const result = await run(() => consumeLink(null, form({ token })));

    expect(result.state).toEqual({ kind: "unusable" });
    expect(mocks.fetch).not.toHaveBeenCalled();
  });

  it("returns a terminal outcome as state and touches no cookie", async () => {
    const body = { outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" };
    mocks.fetch.mockResolvedValue(json(200, body));

    const result = await run(() => consumeLink(null, form({ token: "link-token" })));

    expect(result.state).toEqual({ kind: "outcome", result: body });
    expect(mocks.writeLoginFlow).not.toHaveBeenCalled();
    expect(mocks.clearLoginFlow).not.toHaveBeenCalled();
  });

  it.each<[string, () => Response | Promise<never>, string]>([
    ["a 429", () => json(429, {}), `${K}.errors.tooManyAttempts`],
    ["a 503", () => json(503, { error: "Tjänsten är inte tillgänglig." }), `${K}.errors.unavailable`],
    ["a transport failure", () => Promise.reject(new Error("ECONNREFUSED")), `${K}.errors.unavailable`],
  ])("answers %s as a retryable error, not as a dead link", async (_label, respond, error) => {
    mocks.fetch.mockImplementation(async () => respond());

    const result = await run(() => consumeLink(null, form({ token: "link-token" })));

    expect(result.state).toEqual({ kind: "error", error });
  });
});

// The rule that makes Next's re-render harmless, stated once over every path above: whenever the
// flow cookie was written, the action ended in a redirect. `resendCode`'s success is the one
// declared exception and is asserted where it lives.
describe("the cookie/redirect rule", () => {
  const scenarios: ReadonlyArray<[string, () => Promise<unknown>, () => void]> = [
    ["requestCode 202", () => requestCode(null, form({ email: "a@example.com" })), () => mocks.fetch.mockResolvedValue(json(202, { challengeId: "c" }))],
    ["verifyCode 410", () => verifyCode(null, form({ code: "123456" })), () => { mocks.readLoginFlow.mockResolvedValue(liveCode); mocks.fetch.mockResolvedValue(problem(410, "Auth.LoginCodeBurned")); }],
    ["verifyCode consentRequired", () => verifyCode(null, form({ code: "123456" })), () => { mocks.readLoginFlow.mockResolvedValue(liveCode); mocks.fetch.mockResolvedValue(json(200, { outcome: "consentRequired", grantToken: "g" })); }],
    ["verifyCode outcome", () => verifyCode(null, form({ code: "123456" })), () => { mocks.readLoginFlow.mockResolvedValue(liveCode); mocks.fetch.mockResolvedValue(json(200, { outcome: "registrationClosed" })); }],
    ["verifyCode no cookie", () => verifyCode(null, form({ code: "123456" })), () => mocks.readLoginFlow.mockResolvedValue(null)],
    ["completeRegistration 410", () => completeRegistration(null, form({ acceptTerms: "on" })), () => { mocks.readLoginFlow.mockResolvedValue(consent); mocks.fetch.mockResolvedValue(problem(410, "Auth.LoginGrantUnusable")); }],
    ["completeRegistration closed", () => completeRegistration(null, form({ acceptTerms: "on" })), () => { mocks.readLoginFlow.mockResolvedValue(consent); mocks.fetch.mockResolvedValue(problem(503, "Auth.RegistrationsClosed")); }],
  ];

  it.each(scenarios)("%s: wrote the cookie, so it redirected", async (_label, action, arrange) => {
    arrange();

    const result = await run(action);

    expect(mocks.writeLoginFlow).toHaveBeenCalledTimes(1);
    expect(result.redirectedTo).toBeDefined();
    expect(result.state).toBeUndefined();
  });
});
