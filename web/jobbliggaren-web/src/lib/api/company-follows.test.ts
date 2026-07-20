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
  getFollowedJobAdIds,
  getCompanyWatchStatusByOrgNr,
  getCompanyWatches,
  markFollowedCompanyAdSeen,
  getNewFollowedCompanyAdCount,
  markFollowedAdsSeen,
  setWatchFilter,
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

describe("getFollowedJobAdIds (#1000, V1) — list follow-overlay, fail-safe to empty", () => {
  const A = "11111111-1111-1111-1111-111111111111";
  const B = "22222222-2222-2222-2222-222222222222";
  const C = "33333333-3333-3333-3333-333333333333";

  it("empty input → [] without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getFollowedJobAdIds([])).toEqual([]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("no session → [] without a backend round-trip (anon degraderar civilt)", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getFollowedJobAdIds([A])).toEqual([]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returnerar BARA de följda id:na (companyWatchId !== null)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        statuses: [
          { jobAdId: A, companyWatchId: "cw-1", followable: true }, // följd
          { jobAdId: B, companyWatchId: null, followable: true }, // följbar, ej följd
          { jobAdId: C, companyWatchId: "cw-2", followable: true }, // följd
        ],
      }),
    );
    expect(await getFollowedJobAdIds([A, B, C])).toEqual([A, C]);
  });

  it("!res.ok → []", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(500));
    expect(await getFollowedJobAdIds([A])).toEqual([]);
  });

  it("shape mismatch (zod parse fail) → []", async () => {
    // `statuses` non-array genuinely fails z.array → exercises the !parsed.success guard.
    // ({ wrong: "shape" }) would parse OK to { statuses: [] } via the schema default.
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ statuses: "nope" }));
    expect(await getFollowedJobAdIds([A])).toEqual([]);
  });

  it("network throw → []", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await getFollowedJobAdIds([A])).toEqual([]);
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

describe("getCompanyWatchStatusByOrgNr (#560 PR-C) — positional overlay, fail-safe to all-null", () => {
  const ORG_A = "5590000001";
  const ORG_B = "5590000002";

  it("empty input → [] without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCompanyWatchStatusByOrgNr([])).toEqual([]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("no session → all-null (one per input) without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await getCompanyWatchStatusByOrgNr([ORG_A, ORG_B])).toEqual([
      { companyWatchId: null },
      { companyWatchId: null },
    ]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("positional pass-through: statuses returned 1:1 in request order", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        statuses: [{ companyWatchId: null }, { companyWatchId: "cw-b" }],
      })
    );
    expect(await getCompanyWatchStatusByOrgNr([ORG_A, ORG_B])).toEqual([
      { companyWatchId: null },
      { companyWatchId: "cw-b" },
    ]);
  });

  it("length mismatch → all-null (never mis-align the zip)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ statuses: [{ companyWatchId: "cw-b" }] }) // 1 status for 2 requested
    );
    expect(await getCompanyWatchStatusByOrgNr([ORG_A, ORG_B])).toEqual([
      { companyWatchId: null },
      { companyWatchId: null },
    ]);
  });

  it("!res.ok → all-null", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(500));
    expect(await getCompanyWatchStatusByOrgNr([ORG_A])).toEqual([{ companyWatchId: null }]);
  });

  it("shape mismatch (zod parse fail) → all-null", async () => {
    // companyWatchId must be string|null; a number fails safeParse (not just a defaulted-empty statuses).
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ statuses: [{ companyWatchId: 123 }] })
    );
    expect(await getCompanyWatchStatusByOrgNr([ORG_A])).toEqual([{ companyWatchId: null }]);
  });

  it("network throw → all-null", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));
    expect(await getCompanyWatchStatusByOrgNr([ORG_A])).toEqual([{ companyWatchId: null }]);
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
    // F4b — the per-watch filter. null = no filter (the domain's canonical NULL).
    filter: null,
  };
  const soleProp = {
    id: "cw-2",
    organizationNumber: null,
    isProtectedIdentity: true,
    companyName: "Anna Andersson Konsult",
    followedAt: "2026-06-10T08:00:00+00:00",
    activeAdCount: 1,
    matchingAdCount: null,
    filter: null,
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

  // ── F4b (#803) — the per-watch filter on the list read ──────────────────────

  it("a filtered watch parses BOTH geo axes verbatim + onlyMatched (no axis crossing, no expansion)", async () => {
    // The two axes are DISJOINT namespaces. A whole-län pick arrives as ONE län concept-id in
    // `regions` — it must not be moved into `municipalities` nor expanded into the län's kommuner.
    const filtered = {
      ...legalEntity,
      filter: {
        municipalities: ["gbg_kn"],
        regions: ["skane_lan"],
        onlyMatched: true,
      },
    };
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([filtered]));

    const result = await getCompanyWatches();

    expect(result).toEqual({ kind: "ok", data: [filtered] });
    if (result.kind === "ok") {
      expect(result.data[0]!.filter).toEqual({
        municipalities: ["gbg_kn"],
        regions: ["skane_lan"],
        onlyMatched: true,
      });
    }
  });

  it("filter: null parses as no-filter (the canonical NULL — never coerced to an empty object)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([legalEntity]));

    const result = await getCompanyWatches();

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") expect(result.data[0]!.filter).toBeNull();
  });

  it("a MISSING filter key → error (required key: `undefined` would render a filtered watch as unfiltered)", async () => {
    // Regression pin against relaxing the schema to `.optional()`. Contract drift on this key is not
    // a cosmetic bug: an absent value would silently strip the BC-9′ resting-state disclosure from a
    // watch whose notifications ARE narrowed — the exact silent-narrowing failure the disclosure exists
    // to prevent. Fail loud instead.
    const { filter: _dropped, ...withoutFilter } = legalEntity;
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([withoutFilter]));

    expect(await getCompanyWatches()).toEqual({ kind: "error" });
  });
});

