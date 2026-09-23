import { beforeEach, describe, expect, it, vi } from "vitest";

// #1740 — change-email by two codes. What it pins: the new address is checked before anything is spent;
// each code is verified by the action that spends it, so no grant and no session id leaves the server
// (security-auditor, #1740 S1 (d)); every refusal after a verified code says the code is spent (Minor 7);
// the request's 409 compares only the two codes with copy of their own (S3); the confirm's answer falls
// in three classes (Minor 3), and only a parsed 200 re-issues the session (S2). The translator returns
// "namespace.key", so assertions check the resolved key.

const { getSessionIdMock, getServerSessionMock, setSessionCookieMock, authedFetchMock } = vi.hoisted(
  () => ({
    getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
    getServerSessionMock: vi.fn(),
    setSessionCookieMock: vi.fn(),
    authedFetchMock: vi.fn(),
  })
);

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
  getServerSession: getServerSessionMock,
  setSessionCookie: setSessionCookieMock,
  deleteSessionCookie: vi.fn(),
}));
vi.mock("@/lib/auth/login-flow-cookie", () => ({ writeLoginFlow: vi.fn() }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
vi.mock("@/lib/api/me", () => ({
  updateNotificationConsent: vi.fn(),
  updateFollowedCompanyNotificationConsent: vi.fn(),
}));

import { confirmEmailChangeAction, requestEmailChangeAction } from "./me";

const CURRENT = "anna@exempel.se";
const NEW = "ny.adress@exempel.se";
const PROOF = { challengeId: "challenge-under-test", code: "123456" };
const GRANT = "grant-sentinel-7c1e";
const CHANGE_GRANT = "change-grant-sentinel-44b9";
const REISSUED = "reissued-session-sentinel-90aa";
const SPENT = "settings.account.reauth.codeSpent";

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status });
const problem = (status: number, title: string) =>
  json(status, { type: "about:blank", title, status, detail: "backend text" });

/** Answers each backend path the way its endpoint would. */
function backend(routes: Record<string, () => Response | Promise<Response>>) {
  authedFetchMock.mockImplementation(async (_session: string, path: string) => {
    const route = routes[path];
    if (!route) throw new Error(`unexpected path ${path}`);
    return route();
  });
}

const verifiedReauth = () => json(200, { reauthGrant: GRANT });
const verifiedChange = () => json(200, { changeEmailGrant: CHANGE_GRANT });
const paths = () => authedFetchMock.mock.calls.map((call) => call[1]);

beforeEach(() => {
  vi.clearAllMocks();
  getSessionIdMock.mockResolvedValue("session-under-test");
  getServerSessionMock.mockResolvedValue({ email: CURRENT });
});

