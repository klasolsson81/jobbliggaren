import { describe, it, expect, vi, beforeEach } from "vitest";

// #1171 — requestPasswordResetAction. The load-bearing tests here are the three
// COUNTERFACTUALS on the 503 arm: this route has other 503 producers, and an arm that
// keyed on the status alone would print "email delivery is off" during an unrelated
// outage and mask it. me.change-email.test.ts pins the same pair for the same reason;
// do not relax either to a bare status check. The translator mock returns the key
// verbatim, so assertions check the resolved key.

const { fetchMock } = vi.hoisted(() => ({ fetchMock: vi.fn() }));

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/http/forwarded-headers", () => ({
  forwardedHeaders: async () => ({}),
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://api.test" } }));

import { requestPasswordResetAction } from "./forgot-password";

const EMAIL = "nagon@exempel.se";

function form(email: string): FormData {
  const fd = new FormData();
  fd.set("email", email);
  return fd;
}

function fakeResponse(status: number, body: unknown = {}): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

/** A body that is not JSON at all — what a reverse proxy answers with (an HTML error page). */
function fakeNonJsonResponse(status: number): Response {
  return {
    status,
    ok: false,
    json: async () => {
      throw new SyntaxError("Unexpected token '<', \"<html>\"... is not valid JSON");
    },
  } as unknown as Response;
}

describe("requestPasswordResetAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("returns success on the backend's uniform 202", async () => {
    fetchMock.mockResolvedValueOnce(fakeResponse(202));

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).toEqual({ success: true });
    expect(fetchMock).toHaveBeenCalledWith(
      "http://api.test/api/v1/auth/forgot-password",
      expect.objectContaining({ method: "POST", cache: "no-store" }),
    );
  });

  it("sends no Authorization header — the requester has no session by definition", async () => {
    fetchMock.mockResolvedValueOnce(fakeResponse(202));

    await requestPasswordResetAction(null, form(EMAIL));

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(init.headers).not.toHaveProperty("Authorization");
  });

  it("refuses when a 503 carries the exact delivery title", async () => {
    fetchMock.mockResolvedValueOnce(
      fakeResponse(503, { title: "Auth.EmailDeliveryUnavailable" }),
    );

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).toEqual({
      success: false,
      refused: true,
      error: "auth.actions.emailDeliveryUnavailable",
    });
  });

  it("does NOT refuse on the Redis 503, whose body carries no title key", async () => {
    // COUNTERFACTUAL 1. SessionStoreUnavailableException is mapped pipeline-wide in Program.cs and
    // writes `{ error }` — no `title`. A status-only arm would call an outage a config problem.
    fetchMock.mockResolvedValueOnce(
      fakeResponse(503, { error: "Session store unavailable" }),
    );

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).toEqual({
      success: false,
      error: "auth.actions.passwordResetFailed",
      values: { email: EMAIL },
    });
    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT refuse on a 503 whose body is not JSON", async () => {
    // COUNTERFACTUAL 2. A reverse proxy in front of the API answers with HTML; res.json() rejects.
    fetchMock.mockResolvedValueOnce(fakeNonJsonResponse(503));

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).toEqual({
      success: false,
      error: "auth.actions.passwordResetFailed",
      values: { email: EMAIL },
    });
    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT refuse on a 503 carrying a foreign title", async () => {
    fetchMock.mockResolvedValueOnce(
      fakeResponse(503, { title: "Auth.RegistrationsClosed" }),
    );

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).not.toHaveProperty("refused");
  });

  it("does NOT refuse on a 429 that carries our own title", async () => {
    // COUNTERFACTUAL 3, the conjunctive half from the other side: the gate is status AND title, so a
    // non-503 carrying the title must not trip it. (me.change-email.test.ts pins the same with a 409.)
    fetchMock.mockResolvedValueOnce(
      fakeResponse(429, { title: "Auth.EmailDeliveryUnavailable" }),
    );

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).not.toHaveProperty("refused");
    expect(result).toEqual({
      success: false,
      error: "auth.actions.passwordResetFailed",
      values: { email: EMAIL },
    });
  });

  it("reads the 503 body exactly once", async () => {
    // readProblemTitle consumes the body; a second read would throw on a real Response.
    const json = vi.fn(async () => ({ title: "Auth.EmailDeliveryUnavailable" }));
    fetchMock.mockResolvedValueOnce({ status: 503, ok: false, json } as unknown as Response);

    await requestPasswordResetAction(null, form(EMAIL));

    expect(json).toHaveBeenCalledTimes(1);
  });

  it("returns the network message when the transport throws", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));

    const result = await requestPasswordResetAction(null, form(EMAIL));

    expect(result).toEqual({
      success: false,
      error: "auth.actions.serverUnreachable",
      values: { email: EMAIL },
    });
  });

  it("rejects an empty address without calling the backend", async () => {
    const result = await requestPasswordResetAction(null, form("   "));

    expect(result).toEqual({
      success: false,
      error: "auth.actions.passwordResetEmailRequired",
      // The trimmed address, which is the empty string here — the form re-seeds what was sent.
      values: { email: "" },
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