describe("setWatchFilter (Bevakning F4b #803) — PUT {id}/filter", () => {
  const KOMMUN = ["gbg_kn"];
  const LAN = ["skane_lan"];

  it("no session → unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setWatchFilter(VALID_ID, {
      municipalities: KOMMUN,
      regions: LAN,
      onlyMatched: true,
    });

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip (path-injection guard)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setWatchFilter("../../admin", {
      municipalities: [],
      regions: [],
      onlyMatched: true,
    });

    expect(result).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("PUTs to {BASE}/{id}/filter with Bearer + a body carrying BOTH axes unexpanded", async () => {
    // The single most important wire pin. `regions` must carry the län concept-id AS PICKED —
    // never expanded into the län's ~49 kommuner, never swapped into the municipality axis. A crossed
    // or expanded id would be stored and then match nothing: a filter that silently suppresses every
    // notification, which the user cannot see and cannot debug.
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;

    await setWatchFilter(VALID_ID, {
      municipalities: KOMMUN,
      regions: LAN,
      onlyMatched: true,
    });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe(`http://test-backend/api/v1/me/company-watches/${VALID_ID}/filter`);
    expect(init?.method).toBe("PUT");
    expect(
      (init?.headers as Record<string, string> | undefined)?.Authorization
    ).toBe("Bearer sess-1");
    expect(JSON.parse(String(init?.body))).toEqual({
      municipalities: ["gbg_kn"],
      regions: ["skane_lan"],
      onlyMatched: true,
    });
  });

  it("an all-empty selection is sent as-is (it is how the user CLEARS the filter, never a 400 client-side)", async () => {
    // The clear path travels the same route as a set — the backend maps the empty selection to the
    // canonical NULL. A client-side "at least one filter"-guard here would leave the user with an
    // active filter and no way to remove it.
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;

    const result = await setWatchFilter(VALID_ID, {
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]?.body))).toEqual({
      municipalities: [],
      regions: [],
      onlyMatched: false,
    });
  });

  it("204 → ok (the body is never read — TD-10)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(204));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: KOMMUN, regions: [], onlyMatched: false })
    ).toEqual({ kind: "ok", data: undefined });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: [], regions: [], onlyMatched: true })
    ).toEqual({ kind: "unauthorized" });
  });

  it("403 → forbidden", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(403));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: [], regions: [], onlyMatched: true })
    ).toEqual({ kind: "forbidden" });
  });

  it("404 (unknown OR another user's watch — BC-6) → notFound, never forbidden", async () => {
    // The backend answers a cross-user id with 404 precisely so the endpoint is not an enumeration
    // oracle. The fetcher must not "helpfully" re-map it to forbidden.
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: KOMMUN, regions: [], onlyMatched: false })
    ).toEqual({ kind: "notFound" });
  });

  it("429 → rateLimited with the parsed Retry-After", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "42" } })
    );

    expect(
      await setWatchFilter(VALID_ID, { municipalities: KOMMUN, regions: [], onlyMatched: false })
    ).toEqual({ kind: "rateLimited", retryAfterSeconds: 42 });
  });

  it("400 (malformed concept-id reaches the VO's default-deny) → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(400));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: ["inte giltig"], regions: [], onlyMatched: false })
    ).toEqual({ kind: "error" });
  });

  it("network throw → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom"));

    expect(
      await setWatchFilter(VALID_ID, { municipalities: KOMMUN, regions: LAN, onlyMatched: true })
    ).toEqual({ kind: "error" });
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

  it("passed session is used and getSessionId is NOT called (after()-safe path, #741)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;

    expect(await markFollowedCompanyAdSeen(VALID_ID, "sess-after")).toEqual({
      kind: "ok",
      data: undefined,
    });
    // The render path read the session; the helper does NOT re-read cookies (which
    // would be forbidden inside `after()` in a Server Component).
    expect(getSessionIdMock).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/ad-hits/"),
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: "Bearer sess-after" }),
      })
    );
  });

  it("passed session = null -> unauthorized without a backend round-trip (anon-skip preserved)", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await markFollowedCompanyAdSeen(VALID_ID, null)).toEqual({
      kind: "unauthorized",
    });
    expect(getSessionIdMock).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
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
