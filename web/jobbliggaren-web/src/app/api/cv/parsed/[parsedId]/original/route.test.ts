import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { GET } from "./route";

const VALID_ID = "11111111-1111-4111-8111-111111111111";
const PDF_CONTENT_TYPE = "application/pdf";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

function makeRequest(): NextRequest {
  return new NextRequest(`http://localhost/api/cv/parsed/${VALID_ID}/original`);
}

function ctxFor(parsedId: string) {
  return { params: Promise.resolve({ parsedId }) };
}

/**
 * The staging-key binding. The shared posture is covered once in the canonical route's suite
 * (`api/cv/[id]/original/route.test.ts`); what is genuinely different here — and therefore what
 * this file pins — is that this route binds the OTHER id form to the OTHER backend path. A copy
 * of the whole error matrix would test the shared module twice and this binding no better.
 */
describe("GET /api/cv/parsed/[parsedId]/original (staging-key binding)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    withSession("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    cookiesMock.mockReset();
  });

  it("proxar mot den PARSADE backend-vägen, inte den kanoniska", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response("bytes", { status: 200, headers: { "Content-Type": PDF_CONTENT_TYPE } })
    );
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(200);
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://test-backend/api/v1/resumes/parsed/${VALID_ID}/original`);
  });

  it("404 vid ogiltigt GUID — backend nås aldrig (SSRF/path-injektions-barriär)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor("not-a-guid"));

    expect(res.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("401 utan session — backend nås aldrig", async () => {
    withSession(undefined);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await GET(makeRequest(), ctxFor(VALID_ID));

    expect(res.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
