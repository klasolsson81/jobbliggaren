import { describe, it, expect } from "vitest";
import { composeSuggestionChip } from "./chip-composition";
import type { JobbUrlState } from "./search-params";
import type { SuggestionDto } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const taxonomy: TaxonomyTree = {
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [
        { conceptId: "AvNB_uwa_6n6", label: "Stockholm" },
        { conceptId: "zHxw_uJZ_NNh", label: "Solna" },
      ],
    },
    {
      conceptId: "oDpK_oQy_3Zc",
      label: "Västra Götalands län",
      municipalities: [{ conceptId: "PVZL_BQT_XtL", label: "Göteborg" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
        { conceptId: "Q5DF_juj_8do", label: "Mjukvaruarkitekt" },
      ],
    },
  ],
};

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  sortBy: "PublishedAtDesc",
};

function s(
  kind: SuggestionDto["kind"],
  conceptId: string | null,
  label: string,
): SuggestionDto {
  return { kind, conceptId, label };
}

describe("composeSuggestionChip (ADR 0067 Fas E2d)", () => {
  it("Title → fri residual-q, rör inte dimensionerna", () => {
    const next = composeSuggestionChip(
      s("Title", null, "AI engineer"),
      { ...empty, occupationGroup: ["MVqp_eS8_kDZ"] },
      taxonomy,
    );
    expect(next.q).toBe("AI engineer");
    expect(next.occupationGroup).toEqual(["MVqp_eS8_kDZ"]);
  });

  it("Title APPENDAR till befintlig q med ci-dedupe (E2i — ersätter aldrig)", () => {
    const next = composeSuggestionChip(
      s("Title", null, "AI Engineer"),
      { ...empty, q: "volvo ai" },
      taxonomy,
    );
    // "ai" finns redan (ci) → bara "Engineer" appendas; "volvo" bevaras.
    expect(next.q).toBe("volvo ai Engineer");
  });

  it("OccupationGroup → läggs till (OR-inom), bevarar q + andra dimensioner", () => {
    const next = composeSuggestionChip(
      s("OccupationGroup", "MVqp_eS8_kDZ", "Systemutvecklare"),
      { ...empty, q: "remote", region: ["CifL_Rzy_Mku"] },
      taxonomy,
    );
    expect(next.occupationGroup).toEqual(["MVqp_eS8_kDZ"]);
    expect(next.q).toBe("remote");
    expect(next.region).toEqual(["CifL_Rzy_Mku"]);
  });

  it("OccupationGroup redan vald → dedupe (idempotent)", () => {
    const next = composeSuggestionChip(
      s("OccupationGroup", "MVqp_eS8_kDZ", "Systemutvecklare"),
      { ...empty, occupationGroup: ["MVqp_eS8_kDZ"] },
      taxonomy,
    );
    expect(next.occupationGroup).toEqual(["MVqp_eS8_kDZ"]);
  });

  it("OccupationField → materialiserar alla barn-yrkesgrupper (VAL 2a)", () => {
    const next = composeSuggestionChip(
      s("OccupationField", "apaJ_2ja_LuF", "Data/IT"),
      empty,
      taxonomy,
    );
    expect(next.occupationGroup).toEqual(["MVqp_eS8_kDZ", "Q5DF_juj_8do"]);
    expect(next.q).toBe("");
  });

  it("OccupationField materialisering dedupe:ar mot redan valda barn", () => {
    const next = composeSuggestionChip(
      s("OccupationField", "apaJ_2ja_LuF", "Data/IT"),
      { ...empty, occupationGroup: ["MVqp_eS8_kDZ"] },
      taxonomy,
    );
    expect(next.occupationGroup).toEqual(["MVqp_eS8_kDZ", "Q5DF_juj_8do"]);
  });

  it("OccupationField utan taxonomi → graceful fallback till q (degraderad ACL)", () => {
    const next = composeSuggestionChip(
      s("OccupationField", "apaJ_2ja_LuF", "Data/IT"),
      empty,
      null,
    );
    expect(next.q).toBe("Data/IT");
    expect(next.occupationGroup).toEqual([]);
  });

  it("Region → läggs till + släcker länets enskilda kommun-val (per-län-norm)", () => {
    const next = composeSuggestionChip(
      s("Region", "CifL_Rzy_Mku", "Stockholms län"),
      { ...empty, municipality: ["zHxw_uJZ_NNh", "PVZL_BQT_XtL"] },
      taxonomy,
    );
    expect(next.region).toEqual(["CifL_Rzy_Mku"]);
    // Solna (Sthlm) släcks; Göteborg (VG) bevaras (annat län).
    expect(next.municipality).toEqual(["PVZL_BQT_XtL"]);
  });

  it("Municipality → läggs till + släcker sitt läns helläns-val (applyMunicipalityChange)", () => {
    const next = composeSuggestionChip(
      s("Municipality", "zHxw_uJZ_NNh", "Solna"),
      { ...empty, region: ["CifL_Rzy_Mku"] },
      taxonomy,
    );
    expect(next.municipality).toEqual(["zHxw_uJZ_NNh"]);
    // Hela Stockholms län släcks (kommun-val ersätter helläns-valet).
    expect(next.region).toEqual([]);
  });

  it("Municipality redan vald → dedupe (no-op)", () => {
    const current = { ...empty, municipality: ["zHxw_uJZ_NNh"] };
    const next = composeSuggestionChip(
      s("Municipality", "zHxw_uJZ_NNh", "Solna"),
      current,
      taxonomy,
    );
    expect(next.municipality).toEqual(["zHxw_uJZ_NNh"]);
  });

  it("cross-län-mix bevaras: kommun i annat län + region (backend-union)", () => {
    const next = composeSuggestionChip(
      s("Municipality", "zHxw_uJZ_NNh", "Solna"),
      { ...empty, region: ["oDpK_oQy_3Zc"] },
      taxonomy,
    );
    // Solna i Sthlm rör inte VG-läns helläns-val.
    expect(next.region).toEqual(["oDpK_oQy_3Zc"]);
    expect(next.municipality).toEqual(["zHxw_uJZ_NNh"]);
  });

  it("dimension-förslag med null conceptId → no-op (defensivt)", () => {
    const next = composeSuggestionChip(
      s("Region", null, "Trasig nod"),
      { ...empty, region: ["CifL_Rzy_Mku"] },
      taxonomy,
    );
    expect(next.region).toEqual(["CifL_Rzy_Mku"]);
  });
});
