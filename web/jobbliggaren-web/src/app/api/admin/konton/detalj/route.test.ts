import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import * as route from "./route";
import { POST } from "./route";

const ID = "00000000-0000-4000-8000-000000000001";

const DETAIL = {
  id: ID,
  email: "konto.a@example.test",
  role: "User",
  status: "Active",
  emailConfirmed: true,
  isSuspended: false,
  registeredAt: "2026-09-28T12:02:00Z",
  deletionEarliest: null,
  deletion: null,
  deletionPreview: null,
  applicationCount: 4,
  resumeCount: 2,
  savedSearchCount: 3,
};

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) => (name === "__Host-jobbliggaren_session" && value !== undefined ? { value } : undefined),
  });
}

function request(body: unknown, headers: Record<string, string> = {}): NextRequest {
  return new Request("http://localhost/api/admin/konton/detalj", {
    method: "POST",
    headers: { host: "localhost", origin: "http://localhost", "content-type": "application/json", ...headers },
    body: JSON.stringify(body),
  }) as unknown as NextRequest;
}

function backend(status: number, body: unknown = DETAIL, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
}

const fetchMock = vi.fn<(input: string, init: RequestInit) => Promise<Response>>();

beforeEach(() => {
  fetchMock.mockReset();
  cookiesMock.mockReset();
  withSession("session-1");
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function expectRefused(response: Response, status: number, error: string) {
  expect(response.status).toBe(status);
  expect(response.headers.get("cache-control")).toBe("no-store");
  expect(await response.json()).toEqual({ error });
}

describe("POST /api/admin/konton/detalj (#1974, ADR 0151)", () => {
  it("answers POST alone, so no link or prefetch can reach it", () => {
    const methods = Object.keys(route).filter((name) => /^(GET|HEAD|PUT|PATCH|DELETE|OPTIONS)$/.test(name));
    expect(methods).toEqual([]);
  });

  it("reads the account the body names and relays its details, storing nothing", async () => {
    fetchMock.mockResolvedValue(backend(200));
    const incoming = request({ id: ID });

    const response = await POST(incoming);

    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(await response.json()).toEqual(DETAIL);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(`http://test-backend/api/v1/admin/accounts/${ID}`);
    expect(init?.method ?? "GET").toBe("GET");
    expect(init?.signal).toBe(incoming.signal);
  });

  it("refuses another origin and a body that is not JSON before anything else", async () => {
    await expectRefused(await POST(request({ id: ID }, { origin: "https://elsewhere.example.test" })), 403, "forbidden");
    await expectRefused(await POST(request({ id: ID }, { "content-type": "text/plain" })), 415, "unsupported");
    expect(cookiesMock).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses an id that is not text, and answers an id that is no GUID as no account, without the backend", async () => {
    await expectRefused(await POST(request({ id: 7 })), 400, "invalid");
    await expectRefused(await POST(request({ id: "../search" })), 404, "notFound");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 401 without asking the backend when there is no session", async () => {
    withSession(undefined);

    await expectRefused(await POST(request({ id: ID })), 401, "unauthorized");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [403, "forbidden"],
    [404, "notFound"],
    [500, "error"],
  ] as const)("relays the backend's %i as a fixed code", async (status, error) => {
    fetchMock.mockResolvedValue(backend(status, { title: "x" }));

    await expectRefused(await POST(request({ id: ID })), status === 500 ? 502 : status, error);
  });

  it("answers a backend it cannot reach with the fixed code", async () => {
    fetchMock.mockRejectedValue(new Error("connect ECONNREFUSED"));

    await expectRefused(await POST(request({ id: ID })), 502, "error");
  });

  it("forwards a rate limit with its wait", async () => {
    fetchMock.mockResolvedValue(backend(429, {}, { "Retry-After": "4" }));

    const response = await POST(request({ id: ID }));

    await expectRefused(response, 429, "rateLimited");
    expect(response.headers.get("retry-after")).toBe("4");
  });
});
