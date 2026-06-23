import { describe, it, expect } from "vitest";
import {
  applyClaimsDelta,
  buildLabelIndex,
  EMPTY_CLAIMS,
  getTokenRange,
  isTextRepresentable,
  parseSearchText,
  serializeSearchText,
  updateTextForStateChange,
} from "./tokenize";
import { buildTaxonomyLabelResolver } from "./chip-models";
import type { JobbUrlState } from "./search-params";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";

const taxonomy: TaxonomyTree = {
  // ADR 0043-amendment 2026-06-13 (Klass 2) — required; not exercised here.
  employmentTypes: [],
  worktimeExtents: [],
  regions: [
    {
      conceptId: "CifL_Rzy_Mku",
      label: "Stockholms län",
      municipalities: [
        { conceptId: "AvNB_uwa_6n6", label: "Stockholm" },
        { conceptId: "zHxw_uJZ_NNh", label: "Solna" },
        { conceptId: "UPPL_vas_001", label: "Upplands Väsby" },
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
        // Ambiguitets-fixture: yrkesgrupp som delar label med kommun.
        { conceptId: "AMBI_xxx_yyy", label: "Solna" },
      ],
    },
  ],
};

const index = buildLabelIndex(taxonomy);
const resolve = buildTaxonomyLabelResolver(taxonomy);

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  sortBy: "PublishedAtDesc",
};

describe("parseSearchText (E2i C′)", () => {
  it("exakt unik label → dimension-anspråk; omatchat ord → q-ord", () => {
    const r = parseSearchText("göteborg volvo", index, null);
    expect(r.matches.map((m) => m.conceptId)).toEqual(["PVZL_BQT_XtL"]);
    expect(r.qWords).toEqual(["volvo"]);
  });

  it("greedy longest-match: flerords-label matchar ('Stockholms län')", () => {
    const r = parseSearchText("stockholms län volvo", index, null);
    expect(r.matches.map((m) => m.label)).toEqual(["Stockholms län"]);
    expect(r.qWords).toEqual(["volvo"]);
  });

  it("'Upplands Väsby' handskrivet matchar (VAL 2-bonusen)", () => {
    const r = parseSearchText("upplands väsby", index, null);
    expect(r.matches.map((m) => m.conceptId)).toEqual(["UPPL_vas_001"]);
    expect(r.qWords).toEqual([]);
  });

  it("ambiguitet (label på flera noder) → fritext, aldrig gissning", () => {
    const r = parseSearchText("solna", index, null);
    expect(r.matches).toEqual([]);
    expect(r.qWords).toEqual(["solna"]);
  });

  it("komma bryter n-gram: 'stockholms, län' matchar INTE flerords-labeln", () => {
    const r = parseSearchText("stockholms, län", index, null);
    expect(r.matches).toEqual([]);
    expect(r.qWords).toEqual(["stockholms", "län"]);
  });

  it("caret-ordet exkluderas och bryter n-gram-grannskap", () => {
    // Caret mitt i "göteb" (position 4) → ordet pågående, parsas ej.
    const r = parseSearchText("volvo göteb", index, 9);
    expect(r.qWords).toEqual(["volvo"]);
    expect(r.matches).toEqual([]);
  });

  it("caret efter avslutande mellanslag → allt parsas", () => {
    const r = parseSearchText("göteborg ", index, 9);
    expect(r.matches.map((m) => m.conceptId)).toEqual(["PVZL_BQT_XtL"]);
  });

  it("ledande +/- strippas (NOT-neutralisering — minus är Klas-pending)", () => {
    const r = parseSearchText("+göteborg -deltid", index, null);
    expect(r.matches.map((m) => m.conceptId)).toEqual(["PVZL_BQT_XtL"]);
    expect(r.qWords).toEqual(["deltid"]);
  });

  it("dubbla avgränsare/tomma tokens skippas; q-ord ci-dedupe:as", () => {
    const r = parseSearchText("volvo,,  VOLVO  saab", index, null);
    expect(r.qWords).toEqual(["volvo", "saab"]);
  });
});

describe("getTokenRange", () => {
  it("hittar ordet under caret", () => {
    expect(getTokenRange("volvo göteb", 9)).toEqual({ start: 6, end: 11 });
  });
  it("null när caret står i avgränsar-rymd", () => {
    expect(getTokenRange("volvo  saab", 6)).toBeNull();
  });
});

