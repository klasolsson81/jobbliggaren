import { describe, it, expect } from "vitest";
import {
  recentJobSearchDtoSchema,
  listRecentSearchesResultSchema,
} from "./recent-searches";

const wireBase = {
  id: "33333333-3333-3333-3333-333333333333",
  q: "backend",
  occupationGroupList: ["MVqp_eS8_kDZ"],
  municipalityList: ["zHxw_uJZ_NNh"],
  regionList: ["CifL_Rzy_Mku"],
  employmentTypeList: ["gro4_cWF_6D7"],
  worktimeExtentList: ["6YE1_gAC_R2G"],
  remote: false,
  occupationGroupLabels: [
    { conceptId: "MVqp_eS8_kDZ", label: "Mjukvaruutveckling" },
  ],
  municipalityLabels: [{ conceptId: "zHxw_uJZ_NNh", label: "Solna" }],
  regionLabels: [{ conceptId: "CifL_Rzy_Mku", label: "Stockholms län" }],
  sortBy: 0,
  // #1430 — labeln är struktur på wire:n, och enums kommer som NAMN (backend
  // JsonStringEnumConverter). Formen här är exakt vad DeriveLabel:s q-gren emitterar.
  label: {
    kind: "Query",
    join: "None",
    parts: [{ kind: "Named", text: "backend", conceptId: null, moreCount: 0 }],
  },
  currentCount: 42,
  newCount: 7,
  lastViewedAt: "2026-05-20T19:00:00Z",
};

