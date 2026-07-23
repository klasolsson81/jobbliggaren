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

function followStateResponse(companyWatchId: string | null) {
  return new Response(JSON.stringify({ statuses: [{ companyWatchId }] }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

/**
 * The BFF composes TWO backend reads for a hit: the register search + the org.nr follow-state overlay
 * (#997). Route the mock by URL so each test can pin both the search result and the follow-state, and so
 * a test can assert the follow-state endpoint was (or was not) called.
 */
function routeFetch(handlers: {
  search: Response;
  followState?: Response;
}) {
  return vi.fn<typeof fetch>((input) => {
    const url = String(input);
    if (url.includes("/companies/search")) return Promise.resolve(handlers.search);
    if (url.includes("/company-watches/status/by-org-nr")) {
      return Promise.resolve(handlers.followState ?? followStateResponse(null));
    }
    throw new Error(`unexpected fetch to ${url}`);
  });
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

const MASKED_COMPANY = {
  organizationNumber: null,
  isProtectedIdentity: true,
  name: "Skyddad firma",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
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

  it("200 with the company + null follow-state on a hit the user does not follow", async () => {
    const fetchMock = routeFetch({
      search: companyResponse(FOUND_COMPANY),
      followState: followStateResponse(null),
    });
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ company: FOUND_COMPANY, companyWatchId: null });

    // org.nr travels in the body of the search call, never a URL.
    const searchCall = fetchMock.mock.calls.find(([url]) => String(url).includes("/companies/search"));
    expect(searchCall).toBeDefined();
    const [url, init] = searchCall!;
    expect(String(url)).toBe("http://test-backend/api/v1/companies/search");
    expect(JSON.parse((init as RequestInit).body as string)).toMatchObject({
      organizationNumber: VALID_ORGNR,
    });
  });

  it("200 with companyWatchId when the user already follows the company", async () => {
    global.fetch = routeFetch({
      search: companyResponse(FOUND_COMPANY),
      followState: followStateResponse("watch-123"),
    });

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ company: FOUND_COMPANY, companyWatchId: "watch-123" });
  });

  it("a masked (sole-prop) hit is not followable — companyWatchId null, no follow-state call", async () => {
    const fetchMock = routeFetch({ search: companyResponse(MASKED_COMPANY) });
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ company: MASKED_COMPANY, companyWatchId: null });
    // The masked row carries no org.nr key → the follow-state overlay is skipped entirely (D8(c)).
    const followCall = fetchMock.mock.calls.find(([url]) =>
      String(url).includes("/status/by-org-nr"),
    );
    expect(followCall).toBeUndefined();
  });

  it("200 null when the register has no such company — no follow-state call", async () => {
    const fetchMock = routeFetch({ search: companyResponse(null) });
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toBeNull();
    const followCall = fetchMock.mock.calls.find(([url]) =>
      String(url).includes("/status/by-org-nr"),
    );
    expect(followCall).toBeUndefined();
  });

  it("still returns the hit when the follow-state overlay fails (degrades to null)", async () => {
    global.fetch = routeFetch({
      search: companyResponse(FOUND_COMPANY),
      followState: new Response(null, { status: 500 }),
    });

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ company: FOUND_COMPANY, companyWatchId: null });
  });

  it("429 forwards Retry-After — no follow-state call", async () => {
    const fetchMock = routeFetch({
      search: new Response(null, { status: 429, headers: { "Retry-After": "42" } }),
    });
    global.fetch = fetchMock;

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(429);
    expect(res.headers.get("Retry-After")).toBe("42");
    const followCall = fetchMock.mock.calls.find(([url]) =>
      String(url).includes("/status/by-org-nr"),
    );
    expect(followCall).toBeUndefined();
  });

  it("502 on a backend error", async () => {
    global.fetch = routeFetch({ search: new Response(null, { status: 500 }) });

    const res = await POST(makeRequest({ organizationNumber: VALID_ORGNR }));

    expect(res.status).toBe(502);
  });
});
