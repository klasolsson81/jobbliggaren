import { describe, it, expect } from "vitest";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { buildOrtGranularityMap, classifyOrtLabel } from "./ort-granularity";

function tree(regions: TaxonomyTree["regions"]): TaxonomyTree {
  return {
    regions,
    occupationFields: [],
    employmentTypes: [],
    worktimeExtents: [],
  };
}

describe("buildOrtGranularityMap", () => {
  it("klassar läns-labels som 'region' och kommun-labels som 'municipality'", () => {
    const map = buildOrtGranularityMap(
      tree([
        {
          conceptId: "r_vg",
          label: "Västra Götalands län",
          municipalities: [{ conceptId: "m_gbg", label: "Göteborg" }],
        },
      ])
    );
    expect(map["Västra Götalands län"]).toBe("region");
    expect(map["Göteborg"]).toBe("municipality");
  });

  it("tvetydig label (både län och kommun, Gotland) → coarser 'region'", () => {
    // Gotland-fallet: namnet är BÅDE ett län och en kommun. Län skrivs först →
    // vinner; kommun-skrivningen hoppas över för en redan satt nyckel.
    const map = buildOrtGranularityMap(
      tree([
        {
          conceptId: "r_gotland",
          label: "Gotland",
          municipalities: [{ conceptId: "m_gotland", label: "Gotland" }],
        },
      ])
    );
    expect(map["Gotland"]).toBe("region");
  });

  it("null-taxonomi → tom karta (degraderar civilt)", () => {
    expect(buildOrtGranularityMap(null)).toEqual({});
  });
});

describe("classifyOrtLabel", () => {
  const map = buildOrtGranularityMap(
    tree([
      {
        conceptId: "r_sthlm",
        label: "Stockholms län",
        municipalities: [{ conceptId: "m_solna", label: "Solna" }],
      },
    ])
  );

  it("känd kommun → 'municipality'", () => {
    expect(classifyOrtLabel("Solna", map)).toBe("municipality");
  });

  it("känt län → 'region'", () => {
    expect(classifyOrtLabel("Stockholms län", map)).toBe("region");
  });

  it("okänd label (stale snapshot) → null (visas rakt av)", () => {
    expect(classifyOrtLabel("Okänd ort", map)).toBeNull();
  });
});
