import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";
import {
  SESSION_COOKIE_NAME,
  REFRESH_AFTER_COOKIE_NAME,
  PERSISTENT_MAX_AGE_SECONDS,
} from "@/lib/auth/cookie-names";

// The refresh driver (PR2b-3b, epic #481) is security-critical: it rotates the
// session id, re-sets the cookie, and forwards the new id to the downstream render.
// These tests pin every branch the CTO/security gate cares about.

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

// isProtectedPath is exercised for real (pure string logic) — /oversikt is protected,
// /cv-granskning is a public sibling.
import { proxy } from "./proxy";

const fetchMock = vi.fn<typeof fetch>();

function makeRequest(
  path: string,
  opts?: { cookies?: Record<string, string>; headers?: Record<string, string> }
): NextRequest {
  const headers = new Headers(opts?.headers);
  if (opts?.cookies) {
    const cookieStr = Object.entries(opts.cookies)
      .map(([k, v]) => `${k}=${v}`)
      .join("; ");
    headers.set("cookie", cookieStr);
  }
  return new NextRequest(`https://app.test${path}`, { headers });
}

function refreshResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

const nowSeconds = () => Math.floor(Date.now() / 1000);

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("proxy refresh driver (PR2b-3b)", () => {
  it("rotated:true — re-sets the session cookie to the new id with a finite Max-Age, rewrites the request cookie, sets no-store, advances the throttle", async () => {
    fetchMock.mockResolvedValue(
      refreshResponse({ rotated: true, sessionId: "NEWID", expiresAt: "2026-12-31T00:00:00Z" })
    );

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID", foo: "bar" },
      })
    );

    // Refresh called once, with the OLD id as the bearer token.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://backend.test/api/v1/auth/refresh",
      expect.objectContaining({
        method: "POST",
        cache: "no-store",
        headers: { Authorization: "Bearer OLDID" },
      })
    );

    // Response Set-Cookie carries the NEW id with the persistent (finite) Max-Age.
    const sessionCookie = res.cookies.get(SESSION_COOKIE_NAME);
    expect(sessionCookie?.value).toBe("NEWID");
    expect(sessionCookie?.maxAge).toBe(PERSISTENT_MAX_AGE_SECONDS);
    expect(sessionCookie?.httpOnly).toBe(true);
    expect(sessionCookie?.secure).toBe(true);
    expect(sessionCookie?.sameSite).toBe("strict");
    expect(sessionCookie?.path).toBe("/");

    // Downstream request cookie is rewritten so the SC render reads the NEW id,
    // while other cookies survive.
    expect(res.headers.get("x-middleware-override-headers")).toContain("cookie");
    const rewritten = res.headers.get("x-middleware-request-cookie");
    expect(rewritten).toContain(`${SESSION_COOKIE_NAME}=NEWID`);
    expect(rewritten).toContain("foo=bar");
    expect(rewritten).not.toContain("OLDID");

    // A rotated Set-Cookie must never be cached.
    expect(res.headers.get("cache-control")).toBe("no-store");

    // Throttle advanced to a future epoch.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value)).toBeGreaterThan(nowSeconds());
    expect(throttle?.maxAge).toBeGreaterThan(0);
  });

  it("rotated:false — leaves the session cookie untouched, advances the throttle, sets no-store", async () => {
    fetchMock.mockResolvedValue(
      refreshResponse({ rotated: false, sessionId: null, expiresAt: "2026-12-31T00:00:00Z" })
    );

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    // No Set-Cookie for the session, no request-cookie rewrite.
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.headers.get("x-middleware-override-headers")).toBeNull();
    // Throttle advanced, response uncacheable.
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBeDefined();
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("throttle not due — no backend call, plain passthrough", async () => {
    const future = String(nowSeconds() + 999);

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: future,
        },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)).toBeUndefined();
  });

  it("prefetch (next-router-prefetch) — no backend call, plain passthrough", async () => {
    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID" },
        headers: { "next-router-prefetch": "1" },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("prefetch (purpose: prefetch) — no backend call, plain passthrough", async () => {
    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: { [SESSION_COOKIE_NAME]: "OLDID" },
        headers: { purpose: "prefetch" },
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("refresh 401 — swallowed, navigation not broken, session cookie untouched, throttle backed off briefly", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    // On failure the throttle is advanced by a SHORT backoff (not the full window) so a
    // degraded backend is not re-hit on every navigation, while recovery stays prompt.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value) - nowSeconds()).toBeGreaterThan(0);
    expect(Number(throttle!.value) - nowSeconds()).toBeLessThanOrEqual(30);
    expect(throttle?.maxAge).toBeLessThanOrEqual(30);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("refresh network error — swallowed, navigation not broken, throttle backed off briefly", async () => {
    fetchMock.mockRejectedValue(new Error("network down"));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.headers.get("x-middleware-next")).toBe("1");
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(throttle?.value).toBeDefined();
    expect(Number(throttle!.value) - nowSeconds()).toBeLessThanOrEqual(30);
  });

  it("rotated:true but sessionId missing — falls to the slide branch, session cookie untouched, throttle advanced", async () => {
    // Defensive: a malformed rotated:true without an id must NOT clear or corrupt the
    // cookie — it is treated as a plain slide.
    fetchMock.mockResolvedValue(refreshResponse({ rotated: true, sessionId: null }));

    const res = await proxy(
      makeRequest("/oversikt", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.cookies.get(SESSION_COOKIE_NAME)).toBeUndefined();
    expect(res.headers.get("x-middleware-override-headers")).toBeNull();
    // Full-window throttle (a successful, non-rotating refresh), not the error backoff.
    const throttle = res.cookies.get(REFRESH_AFTER_COOKIE_NAME);
    expect(Number(throttle!.value) - nowSeconds()).toBeGreaterThan(60);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("throttle elapsed (past epoch) — refresh is due, backend called", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false, sessionId: null }));

    const res = await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: String(nowSeconds() - 5),
        },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value).toBeDefined();
  });

  it("throttle cookie is junk (non-numeric) — treated as due, backend called", async () => {
    fetchMock.mockResolvedValue(refreshResponse({ rotated: false, sessionId: null }));

    await proxy(
      makeRequest("/oversikt", {
        cookies: {
          [SESSION_COOKIE_NAME]: "OLDID",
          [REFRESH_AFTER_COOKIE_NAME]: "not-a-number",
        },
      })
    );

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("unauthenticated on a protected path — redirects to /logga-in, no backend call", async () => {
    const res = await proxy(makeRequest("/oversikt"));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.status).toBe(307);
    const location = res.headers.get("location");
    expect(location).toContain("/logga-in");
    expect(location).toContain("next=%2Foversikt");
  });

  it("non-protected path — plain passthrough, no backend call even when authenticated", async () => {
    const res = await proxy(
      makeRequest("/cv-granskning", { cookies: { [SESSION_COOKIE_NAME]: "OLDID" } })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(res.headers.get("x-middleware-next")).toBe("1");
  });
});
