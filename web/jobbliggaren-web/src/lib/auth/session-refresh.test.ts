import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest, NextResponse } from "next/server";
import { REFRESH_AFTER_COOKIE_NAME, SESSION_COOKIE_NAME } from "./cookie-names";
import { forwardedWithoutSession, refreshSession, writeRefreshCookies } from "./session-refresh";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

const fetchMock = vi.fn<typeof fetch>();
const NOW = 1_800_000_000;

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

const request = (headers: Record<string, string> = {}) => new NextRequest("https://app.test/junior", { headers });
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

describe("refreshSession", () => {
  it.each<[string, Record<string, string>]>([
    ["a prefetch", { "sec-purpose": "prefetch" }],
    ["a refresh not yet due", { cookie: `${REFRESH_AFTER_COOKIE_NAME}=${NOW + 1}` }],
  ])("calls nothing for %s", async (_label, headers) => {
    expect(await refreshSession(request(headers), "sess-1", NOW)).toEqual({ kind: "notDue" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each<[string, () => Promise<Response>, unknown]>([
    ["a rotation with a new id", async () => json({ rotated: true, sessionId: "sess-2" }), { kind: "rotated", sessionId: "sess-2" }],
    ["a slide", async () => json({ rotated: false, sessionId: null }), { kind: "slid" }],
    ["a rotation without an id, which slides", async () => json({ rotated: true }), { kind: "slid" }],
    ["a malformed body", async () => json({ unexpected: 1 }), { kind: "failed" }],
    ["exactly 401", async () => new Response(null, { status: 401 }), { kind: "unknownSession" }],
    ["a 429", async () => new Response(null, { status: 429 }), { kind: "failed" }],
    ["a 503", async () => new Response(null, { status: 503 }), { kind: "failed" }],
    ["a network error", async () => Promise.reject(new TypeError("fetch failed")), { kind: "failed" }],
  ])("reports %s", async (_label, answer, expected) => {
    fetchMock.mockImplementation(answer);

    expect(await refreshSession(request({ cookie: `${REFRESH_AFTER_COOKIE_NAME}=${NOW - 1}` }), "sess-1", NOW)).toEqual(expected);
    expect(fetchMock.mock.calls[0]?.[0]).toBe("http://backend.test/api/v1/auth/refresh");
  });
});

describe("writeRefreshCookies", () => {
  const written = (kind: Parameters<typeof writeRefreshCookies>[1]) => writeRefreshCookies(NextResponse.next(), kind, NOW);

  it("writes nothing, not even no-store, when nothing was due", () => {
    const res = written({ kind: "notDue" });

    expect(res.headers.get("set-cookie")).toBeNull();
    expect(res.headers.get("cache-control")).toBeNull();
  });

  it("advances the throttle by the window on a slide, and by the short backoff on a failure", () => {
    expect(written({ kind: "slid" }).cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBe(String(NOW + 15 * 60));
    expect(written({ kind: "failed" }).cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBe(String(NOW + 30));
  });

  it("sets a rotated id as a persistent __Host- cookie beside the throttle", () => {
    const res = written({ kind: "rotated", sessionId: "sess-2" });
    const header = res.headers.get("set-cookie") ?? "";

    expect(res.cookies.get(SESSION_COOKIE_NAME)?.value).toBe("sess-2");
    expect(header).toMatch(/Secure/);
    expect(header).toMatch(/HttpOnly/i);
    expect(header).toMatch(/SameSite=strict/i);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("clears both cookies on an unknown session, uncacheable", () => {
    const res = written({ kind: "unknownSession" });

    expect(res.cookies.get(SESSION_COOKIE_NAME)?.value).toBe("");
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBe("");
    expect(res.headers.get("set-cookie")).toMatch(/Max-Age=0/);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });
});

describe("forwardedWithoutSession", () => {
  it("drops the session cookie and keeps every other cookie and header", () => {
    const forwarded = new Headers({
      cookie: `NEXT_LOCALE=en; ${SESSION_COOKIE_NAME}=dead-1; theme=light`,
      "x-nonce": "n0nce",
    });
    const headers = forwardedWithoutSession(forwarded);

    expect(headers.get("cookie")).toBe("NEXT_LOCALE=en; theme=light");
    expect(headers.get("x-nonce")).toBe("n0nce");
    expect(forwarded.get("cookie")).toContain("dead-1");
  });

  it("removes the header when the session cookie was the only one", () => {
    expect(forwardedWithoutSession(new Headers({ cookie: `${SESSION_COOKIE_NAME}=dead-1` })).has("cookie")).toBe(false);
  });
});
