import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import type { NextRequest } from "next/server";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { cookiesMock } = vi.hoisted(() => ({ cookiesMock: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: cookiesMock }));

import { POST } from "./route";

function withSession(value: string | undefined) {
  cookiesMock.mockResolvedValue({
    get: (name: string) =>
      name === "__Host-jobbliggaren_session" && value !== undefined
        ? { value }
        : undefined,
  });
}

function makeRequest(body: unknown): NextRequest {
  const req = new Request("http://localhost/api/foretag/sok", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  return req as unknown as NextRequest;
}

function companyResponse(company: unknown | null) {
  return new Response(
    JSON.stringify({
      companies: {
        items: company === null ? [] : [company],
        totalCount: company === null ? 0 : 1,
        page: 1,
        pageSize: 20,
      },
      magnitude: { magnitude: company === null ? 0 : 1, saturated: false },
    }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

const VALID_ORGNR = "5560125790"; // 3rd digit 6 >= 2 → a legal entity, not personnummer-shaped
const PNR_SHAPED = "1010101010"; // 3rd digit 1 < 2 → personnummer-shaped, must be refused pre-backend

const FOUND_COMPANY = {
  organizationNumber: VALID_ORGNR,
  isProtectedIdentity: false,
  name: "Volvo AB",
  seatMunicipalityCode: "1480",
  seatMunicipalityName: "Göteborg",
  sniCodes: ["29100"],
};

describe("POST /api/foretag/sok (org.nr search BFF)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    withSession("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    cookiesMock.mockReset();
  });

  it("400 on a malformed org.nr — backend never touched", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: "12345" }));

    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("400 { reason: 'protected' } on a personnummer-shaped org.nr — NOT forwarded", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: PNR_SHAPED }));

    expect(res.status).toBe(400);
    expect(await res.json()).toEqual({ reason: "protected" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("401 without a session — backend never touched", async () => {
    withSession(undefined);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 with the single company on a hit — org.nr travels in the body, never a URL", async () => {
    const fetchMock = vi.fn().mockResolvedValue(companyResponse(FOUND_COMPANY));
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual(FOUND_COMPANY);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/companies/search");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toMatchObject({
      organizationNumber: VALID_ORGNR,
    });
  });

  it("200 null when the register has no such company", async () => {
    global.fetch = vi.fn().mockResolvedValue(companyResponse(null));

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toBeNull();
  });

  it("429 forwards Retry-After", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response(null, { status: 429, headers: { "Retry-After": "42" } }),
      );

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("42");
  });

  it("502 on a backend error", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 500 }));

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(502);
  });
});
