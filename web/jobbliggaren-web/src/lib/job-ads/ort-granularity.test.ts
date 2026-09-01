import { describe, it, expect } from "vitest";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { buildOrtGranularityMap, classifyOrtConcept } from "./ort-granularity";

function tree(regions: TaxonomyTree["regions"]): TaxonomyTree {
  return {
    regions,
    occupationFields: [],
    employmentTypes: [],
    worktimeExtents: [],
  };
}

describe("buildOrtGranularityMap", () => {
  it("klassar läns-conceptId som 'region' och kommun-conceptId som 'municipality'", () => {
    const map = buildOrtGranularityMap(
      tree([
        {
          conceptId: "r_vg",
          label: "Västra Götalands län",
          municipalities: [{ conceptId: "m_gbg", label: "Göteborg" }],
        },
      ])
    );
    expect(map["r_vg"]).toBe("region");
    expect(map["m_gbg"]).toBe("municipality");
  });

  it("nycklar på id, inte namn: två koncept med samma label klassas var för sig", () => {
    // Fixturen delar label mellan ett län och en kommun för att DISKRIMINERA:
    // under namn-nyckling delade de en nyckel och en av dem måste förlora. Under
    // id-nyckling finns ingen kollision att lösa, så båda behåller sin egen
    // granularitet och skrivordningen saknar betydelse.
    const map = buildOrtGranularityMap(
      tree([
        {
          conceptId: "r_x",
          label: "Delat namn",
          municipalities: [{ conceptId: "m_x", label: "Delat namn" }],
        },
      ])
    );
    expect(map["r_x"]).toBe("region");
    expect(map["m_x"]).toBe("municipality");
    expect(map["Delat namn"]).toBeUndefined();
  });

  it("null-taxonomi → tom karta (degraderar civilt)", () => {
    expect(buildOrtGranularityMap(null)).toEqual({});
  });
});

describe("classifyOrtConcept", () => {
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
    expect(classifyOrtConcept("m_solna", map)).toBe("municipality");
  });

  it("känt län → 'region'", () => {
    expect(classifyOrtConcept("r_sthlm", map)).toBe("region");
  });

  it("okänt concept-id (stale snapshot) → null", () => {
    expect(classifyOrtConcept("m_okand", map)).toBeNull();
  });

  it("postens LABEL är inte en nyckel", () => {
    // Vakt mot att någon nycklar tillbaka på namn: labeln finns i trädet, men
    // kartan känner bara id:n. Utan den här raden skulle en återgång till
    // namn-nyckling passera hela sviten.
    expect(classifyOrtConcept("Solna", map)).toBeNull();
  });
});
