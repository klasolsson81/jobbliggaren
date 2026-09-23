import { beforeEach, describe, expect, it, vi } from "vitest";

const { getSessionIdMock, authedFetchMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
  authedFetchMock: vi.fn(),
}));

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId: getSessionIdMock }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));

import { requestReauthCode } from "./reauth-actions";

// The backend's shapes: a DomainError is ProblemDetails with the machine code in `title`; the session
// store's own 503 is `{ error }` with no title (Program.cs); a proxy may answer with no JSON at all.
const problem = (status: number, title: string) =>
  new Response(JSON.stringify({ type: "about:blank", title, status, detail: "backend text" }), {
    status,
  });
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status });

describe("requestReauthCode", () => {
  beforeEach(() => {
    authedFetchMock.mockReset();
    getSessionIdMock.mockResolvedValue("session-under-test");
  });

  it("asks for a code with no body: the address is the session's own, never the client's", async () => {
    authedFetchMock.mockResolvedValue(json(202, { challengeId: "challenge-under-test" }));

    const result = await requestReauthCode();

    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", "/api/v1/auth/reauth", {
      method: "POST",
    });
    expect(result).toEqual({ ok: true, challengeId: "challenge-under-test" });
  });

  it("calls the daily budget terminal, and names no one who asked", async () => {
    authedFetchMock.mockResolvedValue(problem(409, "Auth.ReauthCodeBudgetExhausted"));

    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "terminal",
      error: "settings.account.reauth.budgetExhausted",
    });
  });

  it("marks the cooldown, so a resend countdown can start over", async () => {
    authedFetchMock.mockResolvedValue(problem(409, "Auth.ReauthCooldown"));

    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "status",
      error: "settings.account.reauth.cooldown",
      cooldown: true,
    });
  });

  it("claims no cause for a 409 it does not know", async () => {
    authedFetchMock.mockResolvedValue(problem(409, "Auth.SomethingElse"));

    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "status",
      error: "settings.account.reauth.unavailable",
    });
  });

  it("tells mail that cannot be delivered from a 503 that says nothing about mail", async () => {
    authedFetchMock.mockResolvedValueOnce(problem(503, "Auth.EmailDeliveryUnavailable"));
    authedFetchMock.mockResolvedValueOnce(json(503, { error: "Tjänsten är inte tillgänglig." }));
    authedFetchMock.mockResolvedValueOnce(new Response("<html>Bad gateway</html>", { status: 503 }));

    expect(await requestReauthCode()).toEqual({ ok: false, kind: "refused" });
    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "status",
      error: "settings.account.reauth.unavailable",
    });
    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "status",
      error: "settings.account.reauth.unavailable",
    });
  });

  it("answers a throttle with the login page's copy, and a lost session as not logged in", async () => {
    authedFetchMock.mockResolvedValueOnce(json(429, {}));
    authedFetchMock.mockResolvedValueOnce(json(401, {}));

    expect(await requestReauthCode()).toEqual({
      ok: false,
      kind: "status",
      error: "pages.auth.passwordless.errors.tooManyAttempts",
    });
    expect(await requestReauthCode()).toEqual({ ok: false, kind: "notLoggedIn" });
  });

  it("does not call the backend without a session", async () => {
    getSessionIdMock.mockResolvedValue(null);

    expect(await requestReauthCode()).toEqual({ ok: false, kind: "notLoggedIn" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("answers a transport failure and an unreadable 202 as unavailable, never as a challenge", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    authedFetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    authedFetchMock.mockResolvedValueOnce(json(202, { challengeId: "" }));

    for (let i = 0; i < 2; i++) {
      expect(await requestReauthCode()).toEqual({
        ok: false,
        kind: "status",
        error: "settings.account.reauth.unavailable",
      });
    }
  });
});
