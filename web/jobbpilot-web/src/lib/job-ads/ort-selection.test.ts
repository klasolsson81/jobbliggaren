import { describe, it, expect } from "vitest";
import {
  applyMunicipalityChange,
  toggleMunicipalityInRegion,
  toggleWholeRegion,
  clearRegionColumn,
  type OrtSelection,
} from "./ort-selection";

// Fixtur: län X med kommuner x1/x2, län Y med kommun y1.
const regionOf = new Map<string, string>([
  ["x1", "X"],
  ["x2", "X"],
  ["y1", "Y"],
]);

const empty: OrtSelection = { region: [], municipality: [] };

describe("applyMunicipalityChange — per-län-normalisering", () => {
  it("nytillagd kommun i valt län tar bort länets helläns-val", () => {
    const next = applyMunicipalityChange(
      { region: ["X"], municipality: [] },
      ["x1"],
      regionOf,
    );
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1"]);
  });

  it("kommun i ANNAT län rör inte helläns-valet (cross-län-mix är giltig union)", () => {
    const next = applyMunicipalityChange(
      { region: ["X"], municipality: [] },
      ["y1"],
      regionOf,
    );
    expect(next.region).toEqual(["X"]);
    expect(next.municipality).toEqual(["y1"]);
  });

  it("avmarkering av kommun lämnar region-axeln orörd", () => {
    const next = applyMunicipalityChange(
      { region: ["Y"], municipality: ["x1", "x2"] },
      ["x1"],
      regionOf,
    );
    expect(next.region).toEqual(["Y"]);
    expect(next.municipality).toEqual(["x1"]);
  });

  it("okänd kommun (saknas i taxonomin) lämnar region-axeln orörd", () => {
    const next = applyMunicipalityChange(
      { region: ["X"], municipality: [] },
      ["stale_id"],
      regionOf,
    );
    expect(next.region).toEqual(["X"]);
    expect(next.municipality).toEqual(["stale_id"]);
  });

  it("flera nytillagda kommuner släcker varsitt läns helläns-val", () => {
    const next = applyMunicipalityChange(
      { region: ["X", "Y"], municipality: [] },
      ["x1", "y1"],
      regionOf,
    );
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1", "y1"]);
  });
});

describe("toggleMunicipalityInRegion — E2f Platsbanken-semantik", () => {
  const xMunis = ["x1", "x2", "x3"];

  it("hela länet valt + kommun-klick = 'hela länet minus den' (övriga materialiseras)", () => {
    const next = toggleMunicipalityInRegion(
      { region: ["X"], municipality: [] },
      "x2",
      "X",
      xMunis,
    );
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1", "x3"]);
  });

  it("hela länet minus en — andra läns val orörda", () => {
    const next = toggleMunicipalityInRegion(
      { region: ["X", "Y"], municipality: ["y1"] },
      "x1",
      "X",
      xMunis,
    );
    expect(next.region).toEqual(["Y"]);
    expect(next.municipality).toEqual(["y1", "x2", "x3"]);
  });

  it("vald kommun avmarkeras (utan helläns-val)", () => {
    const next = toggleMunicipalityInRegion(
      { region: [], municipality: ["x1", "x2"] },
      "x1",
      "X",
      xMunis,
    );
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x2"]);
  });

  it("ovald kommun markeras (utan helläns-val)", () => {
    const next = toggleMunicipalityInRegion(empty, "x1", "X", xMunis);
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1"]);
  });

  it("markering som kompletterar länets alla kommuner kollapsar till region-id", () => {
    const next = toggleMunicipalityInRegion(
      { region: [], municipality: ["x1", "x2", "y1"] },
      "x3",
      "X",
      xMunis,
    );
    expect(next.region).toEqual(["X"]);
    expect(next.municipality).toEqual(["y1"]);
  });

  it("tom kommun-lista (taxonomi-degradering) kollapsar ALDRIG till region-id", () => {
    // length > 0-vakten (code-reviewer Minor 2a) — markering utan känd
    // kommun-lista får inte felaktigt bli ett helläns-val.
    const next = toggleMunicipalityInRegion(empty, "x1", "X", []);
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1"]);
  });

  it("denormaliserat state (region + egen kommun samtidigt): klicket rensar BÅDA (Minor 1)", () => {
    // Handredigerad URL kan bära region X + x2 samtidigt. Klick på x2 under
    // helläns-valet = "hela länet minus x2" — x2 får inte ligga kvar.
    const next = toggleMunicipalityInRegion(
      { region: ["X"], municipality: ["x2"] },
      "x2",
      "X",
      xMunis,
    );
    expect(next.region).toEqual([]);
    expect(next.municipality).toEqual(["x1", "x3"]);
  });
});

describe("toggleWholeRegion", () => {
  it("PÅ: lägger region-id och rensar länets egna kommun-val", () => {
    const next = toggleWholeRegion(
      { region: [], municipality: ["x1", "y1"] },
      "X",
      ["x1", "x2"],
    );
    expect(next.region).toEqual(["X"]);
    expect(next.municipality).toEqual(["y1"]);
  });

  it("PÅ: materialiserar ALDRIG kommun-ids (ett region-id, en chip)", () => {
    const next = toggleWholeRegion(empty, "X", ["x1", "x2"]);
    expect(next.region).toEqual(["X"]);
    expect(next.municipality).toEqual([]);
  });

  it("AV: tar bara bort region-id:t — andra läns kommun-val orörda", () => {
    const next = toggleWholeRegion(
      { region: ["X", "Y"], municipality: ["y1"] },
      "X",
      ["x1", "x2"],
    );
    expect(next.region).toEqual(["Y"]);
    expect(next.municipality).toEqual(["y1"]);
  });
});

describe("clearRegionColumn", () => {
  it("rensar länets helläns-val OCH dess kommun-val, andra län orörda", () => {
    const next = clearRegionColumn(
      { region: ["X", "Y"], municipality: ["x1", "x2", "y1"] },
      "X",
      ["x1", "x2"],
    );
    expect(next.region).toEqual(["Y"]);
    expect(next.municipality).toEqual(["y1"]);
  });

  it("no-op när länet saknar val", () => {
    const next = clearRegionColumn(
      { region: ["Y"], municipality: ["y1"] },
      "X",
      ["x1", "x2"],
    );
    expect(next.region).toEqual(["Y"]);
    expect(next.municipality).toEqual(["y1"]);
  });
});
