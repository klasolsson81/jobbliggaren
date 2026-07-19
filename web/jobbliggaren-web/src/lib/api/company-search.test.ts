import { describe, it, expect, vi } from "vitest";

// The module is `server-only` and imports `@/lib/env` (accessed at call time, not here). Mock env so
// importing the module never touches a real BACKEND_URL — buildSearchBody is a pure function we call
// directly, no fetch involved.
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

import { buildSearchBody } from "./company-search";
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
