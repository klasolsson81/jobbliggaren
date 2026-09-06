import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { GET } from "./route";

const VALID_ID = "22222222-2222-4222-8222-222222222222";

const PDF_CONTENT_TYPE = "application/pdf";
const DOCX_CONTENT_TYPE =
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

function makeRequest(): NextRequest {
  return new NextRequest(`http://localhost/api/cv/${VALID_ID}/original`);
}

function ctxFor(id: string) {
  return { params: Promise.resolve({ id }) };
}

function backendFile(contentType: string): Response {
  return new Response("bytes", {
    status: 200,
    headers: { "Content-Type": contentType },
  });
}

/**
 * The canonical-key BFF for the user's OWN uploaded file. The proxy itself lives in
 * `lib/http/original-file-proxy.ts` and is shared with the staging route, so this suite covers the
 * shared posture once, from a real route binding: the SSRF allowlist, the never-echo-the-body rule,
 * and the two things this proxy does that the delivered preview route does not — narrowing the
 * content type to an allowlist, and choosing the disposition from it.
 */
describe("GET /api/cv/[id]/original (original-file passthrough BFF)", () => {
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

  it("404 vid ogiltigt GUID — backend nås aldrig (SSRF/path-injektions-barriär)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor("../../etc/passwd"));

    expect(res.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 pdf → inline, FÄRSKA headers, Bearer mot rätt backend-väg", async () => {
    const fetchMock = vi.fn().mockResolvedValue(backendFile(PDF_CONTENT_TYPE));
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://test-backend/api/v1/resumes/${VALID_ID}/original`);
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer sess-1");

    expect(res.headers.get("Content-Type")).toBe(PDF_CONTENT_TYPE);
    expect(res.headers.get("Content-Disposition")).toBe(
      'inline; filename="original.pdf"'
    );
    expect(res.headers.get("Cache-Control")).toBe("no-store");
    expect(res.headers.get("X-Content-Type-Options")).toBe("nosniff");
  });

  it("200 docx → attachment (kan aldrig renderas inline)", async () => {
    global.fetch = vi.fn().mockResolvedValue(backendFile(DOCX_CONTENT_TYPE));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toBe(DOCX_CONTENT_TYPE);
    expect(res.headers.get("Content-Disposition")).toBe(
      'attachment; filename="original.docx"'
    );
  });

  it("filnamnet i headern är SYNTETISKT — lagrat, användarkontrollerat namn når aldrig hit", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response("bytes", {
        status: 200,
        headers: {
          "Content-Type": PDF_CONTENT_TYPE,
          "Content-Disposition": 'attachment; filename="mitt CV.pdf"',
        },
      })
    );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    // Backendens Content-Disposition vidarebefordras aldrig: det lagrade namnet är
    // användartext, och en header är den enda plats där det kunde injicera.
    expect(res.headers.get("Content-Disposition")).toBe(
      'inline; filename="original.pdf"'
    );
    expect(res.headers.get("Content-Disposition")).not.toContain("mitt CV");
  });

  it("okänd content-type från backend → 502, aldrig en otypad body till webbläsaren", async () => {
    global.fetch = vi.fn().mockResolvedValue(backendFile("text/html"));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    expect(await res.json()).toEqual({ error: "error" });
  });

  it("backend 404 → 404 utan body (det VANLIGA svaret: ingen sparad originalfil)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(404);
    expect(await res.text()).toBe("");
  });

  it("backend 401 → 401 unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
  });

  it("429 → mappar rate-limit + Retry-After-header", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "42" } })
    );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("42");
    expect(await res.json()).toEqual({ error: "rateLimited", retryAfterSeconds: 42 });
  });

  it("backend non-ok med PII-bärande ProblemDetails → 502, body EKAS ALDRIG", async () => {
    const problem = JSON.stringify({
      detail: "anna.andersson@example.se orsakade ett fel i CvRenderer",
    });
    global.fetch = vi.fn().mockResolvedValue(
      new Response(problem, {
        status: 500,
        headers: { "Content-Type": "application/problem+json" },
      })
    );

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    const body = await res.text();
    expect(body).toBe(JSON.stringify({ error: "error" }));
    expect(body).not.toContain("anna.andersson");
    expect(body).not.toContain("CvRenderer");
  });

  it("502 vid nätverksfel mot backend", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ECONNREFUSED"));

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(502);
    expect(await res.json()).toEqual({ error: "error" });
  });
});
