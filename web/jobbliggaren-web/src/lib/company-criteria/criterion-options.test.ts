import { describe, it, expect } from "vitest";
import {
  buildKommunNodes,
  buildSniNodes,
  decomposeSelection,
  flattenCriterionOptions,
} from "./criterion-options";
import type { CriterionReference } from "@/lib/dto/company-criteria";

// Structurally real, deliberately small: one section, two divisions, two leaves each — enough for a
// division's leaf set to be a strict subset of the section's, which is what every rule here turns on.
const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2026",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet",
          leaves: [
            { code: "62010", name: "Datakonsultverksamhet" },
            { code: "62020", name: "Systemutveckling" },
          ],
        },
        {
          code: "63",
          name: "Informationstjänster",
          leaves: [
            { code: "63110", name: "Databehandling och hosting" },
            { code: "63120", name: "Webbportaler" },
          ],
        },
      ],
    },
  ],
  lan: [
    {
      code: "01",
      name: "Stockholms län",
      kommuner: [
        { code: "0180", name: "Stockholm" },
        { code: "0181", name: "Södertälje" },
      ],
    },
  ],
};

const SNI = buildSniNodes(REFERENCE);

describe("buildSniNodes", () => {
  it("carries all three levels, each expanding to the leaves below it", () => {
    expect(SNI).toHaveLength(1);
    const [section] = SNI;
    expect(section!.leafCodes).toEqual(["62010", "62020", "63110", "63120"]);
    expect(section!.children).toHaveLength(2);
    expect(section!.children![0]!.leafCodes).toEqual(["62010", "62020"]);
    // A leaf carries its own code as the single element, so every level toggles the same way.
    expect(section!.children![0]!.children![0]!.leafCodes).toEqual(["62010"]);
  });
});

describe("buildKommunNodes", () => {
  it("is two levels, with a län expanding to its kommun codes", () => {
    const [lan] = buildKommunNodes(REFERENCE);
    expect(lan!.leafCodes).toEqual(["0180", "0181"]);
    expect(lan!.children!.map((k) => k.code)).toEqual(["0180", "0181"]);
  });
});

describe("flattenCriterionOptions", () => {
  it("emits EVERY level in tree order, tagged with its depth", () => {
    const options = flattenCriterionOptions(SNI);
    expect(options.map((o) => [o.depth, o.name])).toEqual([
      [0, "Informations- och kommunikationsverksamhet"],
      [1, "Dataprogrammering, datakonsultverksamhet"],
      [2, "Datakonsultverksamhet"],
      [2, "Systemutveckling"],
      [1, "Informationstjänster"],
      [2, "Databehandling och hosting"],
      [2, "Webbportaler"],
    ]);
  });

  it("keys are unique across levels", () => {
    const keys = flattenCriterionOptions(SNI).map((o) => o.key);
    expect(new Set(keys).size).toBe(keys.length);
  });

  it("drops a node that expands to nothing — an unselectable row is a control that does nothing", () => {
    const empty = flattenCriterionOptions([
      { code: "X", name: "Tom avdelning", leafCodes: [] },
    ]);
    expect(empty).toEqual([]);
  });
});

describe("decomposeSelection", () => {
  it("collapses a fully selected division into ONE division node", () => {
    const chips = decomposeSelection(SNI, new Set(["62010", "62020"]));
    expect(chips.map((c) => c.name)).toEqual([
      "Dataprogrammering, datakonsultverksamhet",
    ]);
  });

  it("collapses to the SECTION when every leaf under it is selected", () => {
    const chips = decomposeSelection(
      SNI,
      new Set(["62010", "62020", "63110", "63120"]),
    );
    expect(chips.map((c) => c.name)).toEqual([
      "Informations- och kommunikationsverksamhet",
    ]);
  });

  it("descends only where the node is partial, mixing levels in one result", () => {
    // All of 62, one leaf of 63 → the division 62 plus that single leaf, never the section.
    const chips = decomposeSelection(SNI, new Set(["62010", "62020", "63110"]));
    expect(chips.map((c) => c.name)).toEqual([
      "Dataprogrammering, datakonsultverksamhet",
      "Databehandling och hosting",
    ]);
  });

  it("emits nothing for an empty selection", () => {
    expect(decomposeSelection(SNI, new Set())).toEqual([]);
  });

  it("ignores codes that are not in the tree", () => {
    // The server drops unknown codes against the SCB allowlist before this ever renders
    // (`normalizeCodes` in page.tsx), so this is the belt to that braces: a code the tree does not
    // know contributes no chip rather than a phantom one.
    expect(decomposeSelection(SNI, new Set(["99999"]))).toEqual([]);
  });

  it("round-trips: the emitted nodes' leaf codes reconstruct the selection exactly", () => {
    const selection = new Set(["62010", "62020", "63120"]);
    const chips = decomposeSelection(SNI, selection);
    const reconstructed = new Set(chips.flatMap((c) => c.leafCodes));
    expect(reconstructed).toEqual(selection);
  });
});
