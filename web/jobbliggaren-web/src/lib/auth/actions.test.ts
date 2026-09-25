import { beforeEach, describe, expect, it, vi } from "vitest";

const SESSION_ID = "session-id-that-must-not-leak";
const FORWARDED = { "x-forwarded-for": "203.0.113.7" };

const mocks = vi.hoisted(() => ({
  redirect: vi.fn((path: string) => {
    // Next's redirect() throws; a mock that returned would let code after it run and hide an ordering
    // defect.
    throw new Error(`REDIRECT:${path}`);
  }),
  deleteSessionCookie: vi.fn(),
  cookieValue: vi.fn<() => string | undefined>(),
  fetch: vi.fn(),
  events: [] as string[],
}));

vi.mock("next/navigation", () => ({ redirect: mocks.redirect }));
vi.mock("next/headers", () => ({
  cookies: async () => ({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && mocks.cookieValue() !== undefined
        ? { name, value: mocks.cookieValue() }
        : undefined,
  }),
}));
vi.mock("@/lib/auth/session", () => ({ deleteSessionCookie: mocks.deleteSessionCookie }));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("@/lib/http/forwarded-headers", () => ({ forwardedHeaders: async () => FORWARDED }));

import { logoutAction } from "./actions";

/** Runs the action and returns where it redirected; it always redirects. */
async function run(): Promise<string> {
  try {
    await logoutAction();
  } catch (error) {
    const match = /^REDIRECT:(.*)$/.exec((error as Error).message);
    if (match) return match[1]!;
    throw error;
  }
  throw new Error("logoutAction returned without redirecting");
}

let consoleError: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  mocks.redirect.mockClear();
  mocks.deleteSessionCookie.mockReset();
  mocks.cookieValue.mockReset();
  mocks.fetch.mockReset();
  mocks.events.length = 0;
  mocks.deleteSessionCookie.mockImplementation(async () => {
    await Promise.resolve();
    mocks.events.push("delete");
  });
  mocks.redirect.mockImplementation((path: string) => {
    mocks.events.push("redirect");
    throw new Error(`REDIRECT:${path}`);
  });
  vi.stubGlobal("fetch", mocks.fetch);
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
  // spyOn on an already-spied method returns the same spy with its earlier calls.
  consoleError.mockClear();
});

describe("logoutAction", () => {
  it("revokes the backend session with the cookie's id, then deletes the cookie and lands on /logga-in", async () => {
    mocks.cookieValue.mockReturnValue(SESSION_ID);
    mocks.fetch.mockResolvedValue(new Response(null, { status: 204 }));

    const destination = await run();

    expect(mocks.fetch).toHaveBeenCalledTimes(1);
    const [url, init] = mocks.fetch.mock.lastCall!;
    expect(url).toBe("http://backend.test/api/v1/auth/logout");
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ ...FORWARDED, Authorization: `Bearer ${SESSION_ID}` });
    expect(mocks.events).toEqual(["delete", "redirect"]);
    expect(destination).toBe("/logga-in");
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("without a cookie calls no backend and still deletes and redirects", async () => {
    mocks.cookieValue.mockReturnValue(undefined);

    const destination = await run();

    expect(mocks.fetch).not.toHaveBeenCalled();
    expect(mocks.events).toEqual(["delete", "redirect"]);
    expect(destination).toBe("/logga-in");
  });

  it.each([401, 503])("logs a refused backend call (%i) by status alone, and logs the user out locally anyway", async (status) => {
    mocks.cookieValue.mockReturnValue(SESSION_ID);
    mocks.fetch.mockResolvedValue(new Response(null, { status }));

    const destination = await run();

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError.mock.lastCall).toEqual([
      "logout.backend_call_failed",
      { event: "logout", status },
    ]);
    expect(JSON.stringify(consoleError.mock.calls)).not.toContain(SESSION_ID);
    expect(mocks.events).toEqual(["delete", "redirect"]);
    expect(destination).toBe("/logga-in");
  });

  it("logs a thrown fetch by its message, never the session id, and logs the user out locally anyway", async () => {
    mocks.cookieValue.mockReturnValue(SESSION_ID);
    mocks.fetch.mockRejectedValue(new TypeError("fetch failed", { cause: new Error("connect ECONNREFUSED") }));

    const destination = await run();

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError.mock.lastCall).toEqual([
      "logout.backend_call_failed",
      { event: "logout", cause: "fetch failed" },
    ]);
    expect(JSON.stringify(consoleError.mock.calls)).not.toContain(SESSION_ID);
    expect(mocks.events).toEqual(["delete", "redirect"]);
    expect(destination).toBe("/logga-in");
  });
});
