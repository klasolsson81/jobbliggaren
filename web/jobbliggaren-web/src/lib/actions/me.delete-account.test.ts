import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// #1740 — deleteAccountAction on a re-authentication code. What it pins: the typed address is compared
// with the SESSION's before anything is spent (#822), in the one comparison form (NFC, case); the code is
// verified and the account deleted in this one action, so the grant never leaves it; the backend's answer
// after the code falls in three classes (security-auditor, #1740 Minor 3); and a deletion ends the session
// and leaves the login page its notice in the same response, before the redirect.

const calls: string[] = [];
const { getSessionIdMock, getServerSessionMock, authedFetchMock, writeLoginFlowMock } = vi.hoisted(
  () => ({
    getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
    getServerSessionMock: vi.fn(),
    authedFetchMock: vi.fn(),
    writeLoginFlowMock: vi.fn(),
  })
);

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
vi.mock("next/navigation", () => ({
  redirect: (path: string) => {
    calls.push(`redirect ${path}`);
    // The real one throws NEXT_REDIRECT; the tests catch it as the success signal.
    throw new Error("NEXT_REDIRECT");
  },
}));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
  getServerSession: getServerSessionMock,
  setSessionCookie: vi.fn(),
  deleteSessionCookie: async () => {
    calls.push("deleteSessionCookie");
  },
}));
vi.mock("@/lib/auth/login-flow-cookie", () => ({ writeLoginFlow: writeLoginFlowMock }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
vi.mock("@/lib/api/me", () => ({
  updateNotificationConsent: vi.fn(),
  updateFollowedCompanyNotificationConsent: vi.fn(),
}));

import { deleteAccountAction } from "./me";

const ADDRESS = "klas.olsson@exempel.se";
const PROOF = { challengeId: "challenge-under-test", code: "123456" };
const GRANT = "grant-under-test";

const problem = (status: number, title: string) =>
  new Response(JSON.stringify({ type: "about:blank", title, status, detail: "backend text" }), {
    status,
  });
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status });

/** Answers the verify and the delete by path, the way the two backend endpoints would. */
function backend(verify: () => Response, remove: () => Response | Promise<Response>) {
  authedFetchMock.mockImplementation(async (_session: string, path: string) => {
    if (path === "/api/v1/auth/reauth/verify") return verify();
    if (path === "/api/v1/me/delete") return remove();
    throw new Error(`unexpected path ${path}`);
  });
}

const paths = () => authedFetchMock.mock.calls.map((call) => call[1]);

