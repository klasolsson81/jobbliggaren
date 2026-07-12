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

import {
  followCompanyFromJobAd,
  unfollowCompany,
  getCompanyWatchStatus,
  getCompanyWatches,
  markFollowedCompanyAdSeen,
  getNewFollowedCompanyAdCount,
  markFollowedAdsSeen,
} from "./company-follows";

const VALID_ID = "11111111-1111-1111-1111-111111111111";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function emptyResponse(status: number): Response {
  return new Response(null, { status });
}

const originalFetch = global.fetch;

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

describe("followCompanyFromJobAd (#455) — status → kind mapping", () => {
  it("no session -> unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await followCompanyFromJobAd(VALID_ID);

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip (SSRF/path-injection guard)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await followCompanyFromJobAd("not-a-guid");

    expect(result).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("201 { id } → ok with the CompanyWatchId", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ id: "cw-1" }, 201));

    const result = await followCompanyFromJobAd(VALID_ID);

    expect(result).toEqual({ kind: "ok", data: { companyWatchId: "cw-1" } });
  });

  it("201 with a malformed body → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ nope: true }, 201));

    const result = await followCompanyFromJobAd(VALID_ID);

    expect(result).toEqual({ kind: "error" });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await followCompanyFromJobAd(VALID_ID)).toEqual({ kind: "unauthorized" });
  });

  it("404 (unknown ad) → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await followCompanyFromJobAd(VALID_ID)).toEqual({ kind: "notFound" });
  });

  it("400 (ad has no employer org.nr) → error (stale-FE backstop)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(400));
    expect(await followCompanyFromJobAd(VALID_ID)).toEqual({ kind: "error" });
  });

  it("network throw -> error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await followCompanyFromJobAd(VALID_ID)).toEqual({ kind: "error" });
  });
});

describe("unfollowCompany (#455)", () => {
  it("no session → unauthorized", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await unfollowCompany(VALID_ID)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await unfollowCompany("nope")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok (idempotent)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(204));
    expect(await unfollowCompany(VALID_ID)).toEqual({ kind: "ok", data: undefined });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await unfollowCompany(VALID_ID)).toEqual({ kind: "unauthorized" });
  });

  it("network throw -> error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await unfollowCompany(VALID_ID)).toEqual({ kind: "error" });
  });
});

describe("getCompanyWatchStatus (#455) — fail-safe to not-followable", () => {
  const fallback = { companyWatchId: null, followable: false };

  it("no session → fallback (toggle hides) without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getCompanyWatchStatus(VALID_ID)).toEqual(fallback);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → fallback without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCompanyWatchStatus("nope")).toEqual(fallback);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("followed employer → { companyWatchId, followable: true }", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        statuses: [{ jobAdId: VALID_ID, companyWatchId: "cw-9", followable: true }],
      })
    );
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual({
      companyWatchId: "cw-9",
      followable: true,
    });
  });

  it("not-followed but followable → { companyWatchId: null, followable: true }", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        statuses: [{ jobAdId: VALID_ID, companyWatchId: null, followable: true }],
      })
    );
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual({
      companyWatchId: null,
      followable: true,
    });
  });

  it("!res.ok → fallback", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(500));
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual(fallback);
  });

  it("shape mismatch (zod parse fail) → fallback", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ wrong: "shape" }));
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual(fallback);
  });

  it("id absent from the returned statuses → fallback", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        statuses: [{ jobAdId: "22222222-2222-2222-2222-222222222222", companyWatchId: "x", followable: true }],
      })
    );
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual(fallback);
  });

  it("network throw → fallback", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await getCompanyWatchStatus(VALID_ID)).toEqual(fallback);
  });
});

