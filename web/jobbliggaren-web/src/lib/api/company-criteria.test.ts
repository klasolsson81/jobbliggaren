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
  getCompanyWatchCriteria,
  getCriterionReference,
  browseCriterionCompanies,
  browseCriterionAds,
  getCriterionAdCount,
  previewCriterionCount,
  createCriterion,
  updateCriterion,
  deleteCriterion,
} from "./company-criteria";

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

// ── getCompanyWatchCriteria ─────────────────────────────────────────────────

describe("getCompanyWatchCriteria — list read", () => {
  const criterion = {
    id: "cr-1",
    sniCodes: ["62010", "62020"],
    municipalityCodes: ["0180"],
    label: "IT i Stockholm",
    createdAt: "2026-07-14T08:00:00+00:00",
    updatedAt: "2026-07-15T09:00:00+00:00",
  };

  it("no session → unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCompanyWatchCriteria()).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 array (incl. null label) → ok", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse([criterion, { ...criterion, id: "cr-2", label: null }]));
    const result = await getCompanyWatchCriteria();
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data).toHaveLength(2);
      expect(result.data[1]!.label).toBeNull();
    }
  });

  it("200 empty array → ok with []", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([]));
    expect(await getCompanyWatchCriteria()).toEqual({ kind: "ok", data: [] });
  });

  it("200 malformed body → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ not: "an array" }));
    expect(await getCompanyWatchCriteria()).toEqual({ kind: "error" });
  });

  it("404 (collection endpoint) → error, never notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await getCompanyWatchCriteria()).toEqual({ kind: "error" });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await getCompanyWatchCriteria()).toEqual({ kind: "unauthorized" });
  });
});

// ── getCriterionReference ───────────────────────────────────────────────────

describe("getCriterionReference — SCB tree read", () => {
  const tree = {
    sniVersion: "SNI2025",
    kommunVersion: "2025",
    sni: [
      {
        code: "J",
        name: "Information",
        divisions: [
          { code: "62", name: "Dataprogrammering", leaves: [{ code: "62010", name: "Dataprogrammering" }] },
        ],
      },
    ],
    lan: [{ code: "01", name: "Stockholms län", kommuner: [{ code: "0180", name: "Stockholm" }] }],
  };

  it("200 tree → ok, nested shape parses", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(tree));
    const result = await getCriterionReference();
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.sni[0]!.divisions[0]!.leaves[0]!.code).toBe("62010");
      expect(result.data.lan[0]!.kommuner[0]!.name).toBe("Stockholm");
    }
  });

  it("hits the reference path with a Bearer header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(tree));
    global.fetch = fetchMock;
    await getCriterionReference();
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("http://test-backend/api/v1/me/company-watch-criteria/reference");
    expect((init?.headers as Record<string, string>).Authorization).toBe("Bearer sess-1");
  });

  it("200 malformed tree → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ sni: "nope" }));
    expect(await getCriterionReference()).toEqual({ kind: "error" });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await getCriterionReference()).toEqual({ kind: "unauthorized" });
  });
});

// ── browseCriterionCompanies ────────────────────────────────────────────────

describe("browseCriterionCompanies — register run", () => {
  const response = {
    companies: {
      items: [
        {
          organizationNumber: "5592804784",
          isProtectedIdentity: false,
          name: "Acme AB",
          seatMunicipalityCode: "0180",
          seatMunicipalityName: "Stockholm",
          sniCodes: ["62010"],
        },
        {
          organizationNumber: null,
          isProtectedIdentity: true,
          name: "Enskild firma",
          seatMunicipalityCode: "1480",
          seatMunicipalityName: "Göteborg",
          sniCodes: ["62020"],
        },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    },
    magnitude: { magnitude: 2, saturated: false },
  };

  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await browseCriterionCompanies("../admin", 1)).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 composed response (page + magnitude) → ok; masked sole-prop parses", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(response));
    const result = await browseCriterionCompanies(VALID_ID, 1);
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.companies.totalPages).toBe(1);
      expect(result.data.companies.items[1]!.organizationNumber).toBeNull();
      expect(result.data.companies.items[1]!.isProtectedIdentity).toBe(true);
      expect(result.data.magnitude).toEqual({ magnitude: 2, saturated: false });
    }
  });

  it("clamps a non-positive page to 1 in the query string", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(response));
    global.fetch = fetchMock;
    await browseCriterionCompanies(VALID_ID, 0);
    expect(String(fetchMock.mock.calls[0]![0])).toContain("page=1&pageSize=20");
  });

  it("404 (unknown OR cross-user id) → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await browseCriterionCompanies(VALID_ID, 1)).toEqual({ kind: "notFound" });
  });

  it("200 malformed body → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ companies: {} }));
    expect(await browseCriterionCompanies(VALID_ID, 1)).toEqual({ kind: "error" });
  });
});

// ── browseCriterionAds / getCriterionAdCount ────────────────────────────────

