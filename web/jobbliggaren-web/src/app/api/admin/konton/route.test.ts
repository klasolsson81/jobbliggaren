import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import * as route from "./route";
import { POST } from "./route";

const TERM = "konto.sentinel@example.test";

const ANSWER = {
  accounts: { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 },
  counts: { total: 0, active: 0, pendingDeletion: 0, profileMissing: 0 },
};

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) => (name === "__Host-jobbliggaren_session" && value !== undefined ? { value } : undefined),
  });
}

function request(body: string, headers: Record<string, string> = {}): NextRequest {
  return new Request("http://localhost/api/admin/konton", {
    method: "POST",
    headers: { host: "localhost", origin: "http://localhost", "content-type": "application/json", ...headers },
    body,
  }) as unknown as NextRequest;
}

const search = (body: unknown, headers?: Record<string, string>) => request(JSON.stringify(body), headers);

function backend(status: number, body: unknown = ANSWER, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

const fetchMock = vi.fn<(input: string, init: RequestInit) => Promise<Response>>();
const consoleCalls: unknown[][] = [];

beforeEach(() => {
  fetchMock.mockReset();
  cookiesMock.mockReset();
  withSession("session-1");
  vi.stubGlobal("fetch", fetchMock);
  consoleCalls.length = 0;
  for (const method of ["log", "info", "warn", "error", "debug"] as const) {
    vi.spyOn(console, method).mockImplementation((...args: unknown[]) => {
      consoleCalls.push(args);
    });
  }
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function expectRefused(response: Response, status: number, error: string) {
  expect(response.status).toBe(status);
  expect(response.headers.get("cache-control")).toBe("no-store");
  const text = await response.text();
  expect(JSON.parse(text)).toEqual({ error });
  expect(text).not.toContain(TERM);
}

describe("POST /api/admin/konton (#1974, ADR 0151)", () => {
  it("answers POST alone, so no link or prefetch can reach it", () => {
    const methods = Object.keys(route).filter((name) => /^(GET|HEAD|PUT|PATCH|DELETE|OPTIONS)$/.test(name));
    expect(methods).toEqual([]);
  });

  it("relays the backend's page with the term in its body, never in a URL, and stores nothing", async () => {
    fetchMock.mockResolvedValue(backend(200));
    const incoming = search({ address: TERM, status: "ProfileMissing", sort: "AddressAscending", page: 2, pageSize: 50 });

    const response = await POST(incoming);

    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(await response.json()).toEqual(ANSWER);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe("http://test-backend/api/v1/admin/accounts/search");
    expect(url).not.toContain("konto");
    expect(init?.method).toBe("POST");
    expect(JSON.parse(String(init?.body))).toEqual({
      address: TERM,
      status: "ProfileMissing",
      sort: "AddressAscending",
      page: 2,
      pageSize: 50,
    });
    expect(init?.signal).toBe(incoming.signal);
  });

  it("passes on a term as long as an address may be", async () => {
    fetchMock.mockResolvedValue(backend(200));
    const longest = `${"q".repeat(256 - TERM.length)}${TERM}`;

    expect((await POST(search({ address: longest }))).status).toBe(200);
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)).address).toBe(longest);
  });

  it("sends the defaults when the island leaves them out", async () => {
    fetchMock.mockResolvedValue(backend(200));

    await POST(search({}));

    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({ sort: "RegisteredNewest", page: 1, pageSize: 25 });
  });

  it.each([
    ["another origin", { origin: "https://elsewhere.example.test" }],
    ["no origin", { origin: "" }],
  ])("refuses %s before it reads the session or the backend", async (_, headers) => {
    const response = await POST(search({ address: TERM }, headers));

    await expectRefused(response, 403, "forbidden");
    expect(cookiesMock).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses any body that is not JSON by its content type", async () => {
    const response = await POST(search({ address: TERM }, { "content-type": "text/plain" }));

    await expectRefused(response, 415, "unsupported");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["text that does not parse", `{"address": "${TERM}"`],
    ["an array", JSON.stringify([TERM])],
    ["a term longer than an address may be", JSON.stringify({ address: `${"q".repeat(257 - TERM.length)}${TERM}` })],
    ["a term that is not text", JSON.stringify({ address: 7 })],
    ["a status the backend does not filter by", JSON.stringify({ address: TERM, status: "Suspended" })],
    ["an unknown sort", JSON.stringify({ address: TERM, sort: "Newest" })],
    ["page 0", JSON.stringify({ address: TERM, page: 0 })],
    ["a page that is not a whole number", JSON.stringify({ address: TERM, page: "2" })],
    ["more than 100 rows", JSON.stringify({ address: TERM, pageSize: 101 })],
  ])("refuses %s with a fixed code that never echoes the term", async (_, body) => {
    const response = await POST(request(body));

    await expectRefused(response, 400, "invalid");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 401 without asking the backend when there is no session", async () => {
    withSession(undefined);

    const response = await POST(search({ address: TERM }));

    await expectRefused(response, 401, "unauthorized");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("takes the backend's 403 as the authority, with no rows", async () => {
    fetchMock.mockResolvedValue(backend(403, { title: "Forbidden" }));

    await expectRefused(await POST(search({ address: TERM })), 403, "forbidden");
  });

  it("forwards a rate limit with its wait", async () => {
    fetchMock.mockResolvedValue(backend(429, {}, { "Retry-After": "7" }));

    const response = await POST(search({ address: TERM }));

    await expectRefused(response, 429, "rateLimited");
    expect(response.headers.get("retry-after")).toBe("7");
  });

  it("answers a backend it cannot reach with the fixed code, and writes the term nowhere", async () => {
    fetchMock.mockRejectedValue(new Error(`connect ECONNREFUSED while sending ${TERM}`));

    await expectRefused(await POST(search({ address: TERM })), 502, "error");
    expect(JSON.stringify(consoleCalls)).not.toContain(TERM);
  });

  it("answers any other failure with one fixed code", async () => {
    fetchMock.mockResolvedValue(backend(500, { detail: `boom ${TERM}` }));

    await expectRefused(await POST(search({ address: TERM })), 502, "error");
  });

  it("writes the term to no console line on any path", async () => {
    fetchMock.mockResolvedValueOnce(backend(200));
    fetchMock.mockResolvedValueOnce(backend(500, { detail: TERM }));
    fetchMock.mockResolvedValueOnce(backend(200, { accounts: TERM }));

    await POST(search({ address: TERM }));
    await POST(search({ address: TERM }));
    await POST(search({ address: TERM }));
    await POST(request(`{"address": "${TERM}"`));
    await POST(search({ address: TERM, status: "Suspended" }));

    expect(JSON.stringify(consoleCalls)).not.toContain(TERM);
  });
});