describe("getCompanyWatches (#448) — followed-company list read", () => {
  const legalEntity = {
    id: "cw-1",
    organizationNumber: "5592804784",
    isProtectedIdentity: false,
    companyName: "Skatteverket",
    followedAt: "2026-06-14T08:00:00+00:00",
    activeAdCount: 3,
    // #452 — matchande-räknare (>= Good); nullable = not-assessed (no stated occupation).
    matchingAdCount: 2,
  };
  const soleProp = {
    id: "cw-2",
    organizationNumber: null,
    isProtectedIdentity: true,
    companyName: "Anna Andersson Konsult",
    followedAt: "2026-06-10T08:00:00+00:00",
    activeAdCount: 1,
    matchingAdCount: null,
  };

  it("no session -> unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getCompanyWatches()).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 array (incl. masked sole-prop row) → ok, org.nr-null shape parses", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([legalEntity, soleProp]));

    const result = await getCompanyWatches();

    expect(result).toEqual({ kind: "ok", data: [legalEntity, soleProp] });
    // The masked row keeps a null org.nr + protected flag (never a raw value).
    if (result.kind === "ok") {
      expect(result.data[1]!.organizationNumber).toBeNull();
      expect(result.data[1]!.isProtectedIdentity).toBe(true);
    }
  });

  it("200 empty array → ok with []", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([]));
    expect(await getCompanyWatches()).toEqual({ kind: "ok", data: [] });
  });

  it("200 malformed body → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ not: "an array" }));
    expect(await getCompanyWatches()).toEqual({ kind: "error" });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await getCompanyWatches()).toEqual({ kind: "unauthorized" });
  });

  it("404 (collection endpoint) → error, never notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await getCompanyWatches()).toEqual({ kind: "error" });
  });

  it("network throw -> error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await getCompanyWatches()).toEqual({ kind: "error" });
  });
});

describe("markFollowedCompanyAdSeen (#453) - cross-channel dedup (fire-and-forget)", () => {
  it("no session -> unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await markFollowedCompanyAdSeen(VALID_ID)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id -> notFound without a backend round-trip (path-injection guard)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await markFollowedCompanyAdSeen("not-a-guid")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 -> ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(204));
    expect(await markFollowedCompanyAdSeen(VALID_ID)).toEqual({ kind: "ok", data: undefined });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await markFollowedCompanyAdSeen(VALID_ID)).toEqual({ kind: "unauthorized" });
  });

  it("429 (rate-limited) -> error (safe direction: hit left un-stamped)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(429));
    expect(await markFollowedCompanyAdSeen(VALID_ID)).toEqual({ kind: "error" });
  });

  it("network throw -> error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await markFollowedCompanyAdSeen(VALID_ID)).toEqual({ kind: "error" });
  });
});

describe("getNewFollowedCompanyAdCount (Bevakning F2 #801) — Översikt rail count", () => {
  it("no session -> unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getNewFollowedCompanyAdCount()).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 { count } -> ok with the parsed count", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ count: 5 }));

    const result = await getNewFollowedCompanyAdCount();

    expect(result).toEqual({ kind: "ok", data: { count: 5 } });
  });

  it("count === 0 is an honest ok (no active follows / nothing new), never an error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ count: 0 }));

    expect(await getNewFollowedCompanyAdCount()).toEqual({ kind: "ok", data: { count: 0 } });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));

    expect(await getNewFollowedCompanyAdCount()).toEqual({ kind: "unauthorized" });
  });

  it("malformed body (missing count) -> error (schema guard, never a fake 0)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ wrong: true }));

    expect(await getNewFollowedCompanyAdCount()).toEqual({ kind: "error" });
  });

  it("network throw -> error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));

    expect(await getNewFollowedCompanyAdCount()).toEqual({ kind: "error" });
  });
});

describe("markFollowedAdsSeen (Bevakning F2 #801) — watermark advance", () => {
  it("no session -> unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await markFollowedAdsSeen()).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 -> ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(204));

    expect(await markFollowedAdsSeen()).toEqual({ kind: "ok", data: undefined });
  });

  it("no seenThrough -> POST with no body (backend clock-now fallback)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;

    await markFollowedAdsSeen();

    const init = fetchMock.mock.calls[0]?.[1];
    expect(init?.method).toBe("POST");
    expect(init?.body).toBeUndefined();
  });

  it("seenThrough -> POST with { seenThrough } body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;

    await markFollowedAdsSeen("2026-07-12T10:00:00Z");

    const init = fetchMock.mock.calls[0]?.[1];
    expect(init?.method).toBe("POST");
    expect(JSON.parse(String(init?.body))).toEqual({ seenThrough: "2026-07-12T10:00:00Z" });
  });

  it("401 -> unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));

    expect(await markFollowedAdsSeen()).toEqual({ kind: "unauthorized" });
  });

  it("network throw -> error (fire-and-forget never throws)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));

    expect(await markFollowedAdsSeen()).toEqual({ kind: "error" });
  });
});
