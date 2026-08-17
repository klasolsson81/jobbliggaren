import { describe, it, expect } from "vitest";
import {
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

/**
 * #1075 — the written-form contract. The accept pairs below are the SAME fixtures as
 * `tests/Jobbliggaren.Domain.UnitTests/CompanyWatches/OrganizationNumberTests.cs`
 * (`TryFromWrittenForm_ForWrittenForms_NormalisesToStoredForm`), so the two sides agree
 * literally rather than coincidentally. Drift on the VALUE axis, in either direction, is
 * the defect; the separator repertoire is deliberately wider here (see the module docblock).
 */
describe("normalizeOrgNrInput — den tolvsiffriga sekelformen (#1075)", () => {
  it("strippar sekelprefixet 19/20 och ger domänens tiosiffriga värde", () => {
    // Fixture pairs shared with OrganizationNumberTests.cs.
    expect(normalizeOrgNrInput("199001011234")).toBe("9001011234");
    expect(normalizeOrgNrInput("19900101-1234")).toBe("9001011234");
    expect(normalizeOrgNrInput("200010100000")).toBe("0010100000");
    // The forms issue #1075 measured reaching ?namn=.
    expect(normalizeOrgNrInput("195601257901")).toBe("5601257901");
    expect(normalizeOrgNrInput("19560125-7901")).toBe("5601257901");
    expect(normalizeOrgNrInput("191010101010")).toBe("1010101010");
    expect(normalizeOrgNrInput("19101010-1010")).toBe("1010101010");
  });

  it("en sekelform vars tredje siffra >= 2 är en juridisk person — uppslagning, inte avslag", () => {
    // The century strip decides the SHAPE; isPersonnummerShapedOrgNr decides the POSTURE, and it
    // runs on the normalised form. Backend `/companies/search` folds and answers identically.
    expect(normalizeOrgNrInput("205560125790")).toBe("5560125790");
    expect(isPersonnummerShapedOrgNr("5560125790")).toBe(false);
  });

  it("varje verklig personnummerform blir pnr-formad EFTER normaliseringen", () => {
    // The month tens digit lands on index 2 once the century is gone, so it is 0 or 1 for every
    // real date. This is why the widening needs no second discriminator.
    for (const raw of ["195601257901", "19560125-7901", "191010101010", "199001011234"]) {
      const normalised = normalizeOrgNrInput(raw);
      expect(normalised).not.toBeNull();
      expect(isPersonnummerShapedOrgNr(normalised as string)).toBe(true);
    }
  });

  it("accepterar hela husets separatorklass (#497), inte bara bindestreck", () => {
    // `Personnummer.IsSeparator`: ASCII '+', U+2212 MINUS SIGN, and any \p{Pd} (which covers
    // ASCII '-', U+2013 EN DASH and U+2011 NON-BREAKING HYPHEN — the paste path).
    expect(normalizeOrgNrInput("101010–1010")).toBe("1010101010"); // EN DASH
    expect(normalizeOrgNrInput("101010‑1010")).toBe("1010101010"); // NON-BREAKING HYPHEN
    expect(normalizeOrgNrInput("101010−1010")).toBe("1010101010"); // MINUS SIGN
    expect(normalizeOrgNrInput("101010+1010")).toBe("1010101010"); // the 100+ century separator
    expect(normalizeOrgNrInput("19560125–7901")).toBe("5601257901");
  });

  it("separatorer var som helst — FE:ns egen sanktionerade bredd mot domänens en-position-regel", () => {
    // The domain removes ONE hyphen and only immediately before the last four digits, so
    // "5592-804784" is null there. Here it normalises: this side gates typed dispatch, it never
    // matches stored data. Deliberate, and pinned so nobody "restores parity" by narrowing a guard.
    expect(normalizeOrgNrInput("5592-804784")).toBe("5592804784");
    expect(normalizeOrgNrInput("19 900101 1234")).toBe("9001011234");
  });

  it("null för allt utanför värdekontraktet — breddningens gräns", () => {
    expect(normalizeOrgNrInput("189001011234")).toBeNull(); // 18xx is not an accepted century
    expect(normalizeOrgNrInput("219001011234")).toBeNull(); // nor is 21xx
    expect(normalizeOrgNrInput("1990010112345")).toBeNull(); // 13 digits
    expect(normalizeOrgNrInput("19556012579a")).toBeNull(); // 12 chars, 19-prefix, non-digit tail
    // No Unicode-digit folding: the backend's `[0-9]{10}` default-deny (#865) would reject the
    // value this would derive, which WOULD be a value-axis divergence.
    expect(normalizeOrgNrInput("５５６０１２５７９０")).toBeNull();
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
