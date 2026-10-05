import { beforeEach, describe, expect, it, vi } from "vitest";

// #1975 — an administrator's request and cancel of an account's address change. What it pins: the input and the
// role are checked before a code is spent; the step-up code is verified by the action that spends it, so no grant
// leaves the server (#1740 S1); every answer after a verified code says the code is spent; the request's answers
// fall in three classes, and a 5xx, a lost response or an unreadable 202 claim nothing (design-reviewer item 5);
// a cancel that found nothing is never a success. The translator returns "namespace.key", so assertions check the
// resolved key.

const { getSessionIdMock, getServerSessionMock, authedFetchMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
  getServerSessionMock: vi.fn(),
  authedFetchMock: vi.fn(),
}));

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/auth/session", () => ({
  ROLES: { Admin: "Admin" },
  getSessionId: getSessionIdMock,
  getServerSession: getServerSessionMock,
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));

import { cancelAccountEmailChangeAction, requestAccountEmailChangeAction } from "./admin-accounts";

const ACCOUNT = "00000000-0000-4000-8000-000000000007";
const PATH = `/api/v1/admin/accounts/${ACCOUNT}/email-change`;
const NEW = "ny.adress@exempel.se";
const PROOF = { challengeId: "challenge-under-test", code: "123456" };
const GRANT = "grant-sentinel-5d21";
const SPENT = "settings.account.reauth.codeSpent";
const COMPLETABLE_FROM = "2026-10-08T12:00:00+00:00";
const EXPIRES_AT = "2026-10-09T12:00:00+00:00";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers });
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

const verified = () => json(200, { reauthGrant: GRANT });
const paths = () => authedFetchMock.mock.calls.map((call) => call[1]);
const request = () => requestAccountEmailChangeAction(ACCOUNT, NEW, PROOF);

beforeEach(() => {
  vi.clearAllMocks();
  getSessionIdMock.mockResolvedValue("session-under-test");
  getServerSessionMock.mockResolvedValue({ userId: "admin-id", email: "admin@exempel.se", roles: ["Admin"] });
});

