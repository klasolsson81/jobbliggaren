import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { GET } from "./route";

const VALID_ID = "22222222-2222-4222-8222-222222222222";
const ALL_PARAMS = "?template=Klar&accent=NavyBlue&font=Modern&density=Normal";

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
 * NextRequest` saknar `.nextUrl`, så vi konstruerar en riktig `NextRequest`
 * (paritet med `[id]/preview/route.test.ts`).
 */
function makeRequest(query = ""): NextRequest {
  return new NextRequest(
    `http://localhost/api/cv/${VALID_ID}/render/preview${query}`
  );
}

function ctxFor(id: string) {
  return { params: Promise.resolve({ id }) };
}

describe("GET /api/cv/[id]/render/preview (efemär live-preview PDF-passthrough BFF, 8b.3)", () => {
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

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("404 vid ogiltigt GUID i id — backend nås aldrig (SSRF/path-injektions-barriär)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor("not-a-guid"));

    expect(res.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 → strömmar PDF med FÄRSKA headers + Bearer + vidarebefordrar de fyra optionerna", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response("pdf-bytes", { status: 200 }));
    global.fetch = fetchMock;

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toBe("application/pdf");
    expect(res.headers.get("Content-Disposition")).toBe(
      'inline; filename="cv-forhandsvisning.pdf"'
    );
    expect(res.headers.get("Cache-Control")).toBe("no-store");

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/${VALID_ID}/render/preview?template=Klar&accent=NavyBlue&font=Modern&density=Normal`
    );
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer sess-1");
  });

  it("saknade optioner → tomsträngar vidarebefordras (backend är auktoritativ validator)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response("pdf-bytes", { status: 200 }));
    global.fetch = fetchMock;

    await GET(makeRequest(), ctxFor(VALID_ID));

    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/${VALID_ID}/render/preview?template=&accent=&font=&density=`
    );
  });

  it("429 → mappar rate-limit + Retry-After-header", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(null, { status: 429, headers: { "Retry-After": "45" } })
      );

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("45");
    expect(await res.json()).toEqual({
      error: "rateLimited",
      retryAfterSeconds: 45,
    });
  });

  it("backend 404 → 404 utan body (ekar aldrig backend)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(404);
    expect(await res.text()).toBe("");
  });

  it("backend 401 → 401 unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

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

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    const text = await res.text();
    expect(text).not.toContain("PII-LÄCKA");
    expect(text).not.toContain("19900101-1234");
    expect(JSON.parse(text)).toEqual({ error: "error" });
  });

  it("502 vid nätverksfel mot backend", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const res = await GET(makeRequest(ALL_PARAMS), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
  });
});
