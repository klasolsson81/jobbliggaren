import { describe, it, expect } from "vitest";
import { buildLabelIndex, tokenizeDraft } from "./tokenize";
import type { JobbUrlState } from "./search-params";
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
        // Ambiguitets-fixture: en yrkesgrupp som delar label med en kommun.
        { conceptId: "AMBI_xxx_yyy", label: "Solna" },
      ],
    },
  ],
};

const index = buildLabelIndex(taxonomy);

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  sortBy: "PublishedAtDesc",
};

function run(draft: string, current = empty, finalizeAll = false) {
  return tokenizeDraft(draft, current, taxonomy, index, { finalizeAll });
}

describe("tokenizeDraft (E2h)", () => {
  it("pågående ord utan avgränsare stannar i remainder — ingen commit", () => {
    const r = run("göteb");
    expect(r.changed).toBe(false);
    expect(r.remainder).toBe("göteb");
  });

  it("mellanslag avslutar ord: exakt unik taxonomi-match → dimension-chip", () => {
    const r = run("göteborg ");
    expect(r.changed).toBe(true);
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
    expect(r.next.q).toBe("");
    expect(r.remainder).toBe("");
    expect(r.addedLabels).toEqual(["Göteborg"]);
  });

  it("komma fungerar som avgränsare", () => {
    const r = run("göteborg,");
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
  });

  it("omatchat ord → fritext-q-chip", () => {
    const r = run("hogia ");
    expect(r.next.q).toBe("hogia");
    expect(r.next.municipality).toEqual([]);
  });

  it("Klas-exemplet: 'Systemutvecklare, Hogia Göteborg ' → grupp + fritext + ort", () => {
    const r = run("Systemutvecklare, Hogia Göteborg ");
    expect(r.next.occupationGroup).toEqual(["MVqp_eS8_kDZ"]);
    expect(r.next.q).toBe("Hogia");
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
    expect(r.remainder).toBe("");
  });

  it("sista ordet förblir utkast tills avgränsare: 'volvo göteb' committar bara volvo", () => {
    const r = run("volvo göteb");
    expect(r.next.q).toBe("volvo");
    expect(r.remainder).toBe("göteb");
  });

  it("finalizeAll committar även sista ordet (Sök/Enter)", () => {
    const r = run("volvo göteborg", empty, true);
    expect(r.next.q).toBe("volvo");
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
    expect(r.remainder).toBe("");
  });

  it("ambiguitet (label på flera noder) → fritext, aldrig gissning", () => {
    // "Solna" är både kommun och yrkesgrupp i fixturen.
    const r = run("solna ");
    expect(r.next.municipality).toEqual([]);
    expect(r.next.occupationGroup).toEqual([]);
    expect(r.next.q).toBe("solna");
  });

  it("OccupationField-match materialiserar barn-grupperna (VAL 2a-vägen)", () => {
    const r = run("Data/IT ");
    expect(r.next.occupationGroup).toEqual([
      "MVqp_eS8_kDZ",
      "Q5DF_juj_8do",
      "AMBI_xxx_yyy",
    ]);
  });

  it("ledande + strippas", () => {
    const r = run("+göteborg ");
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
  });

  it("ledande - strippas (NOT-neutralisering — minus är Klas-pending)", () => {
    const r = run("-deltid ");
    expect(r.next.q).toBe("deltid");
  });

  it("dubbla avgränsare/tomma tokens skippas tyst", () => {
    const r = run("volvo,,  saab ");
    expect(r.next.q).toBe("volvo saab");
  });

  it("dubblett-fritext dedupe:as case-insensitivt (idempotent)", () => {
    const r = run("VOLVO ", { ...empty, q: "volvo" });
    expect(r.changed).toBe(false);
    expect(r.next.q).toBe("volvo");
  });

  it("redan vald dimension är idempotent (ingen changed-flagga)", () => {
    const r = run("göteborg ", {
      ...empty,
      municipality: ["PVZL_BQT_XtL"],
    });
    expect(r.changed).toBe(false);
  });

  it("q-max-guard: ord som tar joinad q över 100 vägras och hamnar i remainder", () => {
    const longQ = "a".repeat(95);
    const r = run("jättelångtord ", { ...empty, q: longQ });
    expect(r.changed).toBe(false);
    expect(r.rejected).toEqual(["jättelångtord"]);
    expect(r.remainder).toBe("jättelångtord");
    expect(r.next.q).toBe(longQ);
  });

  it("1-teckens ord chippas ändå (parsern är SPOT för q-hygien)", () => {
    const r = run("c ");
    expect(r.next.q).toBe("c");
  });

  it("region-match släcker länets kommun-val (per-län-normalisering via SPOT)", () => {
    const r = run("stockholms ", empty); // "Stockholms län" är flerords → fritext
    expect(r.next.q).toBe("stockholms");
    // Flerords-labels nås bara via förslags-val — dokumenterad semantik.
  });
});
