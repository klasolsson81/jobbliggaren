import { describe, it, expect } from "vitest";
import {
  companyLookupSchema,
  isPersonnummerShapedOrgNr,
  normalizeOrgNrInput,
} from "./company-registry";

describe("normalizeOrgNrInput (#454)", () => {
  it("accepterar 10 siffror, strippar bindestreck och mellanslag", () => {
    expect(normalizeOrgNrInput("5560125790")).toBe("5560125790");
    expect(normalizeOrgNrInput("556012-5790")).toBe("5560125790");
    expect(normalizeOrgNrInput(" 556012 5790 ")).toBe("5560125790");
  });

  it("returnerar null för allt annat (submit-gaten)", () => {
    expect(normalizeOrgNrInput("")).toBeNull();
    expect(normalizeOrgNrInput("55601257")).toBeNull();
    expect(normalizeOrgNrInput("55601257901")).toBeNull();
    expect(normalizeOrgNrInput("556012579a")).toBeNull();
  });
});

describe("isPersonnummerShapedOrgNr (#454 — FE-spegel av backend-heuristiken)", () => {
  it("tredje siffran < 2 ⇒ pnr-shaped (enskild firma-rummet)", () => {
    expect(isPersonnummerShapedOrgNr("1901012384")).toBe(true); // 3:e = 0
    expect(isPersonnummerShapedOrgNr("9011011234")).toBe(true); // 3:e = 1
  });

  it("tredje siffran >= 2 ⇒ juridisk person", () => {
    expect(isPersonnummerShapedOrgNr("5560125790")).toBe(false); // 3:e = 6
    expect(isPersonnummerShapedOrgNr("5592804784")).toBe(false); // 3:e = 9
  });

  it("fail-safe: oväntad form behandlas som känslig", () => {
    expect(isPersonnummerShapedOrgNr("")).toBe(true);
    expect(isPersonnummerShapedOrgNr("abc")).toBe(true);
  });
});

describe("companyLookupSchema (#454 — wire-kontraktet)", () => {
  it("parsar found-shapen", () => {
    const parsed = companyLookupSchema.safeParse({
      status: "found",
      organizationNumber: "5560125790",
      isProtectedIdentity: false,
      companyName: "Volvo Aktiebolag",
      activeAdCount: 0,
      matchingAdCount: null,
      companyWatchId: null,
    });
    expect(parsed.success).toBe(true);
  });

  it("parsar notFound/unavailable-shaperna och avvisar okänd status", () => {
    const empty = {
      organizationNumber: null,
      isProtectedIdentity: false,
      companyName: null,
      activeAdCount: 0,
      matchingAdCount: null,
      companyWatchId: null,
    };
    expect(companyLookupSchema.safeParse({ ...empty, status: "notFound" }).success).toBe(true);
    expect(companyLookupSchema.safeParse({ ...empty, status: "unavailable" }).success).toBe(true);
    expect(companyLookupSchema.safeParse({ ...empty, status: "nope" }).success).toBe(false);
  });
});
