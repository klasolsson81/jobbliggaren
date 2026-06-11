import { describe, it, expect } from "vitest";
import {
  applyMunicipalityChange,
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