describe("applyClaimsDelta (C′ regel 1 — delta, aldrig replace)", () => {
  it("nya anspråk adderas via composeSuggestionChip (per-län-norm ärvs)", () => {
    const claims = parseSearchText("göteborg volvo", index, null);
    const r = applyClaimsDelta(empty, EMPTY_CLAIMS, claims, taxonomy);
    expect(r.next.municipality).toEqual(["PVZL_BQT_XtL"]);
    expect(r.next.q).toBe("volvo");
    expect(r.addedLabels).toEqual(["Göteborg", "volvo"]);
  });

  it("borttagna anspråk släpps — men POPOVER-valda dimensioner rörs inte (I1)", () => {
    const state: JobbUrlState = {
      ...empty,
      municipality: ["PVZL_BQT_XtL"],
      region: ["CifL_Rzy_Mku"], // popover-vald — texten gör inget anspråk
      q: "volvo",
    };
    const prev = parseSearchText("göteborg volvo", index, null);
    const next = parseSearchText("volvo", index, null);
    const r = applyClaimsDelta(state, prev, next, taxonomy);
    expect(r.next.municipality).toEqual([]);
    expect(r.next.region).toEqual(["CifL_Rzy_Mku"]); // orörd
    expect(r.next.q).toBe("volvo");
    expect(r.removedLabels).toEqual(["Göteborg"]);
  });

  it("OccupationField-anspråk materialiserar barn vid add och släpper dem vid remove", () => {
    const claims = parseSearchText("Data/IT", index, null);
    const added = applyClaimsDelta(empty, EMPTY_CLAIMS, claims, taxonomy);
    expect(added.next.occupationGroup).toEqual([
      "MVqp_eS8_kDZ",
      "Q5DF_juj_8do",
      "AMBI_xxx_yyy",
    ]);
    const removed = applyClaimsDelta(
      added.next,
      claims,
      EMPTY_CLAIMS,
      taxonomy,
    );
    expect(removed.next.occupationGroup).toEqual([]);
  });

  it("I1-enforcement: region + egen kommun i texten → BÅDA i staten (normaliseringen viker, CTO BESLUT 2)", () => {
    const claims = parseSearchText(
      "västra götalands län göteborg",
      index,
      null,
    );
    const r = applyClaimsDelta(empty, EMPTY_CLAIMS, claims, taxonomy);
    // Per-län-normaliseringen (kosmetik) får inte släcka text-claimat
    // helläns-val — unionen tål redundans.
    expect(r.next.region).toContain("oDpK_oQy_3Zc");
    expect(r.next.municipality).toContain("PVZL_BQT_XtL");
  });

  it("I1-enforcement: field-removal släpper INTE text-claimat barn (CTO BESLUT 2)", () => {
    const both = parseSearchText("Data/IT Systemutvecklare", index, null);
    const added = applyClaimsDelta(empty, EMPTY_CLAIMS, both, taxonomy);
    // Radera "Data/IT" ur texten — Systemutvecklare claimas fortfarande.
    const remaining = parseSearchText("Systemutvecklare", index, null);
    const r = applyClaimsDelta(added.next, both, remaining, taxonomy);
    expect(r.next.occupationGroup).toContain("MVqp_eS8_kDZ");
    expect(r.next.occupationGroup).not.toContain("Q5DF_juj_8do");
  });

  it("q-max-guard: ord som spränger taket vägras (rejectedQ) och ingår EJ i appliedClaims", () => {
    const state = { ...empty, q: "a".repeat(95) };
    const claims = parseSearchText("jättelångtord", index, null);
    const r = applyClaimsDelta(state, EMPTY_CLAIMS, claims, taxonomy);
    expect(r.rejectedQ).toEqual(["jättelångtord"]);
    expect(r.next.q).toBe("a".repeat(95));
    expect(r.appliedClaims.qWords).toEqual([]);
  });
});

