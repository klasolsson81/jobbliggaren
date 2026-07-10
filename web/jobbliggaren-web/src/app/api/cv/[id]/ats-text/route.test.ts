import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { GET } from "./route";

const VALID_ID = "22222222-2222-4222-8222-222222222222";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

/**
 * GET läser aldrig `request` (JSON-spegling — inga query-params), men signaturen
 * kräver en `NextRequest`. Vi konstruerar en riktig sådan för typparitet med
 * preview-syskonet (ingen `Request as unknown as NextRequest`-cast).
 */
function makeRequest(): NextRequest {
  return new NextRequest(`http://localhost/api/cv/${VALID_ID}/ats-text`);
}

function ctxFor(id: string) {
  return { params: Promise.resolve({ id }) };
}

describe("GET /api/cv/[id]/ats-text (JSON ATS-text-BFF, Fas 4b PR-8.2/8.3)", () => {
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

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["../../etc", "path-traversal-form"],
    ["abc", "icke-GUID-sträng"],
  ])(
    "404 vid ogiltigt id (%s) — backend nås aldrig (SSRF/IDOR-allowlist)",
    async (id) => {
      const fetchMock = vi.fn();
      global.fetch = fetchMock;

      const res = await GET(makeRequest(), ctxFor(id));

      expect(res.status).toBe(404);
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it("backend 401 → 401 unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
  });

  it("backend 404 → 404 utan body (ekar aldrig backend)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(404);
    expect(await res.text()).toBe("");
  });

  it("backend 429 → mappar rate-limit + Retry-After-header", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(null, { status: 429, headers: { "Retry-After": "45" } }),
      );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("45");
    expect(await res.json()).toEqual({
      error: "rateLimited",
      retryAfterSeconds: 45,
    });
  });

  it("backend 500 → 502, body EKAS ALDRIG (GDPR no-echo-pin)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          detail: "INTERN PII-LÄCKA 19900101-1234",
          title: "x",
        }),
        { status: 500, headers: { "Content-Type": "application/json" } },
      ),
    );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    const text = await res.text();
    expect(text).not.toContain("PII-LÄCKA");
    expect(text).not.toContain("19900101-1234");
    expect(JSON.parse(text)).toEqual({ error: "error" });
  });

  it("502 vid nätverksfel mot backend", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
  });

  it("200 → returnerar exakt { source, text } + no-store, Bearer- & no-store-vidarebefordran", async () => {
    // Backend skickar ett extra fält — ACL-schemat (`atsTextResponseSchema`,
    // strip-läge) ska strippa det så det ALDRIG ytas mot klienten.
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          source: "Linearized",
          text: "Ren linjäriserad CV-text",
          secretInternalField: "LEAK",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    expect(res.headers.get("Cache-Control")).toBe("no-store");

    const body = await res.text();
    expect(body).not.toContain("LEAK");
    expect(JSON.parse(body)).toEqual({
      source: "Linearized",
      text: "Ren linjäriserad CV-text",
    });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://test-backend/api/v1/resumes/${VALID_ID}/ats-text`);
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer sess-1");
    expect(init.cache).toBe("no-store");
  });

  it("backend 200 med OGILTIG form (saknar text) → 502, body ekas aldrig (zod ACL-grind)", async () => {
    // parseResponse loggar strukturerad shape-mismatch via console.error — tysta
    // den (redan redigerad för `received`, men vi håller testutdata rent).
    vi.spyOn(console, "error").mockImplementation(() => {});
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ source: "Linearized", leak: "SENTINEL-PII" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    const text = await res.text();
    expect(text).not.toContain("SENTINEL-PII");
    expect(JSON.parse(text)).toEqual({ error: "error" });
  });
});
