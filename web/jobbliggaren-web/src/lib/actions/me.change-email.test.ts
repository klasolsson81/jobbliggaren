import { describe, it, expect, vi, beforeEach } from "vitest";

// #679 — changeEmailAction. Pins the branches the card test can't reach: the
// status -> Swedish-error mapping (401/400/409/500/network), the client-side schema
// gate, and the invariant that — unlike change-password — NO session cookie is ever
// touched (the email is not changed at request time; a link is emailed). The
// translator mock returns the key verbatim, so assertions check the resolved key.

const { setSessionCookieMock, getSessionIdMock, authedFetchMock } = vi.hoisted(
  () => ({
    setSessionCookieMock: vi.fn(),
    getSessionIdMock: vi.fn(async () => "sess-current" as string | null),
    authedFetchMock: vi.fn(),
  }),
);

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
  setSessionCookie: setSessionCookieMock,
  deleteSessionCookie: vi.fn(),
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
vi.mock("@/lib/api/me", () => ({ updateNotificationConsent: vi.fn() }));

import { changeEmailAction } from "./me";

const CURRENT = "Current123456";
const NEW_EMAIL = "ny.adress@exempel.se";

function fakeResponse(status: number, body: unknown = {}): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

/**
 * A response whose body is not JSON — what a reverse proxy in front of the API answers
 * with (an HTML error page). `res.json()` rejects, which is the third 503 producer named
 * in release-checklist.md §2.6 point 5.5.
 */
function fakeNonJsonResponse(status: number): Response {
  return {
    status,
    ok: false,
    json: async () => {
      throw new SyntaxError("Unexpected token '<', \"<html>\"... is not valid JSON");
    },
  } as unknown as Response;
}

describe("changeEmailAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getSessionIdMock.mockResolvedValue("sess-current");
  });

  it("treats 202 as success and never touches the session cookie", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(202));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({ success: true });
    // The email is not changed at request time — no cookie re-issue (the drop).
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("POSTs the current password + new email to /auth/change-email", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(202));

    await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(authedFetchMock).toHaveBeenCalledWith(
      "sess-current",
      "/api/v1/auth/change-email",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ currentPassword: CURRENT, newEmail: NEW_EMAIL }),
      }),
    );
  });

  it("maps 401 to the wrong-password error", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(401));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.wrongPassword",
    });
  });

  it("maps 400 to the invalid-input error", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(400));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.invalidInput",
    });
  });

  it("maps a 409 without a recognized title to the email-taken error (the fallback)", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(409));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.emailTaken",
    });
  });

  it("maps a 409 with the ChangeEmailCooldown title to the cooldown copy (not email-taken)", async () => {
    // #703/#792: the change-email endpoint returns two distinct 409 codes. A cooldown must render the
    // wait-a-moment message, not the (wrong) "address already taken" copy.
    authedFetchMock.mockResolvedValue(
      fakeResponse(409, { title: "Auth.ChangeEmailCooldown" })
    );

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.changeEmailCooldown",
    });
  });

  // #734 B-ii — the 503 arm. This route has at least THREE 503 producers and only one of
  // them is ours, so every test below is paired with its counterfactual: a discriminating
  // assertion measured against only the outcome it is meant to catch cannot discriminate.
  // Fixture provenance: the winning shape is the one the API actually emits, pinned by
  // tests/Jobbliggaren.Api.IntegrationTests/Auth/ChangeEmailTests.cs:263; the losing shape
  // is the one src/Jobbliggaren.Api/Program.cs:296 actually writes.
  it("maps OUR 503 (Auth.EmailDeliveryUnavailable) to the refused state, not the generic failure", async () => {
    authedFetchMock.mockResolvedValue(
      fakeResponse(503, { title: "Auth.EmailDeliveryUnavailable" })
    );

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      refused: true,
      error: "account.errors.emailDeliveryUnavailable",
    });
  });

  it("does NOT claim email is disabled for the session-store 503 (the Redis counterfactual)", async () => {
    // Program.cs:296 writes `WriteAsJsonAsync(new { error = ex.Message })` — plain JSON with
    // no `title` field at all, so readProblemTitle resolves it to null. A status-only arm
    // would print "e-post är inte aktiverat" during a Redis outage and mask the incident.
    authedFetchMock.mockResolvedValue(
      fakeResponse(503, { error: "Redis-session-store är inte tillgänglig." })
    );

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.changeEmailFailed",
    });
    // toEqual tolerates an explicitly-undefined key; assert absence directly.
    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT claim email is disabled for a 503 carrying some OTHER ProblemDetails title", async () => {
    // The whitelist is exact, not "has a title" — a well-formed ProblemDetails from any
    // other producer must still fall through to the generic copy.
    authedFetchMock.mockResolvedValue(
      fakeResponse(503, { title: "Auth.RegistrationsClosed" })
    );

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.changeEmailFailed",
    });
    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT claim email is disabled for a 503 whose body is not JSON (the proxy counterfactual)", async () => {
    authedFetchMock.mockResolvedValue(fakeNonJsonResponse(503));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.changeEmailFailed",
    });
    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT refuse on a 409 carrying our title — the arm is bound to the status too", async () => {
    // Mirror of the hard constraint: the title alone must not trigger the refusal. Catches
    // a 503 arm placed ABOVE the 409 arm, which would swallow the cooldown/taken branch.
    authedFetchMock.mockResolvedValue(
      fakeResponse(409, { title: "Auth.EmailDeliveryUnavailable" })
    );

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.emailTaken",
    });
    expect(result).not.toHaveProperty("refused");
  });

  it("reads the 503 body exactly once (readProblemTitle consumes it)", async () => {
    // problem.ts:14-15 makes single-read a contract and nothing pinned it before. A second
    // read would reject on an already-consumed stream against a real Response.
    const json = vi.fn(async () => ({ error: "Redis-session-store är inte tillgänglig." }));
    authedFetchMock.mockResolvedValue({
      status: 503,
      ok: false,
      json,
    } as unknown as Response);

    await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(json).toHaveBeenCalledTimes(1);
  });

  it("maps an unexpected non-ok status to the generic change-email failure", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(500));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.changeEmailFailed",
    });
  });

  it("maps a network/fetch throw to the network error", async () => {
    authedFetchMock.mockRejectedValue(new Error("boom"));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({ success: false, error: "account.errors.network" });
  });

  it("rejects a malformed new email client-side without calling the backend", async () => {
    const result = await changeEmailAction(CURRENT, "not-an-email");

    expect(result.success).toBe(false);
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("rejects an empty current password client-side without calling the backend", async () => {
    const result = await changeEmailAction("", NEW_EMAIL);

    expect(result.success).toBe(false);
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("returns notLoggedIn when there is no session", async () => {
    getSessionIdMock.mockResolvedValue(null);

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.notLoggedIn",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});
