import { describe, it, expect } from "vitest";
import {
  buildForetagSokHref,
  buildPageHref,
  toStringList,
  parseNamn,
  parseSida,
  normalizeCodes,
  MAX_NAME_PREFIX_LENGTH,
  MAX_PAGE,
  type ForetagSokUrlState,
} from "./search-params";

const empty: ForetagSokUrlState = { namn: "", sni: [], kommun: [] };

describe("buildForetagSokHref (filter/name changes)", () => {
  it("returns the bare route when no axis is set", () => {
    expect(buildForetagSokHref(empty)).toBe("/foretag/sok");
  });

  it("writes namn only when non-empty (trimmed)", () => {
    expect(buildForetagSokHref({ ...empty, namn: "volvo" })).toBe(
      "/foretag/sok?namn=volvo",
    );
    expect(buildForetagSokHref({ ...empty, namn: "  volvo  " })).toBe(
      "/foretag/sok?namn=volvo",
    );
    expect(buildForetagSokHref({ ...empty, namn: "   " })).toBe("/foretag/sok");
  });

  it("appends sni and kommun as repeated params", () => {
    expect(buildForetagSokHref({ ...empty, sni: ["62010", "10710"] })).toBe(
      "/foretag/sok?sni=10710&sni=62010",
    );
    expect(buildForetagSokHref({ ...empty, kommun: ["0180", "1480"] })).toBe(
      "/foretag/sok?kommun=0180&kommun=1480",
    );
  });

  it("sorts each axis so shared links get a stable form", () => {
    // Same selection in a different order must serialize identically.
    const a = buildForetagSokHref({ ...empty, sni: ["62010", "10710", "01131"] });
    const b = buildForetagSokHref({ ...empty, sni: ["01131", "62010", "10710"] });
    expect(a).toBe(b);
    expect(a).toBe("/foretag/sok?sni=01131&sni=10710&sni=62010");
  });

  it("orders axes sni -> kommun -> namn (stable URL form)", () => {
    expect(
      buildForetagSokHref({ namn: "volvo", sni: ["62010"], kommun: ["0180"] }),
    ).toBe("/foretag/sok?sni=62010&kommun=0180&namn=volvo");
  });

  it("never emits sida (a filter change resets to page 1)", () => {
    expect(buildForetagSokHref({ ...empty, namn: "volvo" })).not.toContain(
      "sida",
    );
  });

  it("round-trips repeated params through URLSearchParams.getAll", () => {
    const href = buildForetagSokHref({ ...empty, kommun: ["0180", "1480"] });
    const qs = href.slice(href.indexOf("?") + 1);
    expect(new URLSearchParams(qs).getAll("kommun")).toEqual(["0180", "1480"]);
  });

  it("never emits an org.nr param (D8(c) — org.nr lives only in the island POST body)", () => {
    // The org.nr invariant is enforced at the type level: ForetagSokUrlState has no
    // organizationNumber/orgnr field, so no builder can serialize one. This asserts the
    // resulting URL for a fully-populated state carries no org.nr key.
    const href = buildForetagSokHref({
      namn: "volvo",
      sni: ["62010"],
      kommun: ["0180"],
    });
    expect(href).not.toContain("organizationNumber");
    expect(href).not.toContain("orgnr");
  });
});

describe("buildPageHref (pagination)", () => {
  it("omits sida for page 1 (the param's absence is a clean URL)", () => {
    expect(buildPageHref({ ...empty, namn: "volvo" }, 1)).toBe(
      "/foretag/sok?namn=volvo",
    );
    expect(buildPageHref(empty, 1)).toBe("/foretag/sok");
  });

  it("writes sida only when the target page is > 1", () => {
    expect(buildPageHref({ ...empty, namn: "volvo" }, 3)).toBe(
      "/foretag/sok?namn=volvo&sida=3",
    );
  });

  it("preserves the active filter axes alongside the page", () => {
    const href = buildPageHref(
      { namn: "volvo", sni: ["62010"], kommun: ["0180"] },
      2,
    );
    expect(href).toBe("/foretag/sok?sni=62010&kommun=0180&namn=volvo&sida=2");
  });

  it("never emits an org.nr param either", () => {
    const href = buildPageHref({ namn: "volvo", sni: ["62010"], kommun: [] }, 2);
    expect(href).not.toContain("organizationNumber");
    expect(href).not.toContain("orgnr");
  });
});

describe("toStringList", () => {
  it("normalizes undefined / single / repeated params and drops empties", () => {
    expect(toStringList(undefined)).toEqual([]);
    expect(toStringList("62010")).toEqual(["62010"]);
    expect(toStringList(["62010", "10710"])).toEqual(["62010", "10710"]);
    expect(toStringList(["62010", "", "10710"])).toEqual(["62010", "10710"]);
  });
});

describe("parseNamn", () => {
  it("takes the first value, trims, and returns '' when absent", () => {
    expect(parseNamn(undefined)).toBe("");
    expect(parseNamn("  volvo  ")).toBe("volvo");
    expect(parseNamn(["volvo", "saab"])).toBe("volvo");
  });

  it("truncates to the max prefix length (no sub-minimum — a 1-char prefix is valid)", () => {
    expect(parseNamn("a")).toBe("a");
    const long = "x".repeat(MAX_NAME_PREFIX_LENGTH + 50);
    expect(parseNamn(long)).toHaveLength(MAX_NAME_PREFIX_LENGTH);
  });
});

describe("parseSida", () => {
  it("defaults to 1 for absent or invalid input", () => {
    expect(parseSida(undefined)).toBe(1);
    expect(parseSida("0")).toBe(1);
    expect(parseSida("-3")).toBe(1);
    expect(parseSida("abc")).toBe(1);
  });

  it("parses a positive integer and caps at MAX_PAGE", () => {
    expect(parseSida("5")).toBe(5);
    expect(parseSida(["7", "9"])).toBe(7);
    expect(parseSida(String(MAX_PAGE + 100))).toBe(MAX_PAGE);
  });
});

describe("normalizeCodes (drop-unknown + dedupe + cap)", () => {
  const allowed = new Set(["62010", "10710", "0180"]);

  it("drops codes not in the allowlist (a manipulated URL never 400s the search)", () => {
    expect(normalizeCodes(["62010", "99998", "10710"], 100, allowed)).toEqual([
      "62010",
      "10710",
    ]);
  });

  it("dedupes while preserving order", () => {
    expect(normalizeCodes(["62010", "62010", "10710"], 100, allowed)).toEqual([
      "62010",
      "10710",
    ]);
  });

  it("caps the list length", () => {
    expect(normalizeCodes(["62010", "10710"], 1, allowed)).toEqual(["62010"]);
  });

  it("degraded reference (no allowlist): dedupes + caps only, backend is the last barrier", () => {
    expect(normalizeCodes(["99998", "99998", "12345"], 100)).toEqual([
      "99998",
      "12345",
    ]);
  });
});
