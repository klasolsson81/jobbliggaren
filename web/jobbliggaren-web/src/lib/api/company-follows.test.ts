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
  it("no session → unauthorized without a backend round-trip", async () => {
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

  it("401 → unauthorized", async () => {
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

  it("network throw → error", async () => {
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

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await unfollowCompany(VALID_ID)).toEqual({ kind: "unauthorized" });
  });

  it("network throw → error", async () => {
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