describe("recentJobSearchDtoSchema", () => {
  it("parses a complete wire payload", () => {
    const parsed = recentJobSearchDtoSchema.parse(wireBase);
    expect(parsed.id).toBe(wireBase.id);
    expect(parsed.q).toBe("backend");
    expect(parsed.sortBy).toBe("PublishedAtDesc");
    expect(parsed.currentCount).toBe(42);
    expect(parsed.newCount).toBe(7);
  });

  it("parses the label as structure, keeping the enums as names", () => {
    const parsed = recentJobSearchDtoSchema.parse(wireBase);

    expect(parsed.label.kind).toBe("Query");
    expect(parsed.label.join).toBe("None");
    expect(parsed.label.parts).toEqual([
      { kind: "Named", text: "backend", conceptId: null, moreCount: 0 },
    ]);
  });

  it("parses a Coded part, which carries the code and no text (#1537)", () => {
    const parsed = recentJobSearchDtoSchema.parse({
      ...wireBase,
      label: {
        kind: "Dimensions",
        join: "None",
        parts: [{ kind: "Coded", text: null, conceptId: "6YE1_gAC_R2G", moreCount: 0 }],
      },
    });

    expect(parsed.label.parts).toEqual([
      { kind: "Coded", text: null, conceptId: "6YE1_gAC_R2G", moreCount: 0 },
    ]);
  });

  it("refuses a Coded part that carries text, or one with no code", () => {
    // Spegeln är exakt så snäv som kontraktet: `text` hör Named till och `conceptId`
    // hör Coded till. En del som bär båda, eller ingendera, är ett kontraktsbrott och
    // ska falla här hellre än att rendera något halvt på sidan.
    const withText = {
      ...wireBase,
      label: {
        kind: "Dimensions",
        join: "None",
        parts: [{ kind: "Coded", text: "Heltid", conceptId: "6YE1_gAC_R2G", moreCount: 0 }],
      },
    };
    const withoutCode = {
      ...wireBase,
      label: {
        kind: "Dimensions",
        join: "None",
        parts: [{ kind: "Coded", text: null, conceptId: "", moreCount: 0 }],
      },
    };

    expect(recentJobSearchDtoSchema.safeParse(withText).success).toBe(false);
    expect(recentJobSearchDtoSchema.safeParse(withoutCode).success).toBe(false);
  });

  it("accepts the All label, which is the one kind that carries no parts", () => {
    const parsed = recentJobSearchDtoSchema.parse({
      ...wireBase,
      label: { kind: "All", join: "None", parts: [] },
    });

    expect(parsed.label.kind).toBe("All");
  });

  // Högljutt före tyst fel: varje gren utom All skjuter minst en del, så en tom `parts` är
  // ett kontraktsbrott. Utan grinden hade komponeraren fått välja mellan en tom rubrik och
  // att påstå "alla annonser" om ett tillstånd den inte känner — ett falskt påstående.
  it("refuses a non-All label with no parts", () => {
    const result = recentJobSearchDtoSchema.safeParse({
      ...wireBase,
      label: { kind: "Dimensions", join: "None", parts: [] },
    });

    expect(result.success).toBe(false);
  });

  it("refuses an ordinal where the label enum expects a name", () => {
    const result = recentJobSearchDtoSchema.safeParse({
      ...wireBase,
      label: {
        kind: 0,
        join: "None",
        parts: [{ kind: "Named", text: "x", conceptId: null, moreCount: 0 }],
      },
    });

    expect(result.success).toBe(false);
  });

  it("accepts null q (only occupationGroup/region filter)", () => {
    const parsed = recentJobSearchDtoSchema.parse({ ...wireBase, q: null });
    expect(parsed.q).toBeNull();
  });

  it("defaults missing occupationGroupLabels/regionLabels to empty arrays", () => {
    const { occupationGroupLabels: _a, regionLabels: _b, ...rest } = wireBase;
    const parsed = recentJobSearchDtoSchema.parse(rest);
    expect(parsed.occupationGroupLabels).toEqual([]);
    expect(parsed.regionLabels).toEqual([]);
  });

  it("carries remote from the wire in both polarities (#1407)", () => {
    expect(recentJobSearchDtoSchema.parse({ ...wireBase, remote: true }).remote).toBe(
      true,
    );
    expect(recentJobSearchDtoSchema.parse({ ...wireBase, remote: false }).remote).toBe(
      false,
    );
  });

  it("rejects a payload with no remote field rather than defaulting it to false", () => {
    // The actor that produces this payload is not a path in src/ — it is the deploy
    // skew window #1238 names: the publish job is a five-cell matrix with no fan-in,
    // so IMAGE_TAG's `latest` default can resolve to a new web against an older api
    // that predates the field. This asserts only that the read side degrades safely
    // (throw -> responseToResult -> {kind:"error"}), never what production emits.
    // Defaulting instead would make "the API stopped sending the axis" indistinguishable
    // from "the user did not pick distans" — #1407's own failure mode, one layer out.
    const { remote: _omitted, ...withoutRemote } = wireBase;
    expect(() => recentJobSearchDtoSchema.parse(withoutRemote)).toThrow();
  });

  it("parses Relevance numeric sortBy index 4", () => {
    const parsed = recentJobSearchDtoSchema.parse({ ...wireBase, sortBy: 4 });
    expect(parsed.sortBy).toBe("Relevance");
  });

  it("rejects negative currentCount", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, currentCount: -1 })
    ).toThrow();
  });

  it("rejects negative newCount", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, newCount: -1 })
    ).toThrow();
  });

  it("rejects out-of-range sortBy index", () => {
    expect(() =>
      recentJobSearchDtoSchema.parse({ ...wireBase, sortBy: 9 })
    ).toThrow();
  });

  it("accepts multiple occupationGroup and region concept-ids", () => {
    const parsed = recentJobSearchDtoSchema.parse({
      ...wireBase,
      occupationGroupList: ["a1", "b2"],
      regionList: ["x1", "y2"],
    });
    expect(parsed.occupationGroupList).toEqual(["a1", "b2"]);
    expect(parsed.regionList).toEqual(["x1", "y2"]);
  });
});

describe("listRecentSearchesResultSchema", () => {
  it("parses an empty array", () => {
    const parsed = listRecentSearchesResultSchema.parse([]);
    expect(parsed).toEqual([]);
  });

  it("parses an array of recent searches", () => {
    const parsed = listRecentSearchesResultSchema.parse([wireBase, wireBase]);
    expect(parsed).toHaveLength(2);
  });
});