describe("deleteAccountAction", () => {
  beforeEach(() => {
    calls.length = 0;
    authedFetchMock.mockReset();
    writeLoginFlowMock.mockReset();
    writeLoginFlowMock.mockImplementation(async () => {
      calls.push("writeLoginFlow");
    });
    getSessionIdMock.mockResolvedValue("session-under-test");
    getServerSessionMock.mockResolvedValue({ email: ADDRESS });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("deletes on a verified code: the grant goes to /me/delete and nowhere else", async () => {
    backend(
      () => json(200, { reauthGrant: GRANT }),
      () => new Response(null, { status: 204 })
    );

    await expect(deleteAccountAction(ADDRESS, PROOF)).rejects.toThrow("NEXT_REDIRECT");

    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", "/api/v1/me/delete", {
      method: "POST",
      body: JSON.stringify({ reauthGrant: GRANT }),
    });
  });

  it("leaves the login page its notice and ends the session in the same response, before the redirect", async () => {
    backend(
      () => json(200, { reauthGrant: GRANT }),
      () => new Response(null, { status: 204 })
    );

    await expect(deleteAccountAction(ADDRESS, PROOF)).rejects.toThrow("NEXT_REDIRECT");

    expect(writeLoginFlowMock).toHaveBeenCalledWith({ phase: "notice", notice: "accountDeleted" });
    expect(calls).toEqual(["writeLoginFlow", "deleteSessionCookie", "redirect /logga-in"]);
  });

  it("refuses a typed address that is not the session's before anything is spent", async () => {
    const result = await deleteAccountAction("someone.else@exempel.se", PROOF);

    expect(result).toEqual({
      ok: false,
      kind: "inputRefused",
      error: "settings.account.delete.confirmMismatch",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("fails closed when the session's address is empty: '' must never equal ''", async () => {
    getServerSessionMock.mockResolvedValue({ email: "" });

    const result = await deleteAccountAction("", PROOF);

    expect(result).toMatchObject({ ok: false, kind: "inputRefused" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("accepts the address in another normal form, another case and with spaces (#1740 Minor 4)", async () => {
    getServerSessionMock.mockResolvedValue({ email: `bj${String.fromCodePoint(0xf6)}rn@exempel.se` });
    backend(
      () => json(200, { reauthGrant: GRANT }),
      () => new Response(null, { status: 204 })
    );

    await expect(
      deleteAccountAction(`  BJO${String.fromCodePoint(0x308)}RN@exempel.se `, PROOF)
    ).rejects.toThrow("NEXT_REDIRECT");
  });

  it("refuses a malformed code without presenting it: a malformed code spends no attempt", async () => {
    const result = await deleteAccountAction(ADDRESS, { ...PROOF, code: "12345" });

    expect(result).toEqual({
      ok: false,
      kind: "wrongCode",
      error: "pages.auth.passwordless.code.malformedCode",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("answers a lost session without spending anything", async () => {
    getServerSessionMock.mockResolvedValue(null);

    expect(await deleteAccountAction(ADDRESS, PROOF)).toEqual({ ok: false, kind: "notLoggedIn" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [
      problem(400, "Auth.LoginCodeWrong"),
      { ok: false, kind: "wrongCode", error: "pages.auth.passwordless.code.wrongCode" },
    ],
    [
      problem(400, "Auth.LoginCodeWrongLastAttempt"),
      {
        ok: false,
        kind: "wrongCode",
        error: "pages.auth.passwordless.code.wrongCode pages.auth.passwordless.code.lastAttempt",
      },
    ],
    [problem(410, "Auth.LoginCodeBurned"), { ok: false, kind: "deadCode", reason: "burned" }],
    [problem(410, "Auth.LoginCodeExpired"), { ok: false, kind: "deadCode", reason: "expired" }],
    [
      json(429, {}),
      { ok: false, kind: "status", error: "pages.auth.passwordless.errors.tooManyAttempts" },
    ],
    [json(401, {}), { ok: false, kind: "notLoggedIn" }],
    [
      json(503, { error: "x" }),
      { ok: false, kind: "status", error: "settings.account.reauth.verifyUnavailable" },
    ],
  ])("passes a refused code through and never reaches /me/delete (%#)", async (response, expected) => {
    backend(
      () => response,
      () => new Response(null, { status: 204 })
    );

    expect(await deleteAccountAction(ADDRESS, PROOF)).toEqual(expected);
    expect(paths()).toEqual(["/api/v1/auth/reauth/verify"]);
    expect(calls).toEqual([]);
  });

  it.each([400, 401, 404, 429])(
    "calls a documented %i after the code a refusal, and says the code is spent",
    async (status) => {
      backend(
        () => json(200, { reauthGrant: GRANT }),
        () => problem(status, "Auth.Anything")
      );

      expect(await deleteAccountAction(ADDRESS, PROOF)).toEqual({
        ok: false,
        kind: "operationRefused",
        error: "settings.account.delete.failed settings.account.reauth.codeSpent",
        channel: "status",
      });
      expect(calls).toEqual([]);
    }
  );

  it.each([
    ["a 500", () => json(500, {})],
    ["a titled 503", () => problem(503, "Auth.Anything")],
    ["a 200 that is not the 204", () => json(200, {})],
    [
      "a transport failure",
      () => {
        throw new TypeError("fetch failed");
      },
    ],
  ])("claims nothing about %s after the code: the deletion may have happened", async (_, remove) => {
    backend(() => json(200, { reauthGrant: GRANT }), remove);

    expect(await deleteAccountAction(ADDRESS, PROOF)).toEqual({
      ok: false,
      kind: "outcomeUnknown",
      error: "settings.account.delete.outcomeUnknown",
    });
    expect(calls).toEqual([]);
  });

  it("never returns the grant, and writes none of the secrets to a console", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((level) =>
      vi.spyOn(console, level).mockImplementation(() => {})
    );
    const results: unknown[] = [];
    for (const remove of [
      () => problem(401, "Auth.InvalidCredentials"),
      () => json(500, {}),
      () => json(200, { reauthGrant: 42 }),
    ]) {
      backend(() => json(200, { reauthGrant: GRANT }), remove);
      results.push(await deleteAccountAction(ADDRESS, PROOF));
    }

    const returned = JSON.stringify(results);
    const written = JSON.stringify(spies.flatMap((spy) => spy.mock.calls));
    for (const secret of [GRANT, "session-under-test"]) {
      expect(returned).not.toContain(secret);
      expect(written).not.toContain(secret);
    }
    for (const secret of [PROOF.code, PROOF.challengeId]) {
      expect(written).not.toContain(secret);
    }
  });
});
