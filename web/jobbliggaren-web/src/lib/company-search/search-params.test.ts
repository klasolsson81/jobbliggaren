import { describe, it, expect } from "vitest";
import {
  buildForetagSokHref,
  buildPageHref,
  buildOrgNrRefusedHref,
  parseOrgNrRefused,
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
    expect(parseNamn(undefined)).toEqual({ kind: "name", value: "" });
    expect(parseNamn("  volvo  ")).toEqual({ kind: "name", value: "volvo" });
    expect(parseNamn(["volvo", "saab"])).toEqual({ kind: "name", value: "volvo" });
  });

  it("truncates to the max prefix length (no sub-minimum — a 1-char prefix is valid)", () => {
    expect(parseNamn("a")).toEqual({ kind: "name", value: "a" });
    const long = "x".repeat(MAX_NAME_PREFIX_LENGTH + 50);
    const parsed = parseNamn(long);
    expect(parsed.kind).toBe("name");
    expect(parsed.kind === "name" && parsed.value).toHaveLength(
      MAX_NAME_PREFIX_LENGTH,
    );
  });
});

/**
 * ADR 0087 D8(c) — the `namn` axis must refuse the whole ten-digit class, not merely the
 * personnummer-shaped subclass. The gate fires on the SAME predicate the search island uses to route
 * a value to the org.nr branch, so the two paths cannot drift into two rules.
 */
describe("parseNamn — the org.nr gate", () => {
  it("refuses the ten-digit class in every form the org.nr normaliser accepts", () => {
    // Personnummer-shaped (3rd digit < 2) — the highest-priority case.
    expect(parseNamn("1010101010")).toEqual({ kind: "orgNrShaped" });
    // A legitimate legal-entity org.nr is refused on this axis too: it is an org.nr, and org.nr
    // never enters a URL. Gating on the pnr heuristic instead would make one rule into two.
    expect(parseNamn("5560125790")).toEqual({ kind: "orgNrShaped" });
    // Hyphenated and spaced forms normalise to the same ten digits.
    expect(parseNamn("101010-1010")).toEqual({ kind: "orgNrShaped" });
    expect(parseNamn("10 10 10 1010")).toEqual({ kind: "orgNrShaped" });
    expect(parseNamn("  556012-5790  ")).toEqual({ kind: "orgNrShaped" });
    // A repeated param is gated on its FIRST value, the one the parser would otherwise use.
    expect(parseNamn(["1010101010", "volvo"])).toEqual({ kind: "orgNrShaped" });
  });

  it("leaves real name prefixes alone — the gate is exact, not a digit heuristic", () => {
    expect(parseNamn("volvo")).toEqual({ kind: "name", value: "volvo" });
    expect(parseNamn("101010101")).toEqual({ kind: "name", value: "101010101" }); // 9
    expect(parseNamn("10101010101")).toEqual({ kind: "name", value: "10101010101" }); // 11
    expect(parseNamn("1010101010ab")).toEqual({ kind: "name", value: "1010101010ab" });
    expect(parseNamn("Bolag 1010101010")).toEqual({
      kind: "name",
      value: "Bolag 1010101010",
    });
  });
});

describe("buildOrgNrRefusedHref", () => {
  it("drops the name, preserves the filter axes, and never emits sida", () => {
    const href = buildOrgNrRefusedHref({
      namn: "",
      sni: ["62020", "62010"],
      kommun: ["0180"],
    });
    expect(href).toContain("avvisat=orgnr");
    expect(href).not.toContain("namn=");
    expect(href).toContain("sni=62010");
    expect(href).toContain("sni=62020");
    expect(href).toContain("kommun=0180");
    expect(href).not.toContain("sida=");
  });

  it("carries the flag even when there is no filter to preserve", () => {
    expect(buildOrgNrRefusedHref({ namn: "", sni: [], kommun: [] })).toBe(
      "/foretag/sok?avvisat=orgnr",
    );
  });

  /**
   * The no-loop pin. The redirect target must parse back to a plain empty name — otherwise the wash
   * redirect would re-trigger its own gate and the page would loop. Pinned, not reasoned about.
   */
  it("produces a target whose own `namn` parses as a name, not as orgNrShaped", () => {
    const href = buildOrgNrRefusedHref({
      namn: "",
      sni: ["62010"],
      kommun: [],
    });
    const target = new URLSearchParams(href.slice(href.indexOf("?") + 1));
    expect(parseNamn(target.get("namn") ?? undefined)).toEqual({
      kind: "name",
      value: "",
    });
    expect(parseOrgNrRefused(target.get("avvisat") ?? undefined)).toBe(true);
  });
});

describe("parseOrgNrRefused", () => {
  it("recognises only the exact flag value", () => {
    expect(parseOrgNrRefused("orgnr")).toBe(true);
    expect(parseOrgNrRefused(["orgnr", "x"])).toBe(true);
    expect(parseOrgNrRefused(undefined)).toBe(false);
    expect(parseOrgNrRefused("1")).toBe(false);
    expect(parseOrgNrRefused("ORGNR")).toBe(false);
  });

  /** The flag is a wash artefact: neither commit builder may emit it, so it dies on the next action. */
  it("is never emitted by either commit builder", () => {
    const state = { namn: "volvo", sni: ["62010"], kommun: ["0180"] };
    expect(buildForetagSokHref(state)).not.toContain("avvisat");
    expect(buildPageHref(state, 3)).not.toContain("avvisat");
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
