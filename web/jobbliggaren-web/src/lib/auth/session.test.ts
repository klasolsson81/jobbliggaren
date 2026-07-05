import { describe, it, expect, vi, beforeEach } from "vitest";

// PR2b-3b: setSessionCookie(id, persistent) chooses the cookie lifetime.
//  - persistent=true  → finite Max-Age (the 180d absolute cap) so the login
//    survives a browser restart (never an infinite cookie).
//  - persistent=false → Max-Age omitted → a session cookie dropped on browser
//    close (privacy-by-default, Art. 25(2)).
// Both branches must keep the non-negotiable __Host- attributes.

const { cookieSetMock } = vi.hoisted(() => ({
  cookieSetMock:
    vi.fn<(name: string, value: string, opts: Record<string, unknown>) => void>(),
}));

vi.mock("next/headers", () => ({
  cookies: vi.fn(async () => ({
    set: cookieSetMock,
    get: vi.fn(),
    delete: vi.fn(),
  })),
}));

import { setSessionCookie, SESSION_COOKIE_NAME } from "./session";
import { PERSISTENT_MAX_AGE_SECONDS } from "./cookie-names";

const HOST_ATTRS = {
  httpOnly: true,
  secure: true,
  sameSite: "strict",
  path: "/",
};

describe("setSessionCookie cookie-lifetime branch (PR2b-3b)", () => {
  beforeEach(() => {
    cookieSetMock.mockClear();
  });

  it("persistent=true sets a finite Max-Age (the 180d cap) and keeps __Host- attrs", async () => {
    await setSessionCookie("sess-abc", true);

    expect(cookieSetMock).toHaveBeenCalledTimes(1);
    const [name, value, opts] = cookieSetMock.mock.calls[0]!;
    expect(name).toBe(SESSION_COOKIE_NAME);
    expect(value).toBe("sess-abc");
    expect(opts).toMatchObject(HOST_ATTRS);
    // Finite ceiling, not an infinite cookie.
    expect(opts).toHaveProperty("maxAge", PERSISTENT_MAX_AGE_SECONDS);
    expect(Number.isFinite(PERSISTENT_MAX_AGE_SECONDS)).toBe(true);
  });

  it("persistent=false omits Max-Age (a session cookie) but keeps __Host- attrs", async () => {
    await setSessionCookie("sess-xyz", false);

    expect(cookieSetMock).toHaveBeenCalledTimes(1);
    const [name, value, opts] = cookieSetMock.mock.calls[0]!;
    expect(name).toBe(SESSION_COOKIE_NAME);
    expect(value).toBe("sess-xyz");
    expect(opts).toMatchObject(HOST_ATTRS);
    // No Max-Age → the browser drops the cookie on close.
    expect(opts).not.toHaveProperty("maxAge");
  });
});
