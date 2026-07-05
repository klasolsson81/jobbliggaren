import { describe, it, expect, vi, beforeEach } from "vitest";

// #678 — changePasswordAction. Pins the security-relevant branches the card test
// can't reach: the C6 cookie re-issue (setSessionCookie with the new id +
// persistence) and the 401/400 -> Swedish-error mapping. The translator mock
// returns the key verbatim, so assertions check the resolved message key.

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

import { changePasswordAction } from "./me";

const CURRENT = "Current123456";
const NEW = "NyttL0senord123456";

function fakeResponse(status: number, body?: unknown): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

describe("changePasswordAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getSessionIdMock.mockResolvedValue("sess-current");
  });

  it("re-sets the session cookie to the re-issued id (persistent) on success", async () => {
    authedFetchMock.mockResolvedValue(
      fakeResponse(200, { sessionId: "sess-new", persistent: true }),
    );

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result).toEqual({ success: true });
    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-new", true);
  });

  it("re-sets a non-persistent cookie when the re-issued session is session-scoped", async () => {
    authedFetchMock.mockResolvedValue(
      fakeResponse(200, { sessionId: "sess-new", persistent: false }),
    );

    await changePasswordAction(CURRENT, NEW);

    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-new", false);
  });

  it("maps 401 to the wrong-password error and sets no cookie", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(401));

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result).toEqual({
      success: false,
      error: "account.errors.wrongPassword",
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("maps 400 to the invalid-input error and sets no cookie", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(400));

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result).toEqual({
      success: false,
      error: "account.errors.invalidInput",
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("maps an unexpected non-ok status to a generic failure and sets no cookie", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(500));

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result.success).toBe(false);
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("maps a network/fetch throw to the network error and sets no cookie", async () => {
    authedFetchMock.mockRejectedValue(new Error("boom"));

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result).toEqual({ success: false, error: "account.errors.network" });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
  });

  it("rejects a too-short new password client-side without calling the backend", async () => {
    const result = await changePasswordAction(CURRENT, "short");

    expect(result.success).toBe(false);
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("returns notLoggedIn when there is no session", async () => {
    getSessionIdMock.mockResolvedValue(null);

    const result = await changePasswordAction(CURRENT, NEW);

    expect(result).toEqual({
      success: false,
      error: "account.errors.notLoggedIn",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});
