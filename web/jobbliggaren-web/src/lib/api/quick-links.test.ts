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

import { setJuniorQuickLink, setRecentSearchQuickLink } from "./quick-links";

const ID = "11111111-1111-1111-1111-111111111111";
const originalFetch = global.fetch;

function response(status: number, body?: unknown, headers?: Record<string, string>): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(headers),
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

// ADR 0188 (#2265): a 409 means two things the user must hear apart, so the problem's title decides between them.
describe("quick-link writes", () => {
  it("puts { enabled } to the junior route", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response(204));
    global.fetch = fetchMock;

    expect(await setJuniorQuickLink(false)).toEqual({ kind: "ok" });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/me/junior-quick-link");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(String(init.body))).toEqual({ enabled: false });
  });

  it("marks with PUT and unmarks with DELETE, by the row's id only", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response(204));
    global.fetch = fetchMock;

    await setRecentSearchQuickLink(ID, true);
    await setRecentSearchQuickLink(ID, false);

    const calls = fetchMock.mock.calls as [string, RequestInit][];
    expect(calls.map(([url, init]) => [url, init.method, init.body])).toEqual([
      [`http://test-backend/api/v1/me/recent-searches/${ID}/quick-link`, "PUT", undefined],
      [`http://test-backend/api/v1/me/recent-searches/${ID}/quick-link`, "DELETE", undefined],
    ]);
  });

  it("refuses an id that is no GUID before it reaches the backend URL", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await setRecentSearchQuickLink("../me/profile", true)).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("tells the cap apart from a lost race by the problem's title", async () => {
    global.fetch = vi.fn().mockResolvedValue(response(409, { title: "QuickLink.MaxTotal" }));
    expect(await setRecentSearchQuickLink(ID, true)).toEqual({ kind: "full" });

    global.fetch = vi.fn().mockResolvedValue(response(409, { title: "QuickLink.SlotTaken" }));
    expect(await setRecentSearchQuickLink(ID, true)).toEqual({ kind: "conflict" });

    global.fetch = vi.fn().mockResolvedValue(response(409, { title: "Concurrency.Conflict" }));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "conflict" });

    global.fetch = vi.fn().mockResolvedValue(response(409, "not json"));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "conflict" });
  });

  it("maps the other statuses, and a thrown fetch, to their results", async () => {
    global.fetch = vi.fn().mockResolvedValue(response(404));
    expect(await setRecentSearchQuickLink(ID, false)).toEqual({ kind: "notFound" });

    global.fetch = vi.fn().mockResolvedValue(response(401));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "unauthorized" });

    global.fetch = vi.fn().mockResolvedValue(response(429, undefined, { "Retry-After": "12" }));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "rateLimited", retryAfterSeconds: 12 });

    global.fetch = vi.fn().mockResolvedValue(response(500));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "error" });

    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await setJuniorQuickLink(true)).toEqual({ kind: "error" });
  });

  it("answers unauthorized without a request when there is no session", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await setJuniorQuickLink(true)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