describe("serializeSearchText + rundtripps-teoremet (C′ regel 4)", () => {
  it("kanonisk ordning region → kommun → yrkesgrupp → q-ord", () => {
    const state: JobbUrlState = {
      ...empty,
      region: ["oDpK_oQy_3Zc"],
      municipality: ["AvNB_uwa_6n6"],
      occupationGroup: ["MVqp_eS8_kDZ"],
      q: "volvo",
    };
    expect(serializeSearchText(state, resolve, index)).toBe(
      "Västra Götalands län Stockholm Systemutvecklare volvo",
    );
  });

  it("ambiguös label serialiseras INTE (lever enbart i filter-raden)", () => {
    const state: JobbUrlState = {
      ...empty,
      municipality: ["zHxw_uJZ_NNh"], // "Solna" — ambiguös mot yrkesgruppen
    };
    expect(serializeSearchText(state, resolve, index)).toBe("");
  });

  it("q-ord som unikt matchar en label serialiseras INTE (vore re-claim)", () => {
    const state = { ...empty, q: "göteborg volvo" };
    expect(serializeSearchText(state, resolve, index)).toBe("volvo");
  });

  it("rundtripps-teoremet: parse(serialize(s)) ⊆ s för representativa states", () => {
    const states: JobbUrlState[] = [
      { ...empty, region: ["CifL_Rzy_Mku"], q: "volvo lastbil" },
      { ...empty, municipality: ["PVZL_BQT_XtL", "UPPL_vas_001"] },
      {
        ...empty,
        occupationGroup: ["MVqp_eS8_kDZ", "Q5DF_juj_8do"],
        q: "remote",
      },
      { ...empty, region: ["CifL_Rzy_Mku", "oDpK_oQy_3Zc"] },
      { ...empty, municipality: ["zHxw_uJZ_NNh"], q: "solna göteborg" },
    ];
    for (const s of states) {
      const text = serializeSearchText(s, resolve, index);
      const parsed = parseSearchText(text, index, null);
      for (const m of parsed.matches) {
        const axis =
          m.kind === "Region"
            ? s.region
            : m.kind === "Municipality"
              ? s.municipality
              : s.occupationGroup;
        expect(axis).toContain(m.conceptId);
      }
      const qWords = s.q.toLowerCase().split(/\s+/).filter(Boolean);
      for (const w of parsed.qWords)
        expect(qWords).toContain(w.toLowerCase());
    }
  });
});

describe("isTextRepresentable", () => {
  it("unik label är representabel; ambiguös är det inte", () => {
    expect(
      isTextRepresentable(
        "Göteborg",
        { kind: "Municipality", conceptId: "PVZL_BQT_XtL" },
        index,
      ),
    ).toBe(true);
    expect(
      isTextRepresentable(
        "Solna",
        { kind: "Municipality", conceptId: "zHxw_uJZ_NNh" },
        index,
      ),
    ).toBe(false);
  });

  it("label med komma är inte representabel", () => {
    expect(
      isTextRepresentable(
        "Berg, gruv och bygg",
        { kind: "OccupationGroup", conceptId: "X" },
        index,
      ),
    ).toBe(false);
  });
});

describe("updateTextForStateChange (C′ regel 2/3)", () => {
  it("ren borttagning → kirurgisk text-edit som bevarar ord-ordningen", () => {
    const prev: JobbUrlState = {
      ...empty,
      municipality: ["PVZL_BQT_XtL"],
      q: "volvo lastbil",
    };
    const next: JobbUrlState = { ...empty, q: "volvo lastbil" };
    expect(
      updateTextForStateChange(
        "volvo göteborg lastbil",
        prev,
        next,
        resolve,
        index,
      ),
    ).toBe("volvo lastbil");
  });

  it("borttagning av q-ord plockar ordet ur texten", () => {
    const prev: JobbUrlState = { ...empty, q: "volvo lastbil" };
    const next: JobbUrlState = { ...empty, q: "lastbil" };
    expect(
      updateTextForStateChange("volvo lastbil", prev, next, resolve, index),
    ).toBe("lastbil");
  });

  it("tillägg/extern navigering → full kanonisk serialize", () => {
    const prev = empty;
    const next: JobbUrlState = {
      ...empty,
      region: ["CifL_Rzy_Mku"],
      q: "sjuksköterska",
    };
    expect(
      updateTextForStateChange("gammal text", prev, next, resolve, index),
    ).toBe("Stockholms län sjuksköterska");
  });

  it("Rensa allt → tom text", () => {
    const prev: JobbUrlState = {
      ...empty,
      municipality: ["PVZL_BQT_XtL"],
      q: "volvo",
    };
    expect(
      updateTextForStateChange("göteborg volvo", prev, empty, resolve, index),
    ).toBe("");
  });
});
