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

import { getJobAd, getJobAds } from "./job-ads";

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

describe("getJobAds — the distans/remote wire mapping (#551 punkt 4)", () => {
  function okResponse() {
    return {
      ok: true,
      status: 200,
      json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 20 }),
    } as unknown as Response;
  }

  const baseQuery = {
    page: 1,
    pageSize: 20,
    sortBy: "PublishedAtDesc",
  } as const satisfies Partial<Parameters<typeof getJobAds>[0]>;

  async function urlFor(
    over: Partial<Parameters<typeof getJobAds>[0]> = {},
  ) {
    const query = { ...baseQuery, ...over } as Parameters<typeof getJobAds>[0];
    const fetchMock = vi.fn().mockResolvedValue(okResponse());
    global.fetch = fetchMock;
    await getJobAds(query);
    const [url] = fetchMock.mock.calls[0] as [string, RequestInit];
    return new URL(url).searchParams;
  }

  // THE pin of this axis, and the one most easily got wrong. The route flag the
  // USER sees is Swedish with a sentinel value (?distans=on), but the endpoint
  // binds a C# bool — ASP.NET does not bind "on", so forwarding the sentinel
  // would leave the facet silently unfiltered rather than erroring. Two names AND
  // two values, one translation.
  it("remote=true emits ?remote=true — the English name and the BOOL value, never the sentinel 'on'", async () => {
    const params = await urlFor({ remote: true });
    expect(params.get("remote")).toBe("true");
    expect(params.get("remote")).not.toBe("on");
    expect(params.has("distans")).toBe(false);
  });

  it("remote=false writes no param at all — absence is the off state (parity relaterade/employer)", async () => {
    const params = await urlFor({ remote: false });
    expect(params.has("remote")).toBe(false);
  });

  it("omitted remote is byte-identical to remote=false", async () => {
    const omitted = await urlFor();
    const explicitFalse = await urlFor({ remote: false });
    expect(omitted.toString()).toBe(explicitFalse.toString());
  });

  // Remote is a UNION disjunct of the ort dimension, never a replacement for it:
  // backend filters kommun ∨ län ∨ remote. So the ort ids must still travel.
  it("remote travels ALONGSIDE the two id axes, never instead of them", async () => {
    const params = await urlFor({
      region: ["CifL_Rzy_Mku"],
      municipality: ["AvNB_uwa_6n6"],
      remote: true,
    });
    expect(params.getAll("region")).toEqual(["CifL_Rzy_Mku"]);
    expect(params.getAll("municipality")).toEqual(["AvNB_uwa_6n6"]);
    expect(params.get("remote")).toBe("true");
  });
});
