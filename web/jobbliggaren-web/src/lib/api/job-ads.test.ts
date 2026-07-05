import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
}));

import { getJobAd } from "./job-ads";

const VALID_ID = "11111111-1111-1111-1111-111111111111";

const originalFetch = global.fetch;

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

describe("getJobAd — status → kind mapping + id guard (#633)", () => {
  it("no session → unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getJobAd(VALID_ID)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip (SSRF/path-injection guard, #633)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    // A path-injection attempt never reaches the backend URL: the allowlist
    // guard short-circuits before fetch. Bites on revert — without the guard
    // the malformed id flows into authedFetch and calls fetch.
    expect(await getJobAd("../../secret")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("valid GUID → performs the backend round-trip against the encoded path", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 404 }));
    global.fetch = fetchMock;

    // 404 → notFound (includeNotFound), which proves the guard lets a valid id
    // through to the backend rather than short-circuiting it.
    expect(await getJobAd(VALID_ID)).toEqual({ kind: "notFound" });
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`http://test-backend/api/v1/job-ads/${VALID_ID}`);
  });
});
