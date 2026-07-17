import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import type { NextRequest } from "next/server";
import svPages from "../../../../../messages/sv/pages.json";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

// The route resolves its statusbaserade svenska felcopy via
// `getTranslations("pages.cv.importApi")`. In jsdom next-intl's server entry is
// unavailable, so mock it to a real, namespace-aware translator over the Swedish
// catalog (source of truth) — verbatim copy keeps flowing, identical to prod.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages.cv.importApi") =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace,
    }),
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { POST } from "./route";

const VALID_ID = "11111111-1111-4111-8111-111111111111";
const MULTIPART = "multipart/form-data; boundary=----x";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

function makeRequest(
  headers: Record<string, string>,
  body: BodyInit | null = "cv-bytes",
): NextRequest {
  const req = new Request("http://localhost/api/cv/import", {
    method: "POST",
    headers,
    body,
  });
  return req as unknown as NextRequest;
}

describe("POST /api/cv/import (binär-passthrough BFF)", () => {
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

    const res = await POST(makeRequest({ "content-type": MULTIPART }));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("400 vid icke-multipart content-type", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ "content-type": "application/json" }));

    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("413 när Content-Length överstiger taket — snabb-avvisning före proxy", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(
      makeRequest({ "content-type": MULTIPART, "content-length": "20000000" }),
    );

    expect(res.status).toBe(413);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("400 när ingen kropp bifogas", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ "content-type": MULTIPART }, null));

    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("201 Promoted → vidarebefordrar strömmen med Bearer + bevarad Content-Type och det PII-fria sammansatta utfallet", async () => {
    const RESUME_ID = "22222222-2222-4222-8222-222222222222";
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          parsedResumeId: VALID_ID,
          personnummer: { found: false, count: 0, kinds: [] },
          outcome: "Promoted",
          resumeId: RESUME_ID,
          blockReason: null,
        }),
        { status: 201, headers: { "Content-Type": "application/json" } },
      ),
    );
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ "content-type": MULTIPART }));

    expect(res.status).toBe(201);
    expect(await res.json()).toEqual({
      parsedResumeId: VALID_ID,
      outcome: "Promoted",
      resumeId: RESUME_ID,
      blockReason: null,
      personnummer: { found: false, count: 0, kinds: [] },
    });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/resumes/import");
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer sess-1");
    expect(headers["Content-Type"]).toBe(MULTIPART);
    expect(init.body).not.toBeNull();
  });

  it("200 LeftPending → statusen bevaras och personnummer-fyndet (count/kinds, aldrig värdet) flödar igenom", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          parsedResumeId: VALID_ID,
          personnummer: { found: true, count: 1, kinds: ["Full"] },
          outcome: "LeftPending",
          resumeId: null,
          blockReason: "PersonnummerPresent",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );

    const res = await POST(makeRequest({ "content-type": MULTIPART }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({
      parsedResumeId: VALID_ID,
      outcome: "LeftPending",
      resumeId: null,
      blockReason: "PersonnummerPresent",
      personnummer: { found: true, count: 1, kinds: ["Full"] },
    });
  });

  it("429 → mappar rate-limit + Retry-After", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "45" } }),
    );

    const res = await POST(makeRequest({ "content-type": MULTIPART }));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("45");
    expect(await res.json()).toEqual({
      error: "rateLimited",
      retryAfterSeconds: 45,
    });
  });

  it("413 från backend → 413", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 413 }));
    const res = await POST(makeRequest({ "content-type": MULTIPART }));
    expect(res.status).toBe(413);
  });

  it("401 från backend → 401", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 401 }));
    const res = await POST(makeRequest({ "content-type": MULTIPART }));
    expect(res.status).toBe(401);
  });

  it("400 från backend → 400 med generisk svensk copy (ekar ALDRIG backend-body)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "INTERN PII-LÄCKA", title: "x" }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const res = await POST(makeRequest({ "content-type": MULTIPART }));

    expect(res.status).toBe(400);
    const body = (await res.json()) as { error: string };
    expect(body.error).not.toContain("PII-LÄCKA");
    expect(body.error).toContain("PDF");
  });

  it("502 vid nätverksfel mot backend", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    const res = await POST(makeRequest({ "content-type": MULTIPART }));
    expect(res.status).toBe(502);
  });
});
