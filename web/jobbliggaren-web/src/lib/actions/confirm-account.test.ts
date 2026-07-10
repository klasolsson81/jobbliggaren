import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// #714 — confirmAccountAction (PUBLIC registration-confirm). Pins: 204 -> success; a transient
// 429/5xx -> the retryable network message; every other 4xx -> the uniform "invalid link" message
// (anti-enumeration); a transport throw -> the network message; and the security invariant that the
// request carries { uid, token } (NO email — the address is not changing) and NO Authorization header
// (the link is opened logged-out). The translator mock returns the key verbatim; a bare `fetch` is
// stubbed globally; `@/lib/env` is mocked.

const fetchMock = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import { confirmAccountAction } from "./confirm-account";

const UID = "0af1b2c3d4e5460788990011aabbccdd";
const TOKEN = "Q2ZERjQ4-token-base64url";

function fakeResponse(status: number): Response {
  return { status, ok: status >= 200 && status < 300 } as unknown as Response;
}

describe("confirmAccountAction", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("returns success on 204", async () => {
    fetchMock.mockResolvedValue(fakeResponse(204));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({ success: true });
  });

  it("POSTs { uid, token } to /verify-email with NO email and NO Authorization header", async () => {
    fetchMock.mockResolvedValue(fakeResponse(204));

    await confirmAccountAction(UID, TOKEN);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://backend.test/api/v1/auth/verify-email");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(init.body).toBe(JSON.stringify({ uid: UID, token: TOKEN }));
    // No email in the body (the address is not changing) and no Bearer (opened logged-out).
    expect(init.body).not.toMatch(/email/i);
    expect(init.headers).toEqual({ "Content-Type": "application/json" });
    expect(JSON.stringify(init.headers)).not.toMatch(/authorization/i);
  });

  it("maps 400 to the uniform invalid-link message", async () => {
    fetchMock.mockResolvedValue(fakeResponse(400));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmAccount.invalidBody",
    });
  });

  it("maps a 4xx token-validity failure (410) to the same invalid-link message", async () => {
    fetchMock.mockResolvedValue(fakeResponse(410));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmAccount.invalidBody",
    });
  });

  it("maps a transient 5xx (503) to the retryable network message, not invalid-link", async () => {
    fetchMock.mockResolvedValue(fakeResponse(503));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmAccount.networkError",
    });
  });

  it("maps a rate-limit (429) to the retryable network message", async () => {
    fetchMock.mockResolvedValue(fakeResponse(429));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmAccount.networkError",
    });
  });

  it("maps a transport throw to the network message", async () => {
    fetchMock.mockRejectedValue(new Error("boom"));

    const result = await confirmAccountAction(UID, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmAccount.networkError",
    });
  });
});
