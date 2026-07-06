import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// #679 — confirmEmailChangeAction (PUBLIC). Pins: 204 -> success; any non-204 ->
// the uniform "invalid link" message; a transport throw -> the network message; and
// the security invariant that the request carries NO Authorization header (the link
// is opened logged-out, so no session is read). The translator mock returns the key
// verbatim. A bare `fetch` is stubbed globally; `@/lib/env` is mocked so the action
// does not require a real BACKEND_URL.

const fetchMock = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import { confirmEmailChangeAction } from "./confirm-email-change";

const UID = "0af1b2c3d4e5460788990011aabbccdd";
const EMAIL = "ny.adress@exempel.se";
const TOKEN = "Q2ZERjQ4-token-base64url";

function fakeResponse(status: number): Response {
  return { status, ok: status >= 200 && status < 300 } as unknown as Response;
}

describe("confirmEmailChangeAction", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("returns success on 204", async () => {
    fetchMock.mockResolvedValue(fakeResponse(204));

    const result = await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(result).toEqual({ success: true });
  });

  it("POSTs { uid, email, token } to the public endpoint with NO Authorization header", async () => {
    fetchMock.mockResolvedValue(fakeResponse(204));

    await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://backend.test/api/v1/auth/confirm-email-change");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(init.body).toBe(JSON.stringify({ uid: UID, email: EMAIL, token: TOKEN }));
    // The link is opened possibly logged-out — the request must never carry a Bearer.
    expect(init.headers).toEqual({ "Content-Type": "application/json" });
    expect(JSON.stringify(init.headers)).not.toMatch(/authorization/i);
  });

  it("maps 400 to the uniform invalid-link message", async () => {
    fetchMock.mockResolvedValue(fakeResponse(400));

    const result = await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmEmailChange.invalidBody",
    });
  });

  it("maps a 4xx token-validity failure (410) to the same invalid-link message", async () => {
    fetchMock.mockResolvedValue(fakeResponse(410));

    const result = await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmEmailChange.invalidBody",
    });
  });

  it("maps a transient 5xx (503) to the retryable network message, not invalid-link", async () => {
    fetchMock.mockResolvedValue(fakeResponse(503));

    const result = await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmEmailChange.networkError",
    });
  });

  it("maps a transport throw to the network message", async () => {
    fetchMock.mockRejectedValue(new Error("boom"));

    const result = await confirmEmailChangeAction(UID, EMAIL, TOKEN);

    expect(result).toEqual({
      success: false,
      error: "auth.confirmEmailChange.networkError",
    });
  });
});
