import { describe, it, expect } from "vitest";
import {
  buildKommunNodes,
  buildSniNodes,
  decomposeSelection,
  flattenCriterionOptions,
  toSentenceCase,
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

/**
 * SNI 2025's 22 section names, verbatim from
 * `src/Jobbliggaren.Infrastructure/CompanyRegister/Reference/sni-2025.v1.json` (`sniVersion` 2025.v1),
 * paired with the sentence-case form that must render.
 *
 * The input domain of this transform is finite and small — 22 strings, and only the sections are
 * ALL CAPS in the asset — so the assertion is EXHAUSTIVE rather than a sample. Anything less would be
 * choosing not to prove something that can be proved. If SCB ships a new SNI version, this list is
 * meant to go red: a normaliser silently meeting new input is how an acronym gets lower-cased.
 */
const SECTION_CASES: ReadonlyArray<readonly [string, string]> = [
  ["JORDBRUK, SKOGSBRUK OCH FISKE", "Jordbruk, skogsbruk och fiske"],
  ["UTVINNING AV MINERAL", "Utvinning av mineral"],
  ["TILLVERKNING", "Tillverkning"],
  ["FÖRSÖRJNING AV EL, GAS, VÄRME OCH KYLA", "Försörjning av el, gas, värme och kyla"],
  [
    "VATTENFÖRSÖRJNING; AVLOPPSRENING, AVFALLSHANTERING OCH SANERING",
    "Vattenförsörjning; avloppsrening, avfallshantering och sanering",
  ],
  ["BYGGVERKSAMHET", "Byggverksamhet"],
  ["HANDEL", "Handel"],
  ["TRANSPORT OCH MAGASINERING", "Transport och magasinering"],
  ["HOTELL- OCH RESTAURANGVERKSAMHET", "Hotell- och restaurangverksamhet"],
  [
    "FÖRLAGSVERKSAMHET, RADIO- OCH TV-SÄNDNING SAMT PRODUKTION OCH DISTRIBUTION AV MEDIEINNEHÅLL",
    "Förlagsverksamhet, radio- och TV-sändning samt produktion och distribution av medieinnehåll",
  ],
  [
    "TELEKOMMUNIKATION, DATAPROGRAMMERING, DATAKONSULTVERKSAMHET, DATAINFRASTRUKTUR OCH ANNAN INFORMATIONSVERKSAMHET",
    "Telekommunikation, dataprogrammering, datakonsultverksamhet, datainfrastruktur och annan informationsverksamhet",
  ],
  ["FINANSIELL VERKSAMHET OCH FÖRSÄKRINGSVERKSAMHET", "Finansiell verksamhet och försäkringsverksamhet"],
  ["FASTIGHETSVERKSAMHET", "Fastighetsverksamhet"],
  [
    "VERKSAMHET INOM JURIDIK, EKONOMI, VETENSKAP OCH TEKNIK",
    "Verksamhet inom juridik, ekonomi, vetenskap och teknik",
  ],
  [
    "UTHYRNING, FASTIGHETSSERVICE, RESETJÄNSTER OCH ANNAN STÖDVERKSAMHET",
    "Uthyrning, fastighetsservice, resetjänster och annan stödverksamhet",
  ],
  [
    "OFFENTLIG FÖRVALTNING OCH FÖRSVAR; OBLIGATORISK SOCIALFÖRSÄKRING",
    "Offentlig förvaltning och försvar; obligatorisk socialförsäkring",
  ],
  ["UTBILDNING", "Utbildning"],
  ["VÅRD OCH OMSORG; SOCIAL VERKSAMHET", "Vård och omsorg; social verksamhet"],
  ["KULTUR, IDROTT OCH FRITID", "Kultur, idrott och fritid"],
  ["ANNAN SERVICEVERKSAMHET", "Annan serviceverksamhet"],
  [
    "FÖRVÄRVSARBETE I HUSHÅLL OCH HUSHÅLLENS PRODUKTION AV DIVERSE VAROR OCH TJÄNSTER FÖR EGET BRUK",
    "Förvärvsarbete i hushåll och hushållens produktion av diverse varor och tjänster för eget bruk",
  ],
  [
    "VERKSAMHET VID INTERNATIONELLA ORGANISATIONER, UTLÄNDSKA AMBASSADER O.D.",
    "Verksamhet vid internationella organisationer, utländska ambassader o.d.",
  ],
];

describe("toSentenceCase — DESIGN.md §4 forbids all-caps sans, and SCB ships the sections in caps", () => {
  it.each(SECTION_CASES)("%s", (input, expected) => {
    expect(toSentenceCase(input)).toBe(expected);
  });

  it("covers every section in the asset", () => {
    expect(SECTION_CASES).toHaveLength(22);
  });

  it("keeps TV an acronym and o.d. an abbreviation", () => {
    // The two cases a naive `.toLowerCase()` gets wrong and right respectively — called out by name so
    // a future edit to the exception set has a test that says what it is for.
    expect(toSentenceCase("RADIO- OCH TV-SÄNDNING")).toContain("TV-sändning");
    expect(toSentenceCase("AMBASSADER O.D.")).toBe("Ambassader o.d.");
  });

  it("leaves anything not entirely uppercase alone, and is idempotent", () => {
    // The guard is what makes it safe to apply to every node: divisions and leaves are already
    // sentence-case in the asset and must not be re-cased.
    expect(toSentenceCase("Dataprogrammering")).toBe("Dataprogrammering");
    expect(toSentenceCase("Handel med egna fastigheter")).toBe("Handel med egna fastigheter");
    const once = toSentenceCase("TILLVERKNING");
    expect(toSentenceCase(once)).toBe(once);
  });

  it("does not touch the wire codes", () => {
    const [section] = buildSniNodes(REFERENCE);
    expect(section!.code).toBe("J");
    expect(section!.leafCodes).toEqual(["62010", "62020", "63110", "63120"]);
  });
});

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
