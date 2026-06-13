import { describe, it, expect } from "vitest";
import {
  buildChipModels,
  buildTaxonomyLabelResolver,
  removeChipFromState,
  splitQWords,
} from "./chip-models";
import type { JobbUrlState } from "./search-params";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const taxonomy: TaxonomyTree = {
  // ADR 0043-amendment 2026-06-13 (Klass 2) — anställningsform + omfattning.
  employmentTypes: [{ conceptId: "et_vikariat", label: "Vikariat" }],
  worktimeExtents: [{ conceptId: "wt_heltid", label: "Heltid" }],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [{ conceptId: "zHxw_uJZ_NNh", label: "Solna" }],
    },
  ],
  occupationFields: [
    {
      conceptId: "apaJ_2ja_LuF",
      label: "Data/IT",
      occupationGroups: [
        { conceptId: "MVqp_eS8_kDZ", label: "Systemutvecklare" },
      ],
    },
  ],
};

const state: JobbUrlState = {
  q: "volvo lastbil",
  occupationGroup: ["MVqp_eS8_kDZ"],
  region: ["CifL_Rzy_Mku"],
  municipality: ["zHxw_uJZ_NNh"],
  employmentType: ["et_vikariat"],
  worktimeExtent: ["wt_heltid"],
  sortBy: "PublishedAtDesc",
};

const resolve = buildTaxonomyLabelResolver(taxonomy);

describe("buildChipModels (E2h SPOT)", () => {
  it("ordning: region → municipality → occupationGroup → employmentType → worktimeExtent → q-ord", () => {
    const chips = buildChipModels(state, resolve, { includeQ: true });
    expect(chips.map((c) => c.label)).toEqual([
      "Stockholms län",
      "Solna",
      "Systemutvecklare",
      "Vikariat",
      "Heltid",
      "volvo",
      "lastbil",
    ]);
  });

  it("includeQ utelämnad → bara dimension-chips (toolbar-fallet)", () => {
    const chips = buildChipModels(state, resolve);
    expect(chips.map((c) => c.axis)).toEqual([
      "region",
      "municipality",
      "occupationGroup",
      "employmentType",
      "worktimeExtent",
    ]);
  });

  it("Klass 2 — anställningsform/omfattning-chips bär rätt axel + label", () => {
    const chips = buildChipModels(state, resolve);
    const et = chips.find((c) => c.axis === "employmentType");
    const wt = chips.find((c) => c.axis === "worktimeExtent");
    expect(et).toEqual({
      axis: "employmentType",
      value: "et_vikariat",
      label: "Vikariat",
    });
    expect(wt).toEqual({
      axis: "worktimeExtent",
      value: "wt_heltid",
      label: "Heltid",
    });
  });

  it("okänt conceptId → 'Okänd kod' (graceful, ADR 0043)", () => {
    const chips = buildChipModels(
      { ...state, region: ["STALE_id_123"] },
      resolve,
    );
    expect(chips[0]!.label).toBe("Okänd kod (STALE_id_123)");
  });
});

describe("removeChipFromState (E2h SPOT — fält-× = toolbar-×)", () => {
  it("dimension-chip: filtrerar bort conceptId ur rätt axel", () => {
    const next = removeChipFromState(state, {
      axis: "municipality",
      value: "zHxw_uJZ_NNh",
      label: "Solna",
    });
    expect(next.municipality).toEqual([]);
    expect(next.region).toEqual(["CifL_Rzy_Mku"]);
    expect(next.q).toBe("volvo lastbil");
  });

  it("Klass 2 — employmentType-chip filtreras bort ur rätt axel (generisk väg)", () => {
    const next = removeChipFromState(state, {
      axis: "employmentType",
      value: "et_vikariat",
      label: "Vikariat",
    });
    expect(next.employmentType).toEqual([]);
    expect(next.worktimeExtent).toEqual(["wt_heltid"]);
  });

  it("Klass 2 — worktimeExtent-chip filtreras bort ur rätt axel", () => {
    const next = removeChipFromState(state, {
      axis: "worktimeExtent",
      value: "wt_heltid",
      label: "Heltid",
    });
    expect(next.worktimeExtent).toEqual([]);
    expect(next.employmentType).toEqual(["et_vikariat"]);
  });

  it("q-chip: tar bort ordet (case-insensitivt) och joinar om", () => {
    const next = removeChipFromState(state, {
      axis: "q",
      value: "VOLVO",
      label: "VOLVO",
    });
    expect(next.q).toBe("lastbil");
  });

  it("q-chip för ord som inte finns → oförändrad state", () => {
    const next = removeChipFromState(state, {
      axis: "q",
      value: "saknas",
      label: "saknas",
    });
    expect(next).toBe(state);
  });
});

describe("buildTaxonomyLabelResolver Klass 2", () => {
  it("resolverar employmentTypes + worktimeExtents ur trädet", () => {
    expect(resolve("employmentType", "et_vikariat")).toBe("Vikariat");
    expect(resolve("worktimeExtent", "wt_heltid")).toBe("Heltid");
  });

  it("okänt Klass-2-id → 'Okänd kod' (graceful, ADR 0043)", () => {
    expect(resolve("employmentType", "STALE")).toBe("Okänd kod (STALE)");
  });
});

describe("splitQWords", () => {
  it("whitespace-splittar och slänger tomma segment", () => {
    expect(splitQWords("  volvo   lastbil ")).toEqual(["volvo", "lastbil"]);
    expect(splitQWords("")).toEqual([]);
  });
});
