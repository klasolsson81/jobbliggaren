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

function fakeResponse(status: number): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => ({}),
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

  it("maps 409 to the email-taken error (not the generic mapActionError conflict text)", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(409));

    const result = await changeEmailAction(CURRENT, NEW_EMAIL);

    expect(result).toEqual({
      success: false,
      error: "account.errors.emailTaken",
    });
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
