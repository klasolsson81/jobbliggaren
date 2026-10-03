import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { NextRequest } from "next/server";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";

const SESSION_ID = "session-id-that-must-not-leak";

const mocks = vi.hoisted(() => ({
  getSessionId: vi.fn<() => Promise<string | null>>(),
  deleteSessionCookie: vi.fn<() => Promise<void>>(),
  fetch: vi.fn(),
  events: [] as string[],
}));

vi.mock("@/lib/auth/session", () => ({
  getSessionId: mocks.getSessionId,
  deleteSessionCookie: mocks.deleteSessionCookie,
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import * as route from "./route";

function makeRequest(headers: Record<string, string> = {}): NextRequest {
  return new Request("http://localhost/api/auth/logout", {
    method: "POST",
    headers: { host: "localhost", origin: "http://localhost", ...headers },
  }) as unknown as NextRequest;
}

let consoleError: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  vi.stubEnv("NODE_ENV", "test");
  mocks.getSessionId.mockReset();
  mocks.deleteSessionCookie.mockReset();
  mocks.fetch.mockReset();
  mocks.events.length = 0;
  mocks.getSessionId.mockImplementation(async () => {
    mocks.events.push("read");
    return SESSION_ID;
  });
  mocks.deleteSessionCookie.mockImplementation(async () => {
    await Promise.resolve();
    mocks.events.push("delete");
  });
  mocks.fetch.mockImplementation(async () => {
    mocks.events.push("backend");
    return new Response(null, { status: 204 });
  });
  vi.stubGlobal("fetch", mocks.fetch);
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
  consoleError.mockClear();
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

function expectLoggedOutTo(response: Response) {
  expect(response.status).toBe(303);
  expect(response.headers.get("location")).toBe("/logga-in");
  expect(response.headers.get("cache-control")).toBe("no-store");
}

describe("POST /api/auth/logout (#1956)", () => {
  it("revokes the backend session with the cookie's id, then deletes the cookie and sends the browser to /logga-in", async () => {
    const response = await route.POST(makeRequest({ "x-forwarded-for": "203.0.113.7" }));

    expect(mocks.fetch).toHaveBeenCalledTimes(1);
    const [url, init] = mocks.fetch.mock.lastCall!;
    expect(url).toBe("http://backend.test/api/v1/auth/logout");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(init.headers).toEqual({ "x-forwarded-for": "203.0.113.7", Authorization: `Bearer ${SESSION_ID}` });
    expect(mocks.events).toEqual(["read", "backend", "delete"]);
    expectLoggedOutTo(response);
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("without a cookie calls no backend and still deletes the cookie and redirects", async () => {
    mocks.getSessionId.mockResolvedValue(null);

    const response = await route.POST(makeRequest());

    expect(mocks.fetch).not.toHaveBeenCalled();
    expect(mocks.deleteSessionCookie).toHaveBeenCalledTimes(1);
    expectLoggedOutTo(response);
  });

  it.each([401, 503])("logs a refused backend call (%i) by status alone, and logs the user out locally anyway", async (status) => {
    mocks.fetch.mockResolvedValue(new Response(null, { status }));

    const response = await route.POST(makeRequest());

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError.mock.lastCall).toEqual(["logout.backend_call_failed", { event: "logout", status }]);
    expect(JSON.stringify(consoleError.mock.calls)).not.toContain(SESSION_ID);
    expect(mocks.deleteSessionCookie).toHaveBeenCalledTimes(1);
    expectLoggedOutTo(response);
  });

  it("logs a thrown fetch by its message, never the session id, and logs the user out locally anyway", async () => {
    mocks.fetch.mockRejectedValue(new TypeError("fetch failed", { cause: new Error("connect ECONNREFUSED") }));

    const response = await route.POST(makeRequest());

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError.mock.lastCall).toEqual(["logout.backend_call_failed", { event: "logout", cause: "fetch failed" }]);
    expect(JSON.stringify(consoleError.mock.calls)).not.toContain(SESSION_ID);
    expect(mocks.deleteSessionCookie).toHaveBeenCalledTimes(1);
    expectLoggedOutTo(response);
  });

  it.each([null, "null", "http://foreign.test", "https://localhost", "http://localhost:3000"])(
    "refuses Origin %s before reading the session, calling the backend or touching the cookie",
    async (origin) => {
      const request = makeRequest();
      if (origin === null) request.headers.delete("origin");
      else request.headers.set("origin", origin);

      const response = await route.POST(request);

      expect(response.status).toBe(403);
      expect(response.headers.get("location")).toBeNull();
      expect(mocks.events).toEqual([]);
      expect(mocks.deleteSessionCookie).not.toHaveBeenCalled();
    }
  );

  it("answers POST alone, so no link, prefetch or image can log anyone out", () => {
    const methods = Object.keys(route).filter((name) => /^(GET|HEAD|PUT|PATCH|DELETE|OPTIONS)$/.test(name));
    expect(methods).toEqual([]);
  });

  it("lives at LOGOUT_PATH, the path every logout form posts to", () => {
    const folder = dirname(fileURLToPath(import.meta.url)).replaceAll("\\", "/");
    expect(folder.endsWith(`/src/app${LOGOUT_PATH}`)).toBe(true);
  });
});
