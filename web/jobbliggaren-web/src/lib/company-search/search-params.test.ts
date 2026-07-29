import { describe, it, expect } from "vitest";
import {
  buildForetagSokHref,
  buildPageHref,
  buildOrgNrRefusedHref,
  parseOrgNrRefused,
  parseCodeAxis,
  serializeCodeAxis,
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

  it("writes ONE param per axis, codes joined", () => {
    expect(buildForetagSokHref({ ...empty, sni: ["62010", "10710"] })).toBe(
      "/foretag/sok?sni=10710-62010",
    );
    expect(buildForetagSokHref({ ...empty, kommun: ["0180", "1480"] })).toBe(
      "/foretag/sok?kommun=0180-1480",
    );
  });

  /**
   * The reason the form changed, asserted as a property rather than trusted to the comment.
   *
   * Next's client router cache collapses REPEATED query keys to the last value, so under the old
   * form `?kommun=A&kommun=B` and `?kommun=B` shared one cache entry and the second navigation
   * fetched nothing. Under one-param-per-axis, two different applied states cannot collide.
   *
   * The oracle is exercised in BOTH directions on purpose. A first version asserted only that the
   * two current hrefs do not collapse together — and that assertion cannot fail for the reason it
   * names: any roughly-injective transform satisfies a `not.toBe` between two already-different
   * strings, so replacing `collapse` with the identity function left it green. code-reviewer
   * measured exactly that. The counterfactual below is what makes the oracle load-bearing: it
   * pins that `collapse` DOES fuse the old form, which is the property the whole PR rests on.
   */
  it("no two distinct filter states can collapse to the same router cache key", () => {
    const collapse = (href: string) => {
      const qs = href.slice(href.indexOf("?") + 1);
      const out = new Map<string, string>();
      for (const [k, v] of new URLSearchParams(qs)) out.set(k, v);
      return [...out].map(([k, v]) => `${k}=${v}`).join("&");
    };

    // The oracle must FUSE the old repeated form — otherwise it measures nothing below.
    expect(collapse("/foretag/sok?kommun=0180&kommun=1480")).toBe(
      collapse("/foretag/sok?kommun=1480"),
    );

    const both = buildForetagSokHref({ ...empty, kommun: ["0180", "1480"] });
    const second = buildForetagSokHref({ ...empty, kommun: ["1480"] });
    expect(both).not.toBe(second);
    // ...and it must NOT fuse what the builders write today.
    expect(collapse(both)).not.toBe(collapse(second));
  });

  /**
   * The same oracle, applied to the one producer that cannot call a builder: the search island's
   * no-JS form serialises its own hidden fields. It went unnoticed in the first round of this PR
   * because the existing native-GET assertion used a SINGLE code, where `?kommun=0180` is
   * byte-identical under both shapes. `serializeCodeAxis` is the shared point that keeps them
   * honest; this pins its output rather than the form's, so the pin survives a refactor of either.
   */
  it("serializeCodeAxis produces the same shape the builders write", () => {
    const href = buildForetagSokHref({ ...empty, kommun: ["1480", "0180"] });
    expect(href).toContain(`kommun=${serializeCodeAxis(["1480", "0180"])}`);
    // Sorted here too, so the two producers cannot drift on ordering.
    expect(serializeCodeAxis(["1480", "0180"])).toBe("0180-1480");
    expect(parseCodeAxis(serializeCodeAxis(["1480", "0180"]))).toEqual([
      "0180",
      "1480",
    ]);
  });

  it("sorts each axis so shared links get a stable form", () => {
    // Same selection in a different order must serialize identically.
    const a = buildForetagSokHref({ ...empty, sni: ["62010", "10710", "01131"] });
    const b = buildForetagSokHref({ ...empty, sni: ["01131", "62010", "10710"] });
    expect(a).toBe(b);
    expect(a).toBe("/foretag/sok?sni=01131-10710-62010");
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

  it("round-trips through the parser, in BOTH the joined and the legacy repeated form", () => {
    const href = buildForetagSokHref({ ...empty, kommun: ["0180", "1480"] });
    const qs = href.slice(href.indexOf("?") + 1);
    // What the builder writes today: one value.
    expect(new URLSearchParams(qs).getAll("kommun")).toEqual(["0180-1480"]);
    // What the PARSER makes of it — and of the form every link shared before 2026-07-29
    // still carries. Both must yield the same codes, or old bookmarks break silently.
    expect(parseCodeAxis(new URLSearchParams(qs).getAll("kommun"))).toEqual([
      "0180",
      "1480",
    ]);
    expect(parseCodeAxis(["0180", "1480"])).toEqual(["0180", "1480"]);
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

describe("parseCodeAxis", () => {
  it("normalizes undefined / single / repeated params and drops empties", () => {
    expect(parseCodeAxis(undefined)).toEqual([]);
    expect(parseCodeAxis("62010")).toEqual(["62010"]);
    expect(parseCodeAxis(["62010", "10710"])).toEqual(["62010", "10710"]);
    expect(parseCodeAxis(["62010", "", "10710"])).toEqual(["62010", "10710"]);
  });

  it("reads the JOINED form the builders now write", () => {
    expect(parseCodeAxis("62010-10710")).toEqual(["62010", "10710"]);
  });

  /**
   * The back-compat obligation, stated as an assertion rather than as a promise in a docblock.
   * Every link shared or bookmarked before 2026-07-29 carries the REPEATED form; if the parser
   * stopped accepting it those links would silently lose their filter and answer the whole
   * register instead — the same silent-wash failure the org.nr refusal notice exists to avoid.
   */
  it("parses the legacy repeated form and the joined form to the SAME codes", () => {
    const legacy = parseCodeAxis(["0180", "1480", "1280"]);
    const joined = parseCodeAxis("0180-1480-1280");
    expect(joined).toEqual(legacy);
    // And a mixture, which a hand-edited or half-migrated link can produce.
    expect(parseCodeAxis(["0180-1480", "1280"])).toEqual(legacy);
  });

  it("trims each value — new behaviour the old parser did not have", () => {
    expect(parseCodeAxis(" 0180 ")).toEqual(["0180"]);
    expect(parseCodeAxis(" 0180 - 1480 ")).toEqual(["0180", "1480"]);
  });

  it("drops empty segments left by a stray separator", () => {
    expect(parseCodeAxis("-0180--1480-")).toEqual(["0180", "1480"]);
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
    expect(parseNamn(["1010101010", "volvo"])).toEqual({ kind: "orgNrShaped" });
  });

  /**
   * The gate reads EVERY repeated value, not just `raw[0]`. Reproduced against the running dev
   * server before the fix: `/foretag/sok?namn=&namn=1010101010` rendered the page with no wash at
   * all, because the parser only ever looked at the value it was going to use. What reaches history,
   * a re-shared link and the access log is the whole query string.
   */
  it("refuses a ten-digit value in ANY repeated position, not only the first", () => {
    expect(parseNamn(["", "1010101010"])).toEqual({ kind: "orgNrShaped" });
    expect(parseNamn(["volvo", "1010101010"])).toEqual({ kind: "orgNrShaped" });
    expect(parseNamn(["volvo", "saab", "556012-5790"])).toEqual({
      kind: "orgNrShaped",
    });
    // ...and an all-clean repetition still parses to the first value, unchanged.
    expect(parseNamn(["volvo", "saab"])).toEqual({ kind: "name", value: "volvo" });
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
      sni: ["62020", "62010"],
      kommun: ["0180"],
    });
    expect(href).toContain("avvisat=orgnr");
    expect(href).not.toContain("namn=");
    expect(href).toContain("sni=62010-62020");
    expect(href).toContain("kommun=0180");
    expect(href).not.toContain("sida=");
  });

  it("carries the flag even when there is no filter to preserve", () => {
    expect(buildOrgNrRefusedHref({ sni: [], kommun: [] })).toBe(
      "/foretag/sok?avvisat=orgnr",
    );
  });

  /**
   * The no-loop pin. The redirect target must parse back to a plain empty name — otherwise the wash
   * redirect would re-trigger its own gate and the page would loop. Pinned, not reasoned about.
   */
  it("produces a target whose own `namn` parses as a name, not as orgNrShaped", () => {
    const href = buildOrgNrRefusedHref({
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
