import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
}));

import { getJuniorItCount } from "./junior-it-count";

const originalFetch = global.fetch;

function response(status: number, body?: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    json: async () => body,
  } as unknown as Response;
}

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

// ADR 0188 (#2265), security-auditor m3: the junior quick link's count is read per request, never cached, and every
// failure is a result the hero hides the link on. The hero's Promise.all must not reject, so the reader never throws.
describe("getJuniorItCount", () => {
  it("reads the signed-in count route with no-store and a timeout of its own", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response(200, { count: 152 }));
    global.fetch = fetchMock;

    expect(await getJuniorItCount()).toEqual({ kind: "ok", data: { count: 152 } });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/job-ads/junior-it-count");
    expect(init.cache).toBe("no-store");
    expect(init.signal).toBeInstanceOf(AbortSignal);
  });

  it("answers unauthorized without a request when there is no session", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getJuniorItCount()).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("treats a member it does not know as an error, so nobody renders an unreviewed field", async () => {
    global.fetch = vi.fn().mockResolvedValue(response(200, { count: 3, extra: "x" }));

    expect(await getJuniorItCount()).toEqual({ kind: "error" });
  });

  it("treats a negative or fractional count as an error", async () => {
    global.fetch = vi.fn().mockResolvedValue(response(200, { count: -1 }));
    expect(await getJuniorItCount()).toEqual({ kind: "error" });

    global.fetch = vi.fn().mockResolvedValue(response(200, { count: 1.5 }));
    expect(await getJuniorItCount()).toEqual({ kind: "error" });
  });

  it("resolves a server error and a thrown fetch (a timeout among them) to an error result, never a rejection", async () => {
    global.fetch = vi.fn().mockResolvedValue(response(500));
    expect(await getJuniorItCount()).toEqual({ kind: "error" });

    global.fetch = vi.fn().mockRejectedValue(new DOMException("The operation timed out.", "TimeoutError"));
    expect(await getJuniorItCount()).toEqual({ kind: "error" });
  });
});