describe("browseCriterionAds — the criterion's ad run", () => {
  const ad = {
    id: "aaaaaaaa-1111-2222-3333-444444444444",
    title: "Systemingenjör",
    companyName: "Acme AB",
    url: "https://example.com/jobs/1",
    source: "Platsbanken",
    status: "Active",
    publishedAt: "2026-08-20T08:00:00+00:00",
    expiresAt: "2026-09-20T08:00:00+00:00",
    createdAt: "2026-08-20T09:00:00+00:00",
  };
  const response = {
    ads: { items: [ad], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 },
    magnitude: { magnitude: 167, saturated: false },
    // #1656 (b) — null is "the caller did not ask for the matching view". Always present on the
    // wire: the schema declares it nullable, never optional, so the shape cannot vary with the
    // filter.
    matching: null,
  };

  it("no session → unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await browseCriterionAds(VALID_ID, 1)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await browseCriterionAds("../admin", 1)).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("defaults to the unfiltered view and says so on the wire", async () => {
    // #1656 (b) — a filter nobody asked for must never be applied. The parameter is explicit rather
    // than omitted so the request states the axis instead of relying on a backend default.
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(response));
    global.fetch = fetchMock;
    await browseCriterionAds(VALID_ID, 1);
    expect(String(fetchMock.mock.calls[0]![0])).toContain("onlyMatching=false");
  });

  it("asks for the matching view when the caller does", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(response));
    global.fetch = fetchMock;
    await browseCriterionAds(VALID_ID, 1, true);
    expect(String(fetchMock.mock.calls[0]![0])).toContain("onlyMatching=true");
  });

  it("200 composed response (page + magnitude) → ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(response));
    const result = await browseCriterionAds(VALID_ID, 1);
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.ads.totalPages).toBe(1);
      expect(result.data.ads.items[0]!.title).toBe("Systemingenjör");
      expect(result.data.magnitude).toEqual({ magnitude: 167, saturated: false });
    }
  });

  it("clamps a non-positive page to 1 in the query string", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(response));
    global.fetch = fetchMock;
    await browseCriterionAds(VALID_ID, 0);
    expect(String(fetchMock.mock.calls[0]![0])).toContain("page=1&pageSize=20");
  });

  it("404 (unknown OR cross-user id) → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await browseCriterionAds(VALID_ID, 1)).toEqual({ kind: "notFound" });
  });

  it("200 malformed body → error", async () => {
    // The ad rows are validated by the SAME `jobAdDtoSchema` /jobb uses, so a wire skew on any of
    // its fields degrades to a civil error rather than rendering a half-parsed ad. This is what
    // makes the mirrored schema a guarantee instead of a hope.
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse({ ads: { items: [{ id: ad.id }] }, magnitude: {} }));
    expect(await browseCriterionAds(VALID_ID, 1)).toEqual({ kind: "error" });
  });
});

describe("getCriterionAdCount — the headline number alone", () => {
  it("no session → unauthorized without a backend round-trip", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await getCriterionAdCount("../admin")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 { ads, matching } → ok, and asks the count route (never the ad page)", async () => {
    const body = {
      ads: { magnitude: 167, saturated: false },
      matching: { count: 9, tooBroad: false },
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body));
    global.fetch = fetchMock;
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "ok", data: body });
    // The detail page must not pay for twenty ad rows it never renders, and must not read the
    // capped pagination `totalCount` as the magnitude — both are the same mistake in one call.
    const url = String(fetchMock.mock.calls[0]![0]);
    expect(url).toContain("/ad-count");
    expect(url).not.toContain("pageSize");
  });

  it("saturated true survives the wire", async () => {
    const body = {
      ads: { magnitude: 10000, saturated: true },
      matching: { count: null, tooBroad: true },
    };
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(body));
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "ok", data: body });
  });

  it("the two ad numbers keep their own doctrines: saturating vs exact-or-absent", async () => {
    // #1656 (b) — one schema for both would let a surface render the ad magnitude where it means
    // the personal count. `ads` may saturate and render "10 000+"; `matching` carries no
    // `saturated` at all, because its set is REFUSED rather than truncated and its number is
    // therefore exact whenever it is present.
    const body = {
      ads: { magnitude: 10000, saturated: true },
      matching: { count: 12, tooBroad: false },
    };
    global.fetch = vi.fn().mockResolvedValue(jsonResponse(body));
    const result = await getCriterionAdCount(VALID_ID);
    expect(result).toEqual({ kind: "ok", data: body });
  });

  it("a matching count beside tooBroad is rejected, never rendered as a floor", async () => {
    // The state the backend DTO forbids in its constructor. If it ever reached the wire, a truncated
    // floor would be rendered as an exact count -- the defect the refusal bound exists to prevent.
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        ads: { magnitude: 5, saturated: false },
        matching: { count: 3, tooBroad: true },
      }),
    );
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "error" });
  });

  it("404 (unknown OR cross-user id) → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "notFound" });
  });

  it("200 malformed body → error, never a false 0", async () => {
    // The detail page renders a civil "cannot be shown" line on `error`. A schema that let a
    // malformed body through as `{ magnitude: 0 }` would render "Inga aktiva annonser" — a false
    // statement rather than an absent one (#859).
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ ads: { magnitude: "many" }, matching: { count: null, tooBroad: false } }),
    );
    expect(await getCriterionAdCount(VALID_ID)).toEqual({ kind: "error" });
  });
});

