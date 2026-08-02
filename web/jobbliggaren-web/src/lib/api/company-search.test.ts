import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// The module is `server-only` and imports `@/lib/env` (accessed at call time, not here). Mock env so
// importing the module never touches a real BACKEND_URL. `buildSearchBody` is a pure function we call
// directly; the `searchCompanies` block below stubs `global.fetch` (the sibling `company-criteria`
// client test's pattern) so the RESPONSE half runs through the real zod schema.
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId: getSessionIdMock }));

import { buildSearchBody, searchCompanies } from "./company-search";
import type { CompanySearchCriteria } from "@/lib/dto/company-search";

const base: CompanySearchCriteria = {
  sniCodes: [],
  municipalityCodes: [],
  page: 1,
  pageSize: 20,
};

describe("buildSearchBody", () => {
  it("always carries page and pageSize", () => {
    expect(buildSearchBody(base)).toEqual({ page: 1, pageSize: 20 });
  });

  it("omits empty axes (absent axis = don't filter)", () => {
    const body = buildSearchBody(base);
    expect("name" in body).toBe(false);
    expect("sniCodes" in body).toBe(false);
    expect("municipalityCodes" in body).toBe(false);
  });

  it("includes name (trimmed) only when non-empty", () => {
    expect(buildSearchBody({ ...base, name: "volvo" }).name).toBe("volvo");
    expect(buildSearchBody({ ...base, name: "  volvo  " }).name).toBe("volvo");
    expect("name" in buildSearchBody({ ...base, name: "   " })).toBe(false);
  });

  it("includes non-empty code axes", () => {
    const body = buildSearchBody({
      ...base,
      sniCodes: ["62010"],
      municipalityCodes: ["0180"],
    });
    expect(body.sniCodes).toEqual(["62010"]);
    expect(body.municipalityCodes).toEqual(["0180"]);
  });

  it("NEVER carries an organizationNumber key (the org.nr invariant — D8(c))", () => {
    // The RSC search body physically cannot contain org.nr: CompanySearchCriteria has no such
    // field, and buildSearchBody never sets one. This is the runtime belt to the compile-time braces.
    expect("organizationNumber" in buildSearchBody(base)).toBe(false);
    expect(
      "organizationNumber" in
        buildSearchBody({
          ...base,
          name: "volvo",
          sniCodes: ["62010"],
          municipalityCodes: ["0180"],
        }),
    ).toBe(false);
  });
});

// ── searchCompanies — the RESPONSE half ─────────────────────────────────────

const COMPANY = {
  organizationNumber: "5592804784",
  isProtectedIdentity: false,
  name: "Acme Bygg AB",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: ["62010"],
};

const originalFetch = global.fetch;

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/**
 * #1149 — the seam BETWEEN the two halves that were already pinned.
 *
 * `CompanySearchEndpointTests` pins that the wire carries JSON `null` for a browse-all.
 * `foretag-sok-results.test.tsx` pins what the component renders once it holds `null`.
 * `companySearchResponseSchema` is what carries the value from one to the other, and nothing
 * exercised it: every component test stubs `searchCompanies` itself, i.e. ABOVE the parse.
 *
 * That gap is silent by construction. A zod failure is not a type error and not a crash —
 * `responseToResult` swallows it into `{ kind: "error" }`, which `/foretag/sok` renders as the
 * civic "Sökningen kunde inte genomföras" notice. Reverting `.nullable()` therefore breaks the
 * DEFAULT view of the page (browse-all is what an empty filter sends) with the whole suite green.
 *
 * Fixtures are the shapes the endpoint actually emits: `companies.totalCount` and `magnitude` are
 * the SAME count over the SAME predicate with different caps, so a browse-all saturates the
 * pagination count at `MaxServableRows` (2 000 at pageSize 20) while carrying no magnitude, and a
 * filtered search below both caps carries the one number twice.
 */
describe("searchCompanies — the magnitude is NULLABLE on the wire, never absent", () => {
  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("parses a browse-all response whose magnitude is null", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        companies: { items: [COMPANY], totalCount: 2000, page: 1, pageSize: 20 },
        magnitude: null,
      }),
    );

    const result = await searchCompanies(base);

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.magnitude).toBeNull();
      // The page survives the null — a browse-all still serves rows.
      expect(result.data.companies.items).toHaveLength(1);
    }
  });

  it("still parses a filtered response that carries a magnitude object", async () => {
    // The counterfactual. Without it, a schema narrowed to `z.null()` would satisfy the case
    // above and silently break every filtered search.
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        companies: { items: [COMPANY], totalCount: 1234, page: 1, pageSize: 20 },
        magnitude: { magnitude: 1234, saturated: false },
      }),
    );

    const result = await searchCompanies({ ...base, name: "acme" });

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.magnitude).toEqual({ magnitude: 1234, saturated: false });
    }
  });

  it("REJECTS a response with no magnitude key at all (nullable, not optional)", async () => {
    // The schema's own claim: the key is always on the wire, so the shape does not vary with the
    // filter — `CompanySearchEndpointTests` pins the backend side of that (property present, value
    // null). `optional()` would accept both and lose the distinction; this is what separates them.
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        companies: { items: [COMPANY], totalCount: 2000, page: 1, pageSize: 20 },
      }),
    );

    expect((await searchCompanies(base)).kind).toBe("error");
  });
});
