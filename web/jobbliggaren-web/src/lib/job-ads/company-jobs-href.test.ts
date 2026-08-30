import { describe, it, expect } from "vitest";
import { buildCompanyJobsHref } from "./company-jobs-href";
import { parseEmployerParam } from "./search-params";

const ORG_NR = "5592804784";

/**
 * #1547 — the URL contract for the two links a watch row renders. Asserted as exact
 * strings, not composed from the constants under test: a composed expectation would
 * follow a separator or grade-order change instead of failing on it (precedent
 * `search-params.test.ts`, which writes its axis URLs out in full).
 */
describe("buildCompanyJobsHref (#1547)", () => {
  it("scope 'all' → arbetsgivar-axeln ensam, inga andra params", () => {
    expect(buildCompanyJobsHref([ORG_NR], "all")).toBe("/jobb?employer=5592804784");
  });

  it("scope 'matching' → arbetsgivaren plus grad-delmängden Good.Strong", () => {
    expect(buildCompanyJobsHref([ORG_NR], "matching")).toBe(
      "/jobb?employer=5592804784&matchGrades=Good.Strong",
    );
  });

  it("matchande-länken bär ALDRIG baraMatchade eller matchning=off", () => {
    // The deleted originator (`company-lookup.tsx`, removed in `aca39970`) linked its
    // matching action with `?baraMatchade=on`. That maps to `onlyMatched`, which
    // `ListJobAdsQueryHandler.cs:122-125` expands to the whole filterable band
    // [Basic, Related, Good, Strong] — wider than the [Good, Strong] the row's count is
    // computed at, so the destination would hold MORE ads than the number promised.
    // `matchning=off` is the other trap: it would filter the list while hiding every
    // visual trace of the filter, because the grade chips render only when matching is on.
    const href = buildCompanyJobsHref([ORG_NR], "matching");
    expect(href).not.toContain("baraMatchade");
    expect(href).not.toContain("matchning=off");
  });

  it("org.nr:et bärs ordagrant och överlever appens egen parser", () => {
    // Writer/reader symmetry. `parseEmployerParam` drops anything that is not `^\d{10}$`
    // SILENTLY, so a formatted number ("559280-4784") would produce a link that looks
    // right and shows EVERY ad instead of the employer's.
    const href = buildCompanyJobsHref([ORG_NR], "all");
    expect(href).not.toBeNull();
    const raw = new URLSearchParams(href!.slice(href!.indexOf("?"))).get("employer");
    expect(raw).toBe(ORG_NR);
    expect(parseEmployerParam(raw ?? undefined)).toEqual([ORG_NR]);
  });

  it.each([
    ["formaterat org.nr", "559280-4784"],
    ["för kort", "55928047"],
    ["för långt", "55928047840"],
    ["med inledande blanksteg", " 5592804784"],
    ["tomt", ""],
  ])("vägrar bygga en länk för %s", (_label, value) => {
    // The writer floor mirrors the reader: `parseEmployerParam` drops a mismatch SILENTLY, so
    // without this the link would look right and the page would show EVERY ad. Deliberately a
    // FORMAT floor and not a personnummer discriminator -- that would give IsPersonnummerShaped
    // a second home, which the house rejected once (#844).
    expect(buildCompanyJobsHref([value], "all")).toBeNull();
    expect(buildCompanyJobsHref([value], "matching")).toBeNull();
  });
});