// ── previewCriterionCount ───────────────────────────────────────────────────

describe("previewCriterionCount — live magnitude preview", () => {
  it("200 { magnitude, saturated } → ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ magnitude: 412, saturated: false }));
    expect(await previewCriterionCount({ sniCodes: ["62010"], municipalityCodes: ["0180"] })).toEqual({
      kind: "ok",
      data: { magnitude: 412, saturated: false },
    });
  });

  it("wraps the predicate under a `criteria` member on the wire", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ magnitude: 1, saturated: false }));
    global.fetch = fetchMock;
    await previewCriterionCount({ sniCodes: ["62010"], municipalityCodes: ["0180"] });
    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]?.body))).toEqual({
      criteria: { sniCodes: ["62010"], municipalityCodes: ["0180"] },
    });
  });

  it("400 (missing axis) → error (hook then nulls the count)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(400));
    expect(
      await previewCriterionCount({ sniCodes: [], municipalityCodes: ["0180"] }),
    ).toEqual({ kind: "error" });
  });
});

// ── createCriterion (write path + message extraction) ───────────────────────

describe("createCriterion — write path", () => {
  const predicate = { sniCodes: ["62010"], municipalityCodes: ["0180"] };

  it("201 { id } → ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ id: "cr-9" }, 201));
    expect(await createCriterion(predicate, "IT")).toEqual({
      kind: "ok",
      data: { id: "cr-9" },
    });
  });

  it("sends { criteria, label } on the wire", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: "cr-9" }, 201));
    global.fetch = fetchMock;
    await createCriterion(predicate, "IT i Stockholm");
    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]?.body))).toEqual({
      criteria: { sniCodes: ["62010"], municipalityCodes: ["0180"] },
      label: "IT i Stockholm",
    });
  });

  it("201 with a malformed body → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ nope: true }, 201));
    expect(await createCriterion(predicate, null)).toEqual({ kind: "error" });
  });

  it("400 validation `errors` dict → { validation, message } (unknown-codes surfaced)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse(
        { errors: { "Criteria.SniCodes": ["Okända SNI-koder: 99998."] } },
        400,
      ),
    );
    expect(await createCriterion(predicate, null)).toEqual({
      kind: "validation",
      message: "Okända SNI-koder: 99998.",
    });
  });

  it("409 ProblemDetails `detail` → { conflict, message } (max-per-user surfaced)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse(
        {
          title: "CompanyWatchCriterion.MaxPerUser",
          detail: "Du kan ha högst 20 bevakningar. Ta bort en bevakning för att skapa en ny.",
          status: 409,
        },
        409,
      ),
    );
    expect(await createCriterion(predicate, null)).toEqual({
      kind: "conflict",
      message: "Du kan ha högst 20 bevakningar. Ta bort en bevakning för att skapa en ny.",
    });
  });

  it("400 with an unreadable body → { validation, message: null } (action falls back to i18n)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(400));
    expect(await createCriterion(predicate, null)).toEqual({
      kind: "validation",
      message: null,
    });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await createCriterion(predicate, null)).toEqual({ kind: "unauthorized" });
  });
});

// ── updateCriterion ─────────────────────────────────────────────────────────

describe("updateCriterion — PATCH partial", () => {
  const body = { label: "", criteria: { sniCodes: ["62010"], municipalityCodes: ["0180"] } };

  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await updateCriterion("nope", body)).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok; a blank label is sent as-is (clears the name)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(emptyResponse(204));
    global.fetch = fetchMock;
    expect(await updateCriterion(VALID_ID, body)).toEqual({ kind: "ok", data: undefined });
    const sent = JSON.parse(String(fetchMock.mock.calls[0]![1]?.body)) as Record<string, unknown>;
    expect(sent.label).toBe("");
    expect(sent.criteria).toEqual({ sniCodes: ["62010"], municipalityCodes: ["0180"] });
  });

  it("404 → notFound", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await updateCriterion(VALID_ID, body)).toEqual({ kind: "notFound" });
  });

  it("400 → validation", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(400));
    expect(await updateCriterion(VALID_ID, body)).toEqual({
      kind: "validation",
      message: null,
    });
  });
});

// ── deleteCriterion ─────────────────────────────────────────────────────────

describe("deleteCriterion — hard delete", () => {
  it("non-GUID id → notFound without a backend round-trip", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    expect(await deleteCriterion("nope")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(204));
    expect(await deleteCriterion(VALID_ID)).toEqual({ kind: "ok", data: undefined });
  });

  it("404 (repeat delete — row already gone) → notFound (action treats it as success)", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(404));
    expect(await deleteCriterion(VALID_ID)).toEqual({ kind: "notFound" });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(emptyResponse(401));
    expect(await deleteCriterion(VALID_ID)).toEqual({ kind: "unauthorized" });
  });
});