describe("requestEmailChangeAction", () => {
  it("spends the re-auth code on the change request and hands back only the change challenge", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => json(202, { challengeId: "change-challenge" }),
    });

    const result = await requestEmailChangeAction(`  ${NEW}  `, PROOF);

    expect(result).toEqual({ ok: true, value: { challengeId: "change-challenge" } });
    // The trimmed address goes over the wire verbatim; the grant goes there and nowhere else.
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", "/api/v1/auth/change-email", {
      method: "POST",
      body: JSON.stringify({ reauthGrant: GRANT, newEmail: NEW }),
    });
    expect(JSON.stringify(result)).not.toContain(GRANT);
  });

  it.each([
    ["an empty address", "   ", "settings.account.changeEmail.newEmailRequired"],
    ["a malformed address", "ny.exempel.se", "settings.account.changeEmail.invalidEmail"],
    ["the account's own address", " ANNA@exempel.se ", "settings.account.changeEmail.sameEmail"],
  ])("refuses %s before anything is spent", async (_label, input, copy) => {
    const result = await requestEmailChangeAction(input, PROOF);

    expect(result).toEqual({ ok: false, kind: "inputRefused", error: copy });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("refuses a malformed code before anything is spent", async () => {
    const result = await requestEmailChangeAction(NEW, { challengeId: "c", code: "12ab" });

    expect(result).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "pages.auth.passwordless.code.malformedCode",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("stops at a refused code: the request is never sent and nothing says a code was spent", async () => {
    backend({
      "/api/v1/auth/reauth/verify": () => problem(400, "Auth.LoginCodeWrongLastAttempt"),
    });

    const result = await requestEmailChangeAction(NEW, PROOF);

    expect(result).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "pages.auth.passwordless.code.wrongCode pages.auth.passwordless.code.lastAttempt",
    });
    expect(paths()).toEqual(["/api/v1/auth/reauth/verify"]);
  });

  it("puts a taken address back on the field, and says the code is spent", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => problem(409, "Auth.EmailTaken"),
    });

    expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.errors.emailTaken ${SPENT}`,
      channel: "field",
    });
  });

  it("ends the flow for the day when the target budget is spent", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => problem(409, "Auth.ChangeEmailTargetBudgetExhausted"),
    });

    expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.errors.changeEmailTargetBudget ${SPENT}`,
      channel: "status",
      terminal: true,
    });
  });

  it.each([
    ["the shared cooldown", "Auth.ChangeEmailCooldown"],
    ["a future 409", "Auth.SomethingNew"],
    ["a 409 carrying the mail title", "Auth.EmailDeliveryUnavailable"],
  ])(
    "gives every other 409 the neutral copy: %s",
    async (_label, title) => {
      backend({
        "/api/v1/auth/reauth/verify": verifiedReauth,
        "/api/v1/auth/change-email": () => problem(409, title),
      });

      expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
        ok: false,
        kind: "operationRefused",
        error: `settings.account.errors.changeEmailCooldown ${SPENT}`,
        channel: "status",
      });
    }
  );

  it("names an address the backend cannot store as unusable, on the field", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => problem(400, "Auth.EmailNotStorable"),
    });

    expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.changeEmail.unusable ${SPENT}`,
      channel: "field",
    });
  });

  it("answers mail that cannot be delivered after the code was spent as the deployment's refusal", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => problem(503, "Auth.EmailDeliveryUnavailable"),
    });

    expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "refused",
      error: `settings.account.errors.emailDeliveryUnavailable ${SPENT}`,
    });
  });

  it.each([
    ["a 401", () => new Response(null, { status: 401 })],
    ["a 429", () => new Response(null, { status: 429 })],
    ["a 500", () => new Response(null, { status: 500 })],
    ["a 503 without a title", () => json(503, { error: "Tjänsten är inte tillgänglig just nu." })],
    ["a 503 whose title is not about mail", () => problem(503, "Auth.SomethingElse")],
    ["a 503 from a proxy, not JSON", () => new Response("<html>Bad gateway</html>", { status: 503 })],
    ["a 202 without a readable id", () => json(202, { nothing: true })],
  ])("says the change was not made on %s, since the address cannot change at this step", async (_l, answer) => {
    backend({ "/api/v1/auth/reauth/verify": verifiedReauth, "/api/v1/auth/change-email": answer });

    expect(await requestEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.changeEmail.notDone ${SPENT}`,
      channel: "status",
    });
  });

  it("says the change was not made when the request never answers", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verifiedReauth,
      "/api/v1/auth/change-email": () => Promise.reject(new Error("network down")),
    });

    expect(await requestEmailChangeAction(NEW, PROOF)).toMatchObject({
      kind: "operationRefused",
      error: `settings.account.changeEmail.notDone ${SPENT}`,
    });
  });
});