describe("requestAccountEmailChangeAction", () => {
  it("spends the administrator's code on the request, and hands back the pending change, never the grant", async () => {
    backend({
      "/api/v1/auth/reauth/verify": verified,
      [PATH]: () => json(202, { completableFrom: COMPLETABLE_FROM, expiresAt: EXPIRES_AT }),
    });

    const result = await requestAccountEmailChangeAction(ACCOUNT, `  ${NEW}  `, PROOF);

    expect(result).toEqual({
      ok: true,
      value: { state: "pending", completableFrom: COMPLETABLE_FROM, expiresAt: EXPIRES_AT },
    });
    // The trimmed address goes over the wire verbatim; the grant goes there and nowhere else.
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", PATH, {
      method: "POST",
      body: JSON.stringify({ newEmail: NEW, reauthGrant: GRANT }),
    });
    expect(paths()).toEqual(["/api/v1/auth/reauth/verify", PATH]);
    expect(JSON.stringify(result)).not.toContain(GRANT);
  });

  it.each([
    ["an empty address", "   ", "admin.users.edit.refusal.required"],
    ["a malformed address", "ny.exempel.se", "admin.users.edit.refusal.invalid"],
  ])("refuses %s before anything is spent", async (_label, input, copy) => {
    expect(await requestAccountEmailChangeAction(ACCOUNT, input, PROOF)).toEqual({
      ok: false,
      kind: "inputRefused",
      error: copy,
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("refuses an account id that is no GUID before anything is spent, so it never becomes a path", async () => {
    expect(await requestAccountEmailChangeAction("../search", NEW, PROOF)).toEqual({
      ok: false,
      kind: "inputRefused",
      error: "admin.users.errors.gone",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("refuses a malformed code before anything is spent", async () => {
    expect(await requestAccountEmailChangeAction(ACCOUNT, NEW, { challengeId: "c", code: "12ab" })).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "pages.auth.passwordless.code.malformedCode",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("spends no code for a session without the Admin role, and none without a session", async () => {
    getServerSessionMock.mockResolvedValueOnce({ userId: "u", email: "medlem@exempel.se", roles: [] });
    expect(await request()).toEqual({ ok: false, kind: "status", error: "admin.users.errors.forbidden" });

    getServerSessionMock.mockResolvedValueOnce(null);
    expect(await request()).toEqual({ ok: false, kind: "notLoggedIn" });

    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("stops at a refused code: the request is never sent and nothing says a code was spent", async () => {
    backend({ "/api/v1/auth/reauth/verify": () => problem(400, "Auth.LoginCodeWrongLastAttempt") });

    expect(await request()).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "pages.auth.passwordless.code.wrongCode pages.auth.passwordless.code.lastAttempt",
    });
    expect(paths()).toEqual(["/api/v1/auth/reauth/verify"]);
  });

  it.each([
    ["a burned code", () => problem(410, "Auth.LoginCodeBurned"), { ok: false, kind: "deadCode", reason: "burned" }],
    [
      "a throttled verify",
      () => json(429, {}),
      { ok: false, kind: "status", error: "pages.auth.passwordless.errors.tooManyAttempts" },
    ],
    ["a session gone at the verify", () => json(401, {}), { ok: false, kind: "notLoggedIn" }],
  ] as const)("keeps %s in the dialog, where the code step knows it", async (_label, answer, expected) => {
    backend({ "/api/v1/auth/reauth/verify": answer });

    expect(await request()).toEqual(expected);
    expect(paths()).toEqual(["/api/v1/auth/reauth/verify"]);
  });

  it.each([
    ["a taken address", () => problem(409, "Auth.EmailTaken"), "settings.account.errors.emailTaken", "field", undefined],
    ["an address the backend cannot store", () => problem(400, "Auth.EmailNotStorable"), "settings.account.changeEmail.unusable", "field", undefined],
    [
      "an administrator account, which a race made one",
      () => problem(409, "Auth.AccountEmailChangeAdministratorTarget"),
      "admin.users.emailChange.administratorTarget",
      "status",
      "changed",
    ],
    [
      "an account no longer active",
      () => problem(409, "Auth.AccountEmailChangeInactiveTarget"),
      "admin.users.emailChange.changed",
      "status",
      "changed",
    ],
    [
      "an address another account's change holds",
      () => problem(409, "Auth.AccountEmailChangePendingForAnotherAccount"),
      "admin.users.emailChange.pendingElsewhere",
      "status",
      "refetch",
    ],
    ["the shared per-address cooldown", () => problem(409, "Auth.ChangeEmailCooldown"), "settings.account.errors.changeEmailCooldown", "status", undefined],
    ["a 409 nobody named", () => problem(409, "Auth.SomethingElse"), "settings.account.errors.changeEmailCooldown", "status", undefined],
    ["a refused grant", () => problem(401, "Auth.InvalidCredentials"), "admin.users.emailChange.notConfirmed", "status", undefined],
    ["a session without the role by now", () => json(403, {}), "admin.users.errors.forbidden", "status", undefined],
    ["a rate limit", () => json(429, {}, { "Retry-After": "6" }), "admin.users.errors.rateLimited", "status", undefined],
    ["an account that is gone", () => problem(404, "Auth.UserNotFound"), "admin.users.errors.gone", "status", "gone"],
  ] as const)("refuses %s after the code, and says the code is spent", async (_label, answer, copy, channel, after) => {
    backend({ "/api/v1/auth/reauth/verify": verified, [PATH]: answer });

    expect(await request()).toEqual({
      ok: false,
      kind: "operationRefused",
      error: `${copy} ${SPENT}`,
      channel,
      ...(after === undefined ? {} : { after }),
    });
  });

  it("answers mail that cannot be sent as such, and says the code is spent", async () => {
    backend({ "/api/v1/auth/reauth/verify": verified, [PATH]: () => problem(503, "Auth.EmailDeliveryUnavailable") });

    expect(await request()).toEqual({
      ok: false,
      kind: "refused",
      error: `admin.users.emailChange.deliveryUnavailable ${SPENT}`,
    });
  });

  it.each([
    ["a 500, a mail not accepted", () => json(500, {})],
    ["the volatile store's 503, which carries no title", () => json(503, { error: "unavailable" })],
    ["a 503 that is not JSON", () => new Response("Service Unavailable", { status: 503 })],
    ["a 202 that does not read", () => json(202, { completableFrom: "later" })],
    ["a 404 nobody named", () => json(404, {})],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing about %s: the change may be pending", async (_label, answer) => {
    backend({ "/api/v1/auth/reauth/verify": verified, [PATH]: answer });

    expect(await request()).toEqual({
      ok: false,
      kind: "outcomeUnknown",
      error: `admin.users.emailChange.unknown ${SPENT}`,
    });
  });

  it("treats a 401 with no title as a session that ended on the way", async () => {
    backend({ "/api/v1/auth/reauth/verify": verified, [PATH]: () => json(401, {}) });

    expect(await request()).toEqual({ ok: false, kind: "notLoggedIn" });
  });
});

describe("cancelAccountEmailChangeAction", () => {
  const cancel = () => cancelAccountEmailChangeAction(ACCOUNT);

  it("deletes the account's pending change, and nothing else", async () => {
    backend({ [PATH]: () => new Response(null, { status: 204 }) });

    expect(await cancel()).toEqual({ kind: "cancelled" });
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", PATH, { method: "DELETE" });
  });

  it("answers a cancel that found nothing as nothing pending, never as done", async () => {
    backend({ [PATH]: () => problem(410, "Auth.AccountEmailChangeNothingPending") });

    expect(await cancel()).toEqual({ kind: "nothingPending" });
  });

  it.each([
    [401, { kind: "refused", reason: "unauthorized" }],
    [403, { kind: "refused", reason: "forbidden" }],
  ] as const)("says why a %i refused the cancel before it ran", async (status, expected) => {
    backend({ [PATH]: () => json(status, {}) });

    expect(await cancel()).toEqual(expected);
  });

  it("forwards a rate limit with its wait", async () => {
    backend({ [PATH]: () => json(429, {}, { "Retry-After": "9" }) });

    expect(await cancel()).toEqual({ kind: "refused", reason: "rateLimited", retryAfterSeconds: 9 });
  });

  it.each([
    ["a 500", () => json(500, {})],
    ["the volatile store's 503", () => json(503, { error: "unavailable" })],
    ["a 410 nobody named", () => json(410, {})],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing about %s: the change may be cancelled", async (_label, answer) => {
    backend({ [PATH]: answer });

    expect(await cancel()).toEqual({ kind: "unknown" });
  });

  it("asks nothing without a session, and answers an id that is no GUID as nothing pending", async () => {
    getSessionIdMock.mockResolvedValueOnce(null);
    expect(await cancel()).toEqual({ kind: "refused", reason: "unauthorized" });

    expect(await cancelAccountEmailChangeAction("../search")).toEqual({ kind: "nothingPending" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});
