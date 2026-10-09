import { beforeEach, describe, expect, it, vi } from "vitest";

const { getSessionId, authedFetch } = vi.hoisted(() => ({ getSessionId: vi.fn(), authedFetch: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch }));
vi.mock("react", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react")>()),
  // The request scope that dedupes the read does not exist in a unit test; each call reads afresh.
  cache: <T,>(fn: T) => fn,
}));

import { getFeedbackPromptState } from "./feedback";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

beforeEach(() => {
  getSessionId.mockReset().mockResolvedValue("session");
  authedFetch.mockReset();
});

describe("getFeedbackPromptState", () => {
  it("makes no request without a session", async () => {
    getSessionId.mockResolvedValue(null);
    expect(await getFeedbackPromptState()).toEqual({ kind: "unavailable" });
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("reads an open state", async () => {
    authedFetch.mockResolvedValue(json(200, { open: true, answeredPages: ["jobs", "cv-review"] }));
    expect(await getFeedbackPromptState()).toEqual({ kind: "open", answered: ["jobs", "cv-review"] });
    expect(authedFetch).toHaveBeenCalledWith("session", "/api/v1/me/feedback/prompt-state", expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("reads a closed state", async () => {
    authedFetch.mockResolvedValue(json(200, { open: false, answeredPages: [] }));
    expect(await getFeedbackPromptState()).toEqual({ kind: "closed" });
  });

  it.each([
    ["a rate-limited read", () => Promise.resolve(new Response(null, { status: 429 }))],
    ["a signed-out read", () => Promise.resolve(new Response(null, { status: 401 }))],
    ["a server error", () => Promise.resolve(json(500, { title: "x" }))],
    ["a timeout", () => Promise.reject(new DOMException("timed out", "TimeoutError"))],
  ])("reads %s as unavailable, never as an error", async (_, answer) => {
    authedFetch.mockImplementation(answer);
    expect(await getFeedbackPromptState()).toEqual({ kind: "unavailable" });
  });
});