describe("confirmEmailChangeAction", () => {
  it("re-issues the device's session from a parsed 200, and hands back nothing of it", async () => {
    backend({
      "/api/v1/auth/change-email/verify": verifiedChange,
      "/api/v1/auth/change-email/confirm": () => json(200, { sessionId: REISSUED, persistent: true }),
    });

    const result = await confirmEmailChangeAction(NEW, PROOF);

    expect(result).toEqual({ ok: true, value: null });
    expect(setSessionCookieMock).toHaveBeenCalledWith(REISSUED, true);
    expect(authedFetchMock).toHaveBeenCalledWith(
      "session-under-test",
      "/api/v1/auth/change-email/confirm",
      { method: "POST", body: JSON.stringify({ changeEmailGrant: CHANGE_GRANT, newEmail: NEW }) }
    );
    expect(JSON.stringify(result)).not.toContain(REISSUED);
    expect(JSON.stringify(result)).not.toContain(CHANGE_GRANT);
    // The old id is dead once the confirm has answered: nothing reads the session after it.
    expect(getServerSessionMock).not.toHaveBeenCalled();
  });

  it("keeps a session's lifetime profile: a non-persistent device stays non-persistent", async () => {
    backend({
      "/api/v1/auth/change-email/verify": verifiedChange,
      "/api/v1/auth/change-email/confirm": () => json(200, { sessionId: REISSUED, persistent: false }),
    });

    await confirmEmailChangeAction(NEW, PROOF);

    expect(setSessionCookieMock).toHaveBeenCalledWith(REISSUED, false);
  });

  it.each([
    ["a 200 that does not parse", () => json(200, { sessionId: 42 })],
    ["a 500", () => problem(500, "An error occurred")],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing on %s: the change may have been committed", async (_label, answer) => {
    backend({ "/api/v1/auth/change-email/verify": verifiedChange, "/api/v1/auth/change-email/confirm": answer });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "outcomeUnknown",
      error: "settings.account.changeEmail.outcomeUnknown",
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("sends a taken address back to the field when it was taken meanwhile", async () => {
    backend({
      "/api/v1/auth/change-email/verify": verifiedChange,
      "/api/v1/auth/change-email/confirm": () => problem(409, "Auth.EmailTaken"),
    });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.errors.emailTaken ${SPENT}`,
      channel: "field",
    });
  });

  it("names an incomplete swap, and the flow starts over", async () => {
    backend({
      "/api/v1/auth/change-email/verify": verifiedChange,
      "/api/v1/auth/change-email/confirm": () => problem(409, "Auth.EmailChangeIncomplete"),
    });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.changeEmail.incomplete ${SPENT}`,
      channel: "status",
      terminal: true,
    });
  });

  it.each([
    ["a 410", () => problem(410, "Auth.EmailChangeGrantUnusable")],
    ["a 400", () => problem(400, "Validation")],
    ["a 401", () => new Response(null, { status: 401 })],
    ["a 429", () => new Response(null, { status: 429 })],
    ["another 409", () => problem(409, "Auth.SomethingNew")],
  ])("says the change was not made on %s, and the flow starts over", async (_label, answer) => {
    backend({ "/api/v1/auth/change-email/verify": verifiedChange, "/api/v1/auth/change-email/confirm": answer });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `settings.account.changeEmail.notDone ${SPENT}`,
      channel: "status",
      terminal: true,
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("names which code was wrong: the one mailed to the new address", async () => {
    backend({ "/api/v1/auth/change-email/verify": () => problem(400, "Auth.LoginCodeWrong") });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "settings.account.changeEmail.wrongCode",
    });
    expect(paths()).toEqual(["/api/v1/auth/change-email/verify"]);
  });

  it("warns before the last attempt in the change code's own words", async () => {
    backend({ "/api/v1/auth/change-email/verify": () => problem(400, "Auth.LoginCodeWrongLastAttempt") });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "settings.account.changeEmail.wrongCode settings.account.changeEmail.lastAttempt",
    });
  });

  it.each([
    ["burned", "Auth.LoginCodeBurned", "burned"],
    ["expired", "Auth.LoginCodeExpired", "expired"],
  ])("hands back a dead code as %s", async (_label, title, reason) => {
    backend({ "/api/v1/auth/change-email/verify": () => problem(410, title) });

    expect(await confirmEmailChangeAction(NEW, PROOF)).toEqual({ ok: false, kind: "deadCode", reason });
  });

  it("refuses a malformed address or code before anything is spent", async () => {
    expect(await confirmEmailChangeAction("   ", PROOF)).toEqual({
      ok: false,
      kind: "inputRefused",
      error: "settings.account.changeEmail.invalidEmail",
    });
    expect(await confirmEmailChangeAction(NEW, { challengeId: "c", code: "1" })).toMatchObject({
      kind: "wrongCode",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});

describe("both change-email actions", () => {
  it("never return a grant or a session id, and write none of the secrets to a console", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((level) =>
      vi.spyOn(console, level).mockImplementation(() => {})
    );
    const results: unknown[] = [];
    for (const answer of [
      () => problem(409, "Auth.EmailTaken"),
      () => json(500, {}),
      () => json(202, { challengeId: 7 }),
    ]) {
      backend({ "/api/v1/auth/reauth/verify": verifiedReauth, "/api/v1/auth/change-email": answer });
      results.push(await requestEmailChangeAction(NEW, PROOF));
    }
    for (const answer of [
      () => json(200, { sessionId: REISSUED, persistent: true }),
      // Fails the strict parse while carrying the new session id: the parse error must not echo it.
      () => json(200, { sessionId: REISSUED }),
      () => problem(409, "Auth.EmailChangeIncomplete"),
    ]) {
      backend({
        "/api/v1/auth/change-email/verify": verifiedChange,
        "/api/v1/auth/change-email/confirm": answer,
      });
      results.push(await confirmEmailChangeAction(NEW, PROOF));
    }

    const returned = JSON.stringify(results);
    const written = JSON.stringify(spies.flatMap((spy) => spy.mock.calls));
    for (const secret of [GRANT, CHANGE_GRANT, REISSUED, "session-under-test"]) {
      expect(returned).not.toContain(secret);
      expect(written).not.toContain(secret);
    }
    for (const secret of [PROOF.code, PROOF.challengeId]) {
      expect(written).not.toContain(secret);
    }
    spies.forEach((spy) => spy.mockRestore());
  });
});
