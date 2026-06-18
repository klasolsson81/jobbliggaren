import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { GET } from "./route";

const VALID_ID = "11111111-1111-4111-8111-111111111111";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

/**
 * GET läser `request.nextUrl.searchParams` — en plain `Request as unknown as
 * NextRequest` saknar `.nextUrl` i denna test-miljö (verifierat), så vi
 * konstruerar en riktig `NextRequest` från next/server (den exponerar `.nextUrl`).
 */
function makeRequest(query = ""): NextRequest {
  return new NextRequest(
    `http://localhost/api/cv/parsed/${VALID_ID}/preview${query}`
  );
}

function ctxFor(parsedId: string) {
  return { params: Promise.resolve({ parsedId }) };
}

describe("GET /api/cv/parsed/[parsedId]/preview (binär PDF-passthrough BFF)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    withSession("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    cookiesMock.mockReset();
  });

  it("401 utan session — backend nås aldrig", async () => {
    withSession(undefined);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest("?profile=Visual"), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("404 vid ogiltigt GUID i parsedId — backend nås aldrig (SSRF/path-injektions-barriär)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest("?profile=Visual"), ctxFor("not-a-guid"));

    expect(res.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 → strömmar PDF med FÄRSKA headers + Bearer-vidarebefordran (?profile=Visual)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response("pdf-bytes", { status: 200 }));
    global.fetch = fetchMock;

    const res = await GET(makeRequest("?profile=Visual"), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toBe("application/pdf");
    expect(res.headers.get("Content-Disposition")).toBe(
      'inline; filename="cv-visual.pdf"'
    );
    expect(res.headers.get("Cache-Control")).toBe("no-store");

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/render?profile=Visual`
    );
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer sess-1");
  });

  it("saknad profil → upstream använder ?profile=Ats (normalisering, inte tyst korruption)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response("pdf-bytes", { status: 200 }));
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Disposition")).toBe(
      'inline; filename="cv-ats.pdf"'
    );
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/render?profile=Ats`
    );
  });

  it("ogiltig profil → upstream faller till ?profile=Ats", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response("pdf-bytes", { status: 200 }));
    global.fetch = fetchMock;

    await GET(makeRequest("?profile=xyz"), ctxFor(VALID_ID));

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/render?profile=Ats`
    );
  });

  it("429 → mappar rate-limit + Retry-After-header", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(null, { status: 429, headers: { "Retry-After": "45" } })
      );

    const res = await GET(makeRequest("?profile=Ats"), ctxFor(VALID_ID));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("45");
    expect(await res.json()).toEqual({
      error: "rateLimited",
      retryAfterSeconds: 45,
    });
  });

  it("backend 404 → 404 utan body (ekar aldrig backend)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    const res = await GET(makeRequest("?profile=Ats"), ctxFor(VALID_ID));

    expect(res.status).toBe(404);
    expect(await res.text()).toBe("");
  });

  it("backend 401 → 401 unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));

    const res = await GET(makeRequest("?profile=Ats"), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
  });

  it("backend non-ok med PII-bärande ProblemDetails → 502, body EKAS ALDRIG", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          detail: "INTERN PII-LÄCKA 19900101-1234",
          title: "x",
        }),
        { status: 500, headers: { "Content-Type": "application/json" } }
      )
    );

    const res = await GET(makeRequest("?profile=Ats"), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    const text = await res.text();
    expect(text).not.toContain("PII-LÄCKA");
    expect(text).not.toContain("19900101-1234");
    expect(JSON.parse(text)).toEqual({ error: "error" });
  });

  it("502 vid nätverksfel mot backend", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const res = await GET(makeRequest("?profile=Ats"), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
  });
});
