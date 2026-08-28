import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect } from "vitest";
import svJobads from "../../../messages/sv/jobads.json";
import enJobads from "../../../messages/en/jobads.json";
import {
  CODED_TAXONOMY_IDS,
  codedTaxonomyName,
  codedTaxonomyOptions,
  type CodedTaxonomyKey,
} from "./coded-taxonomy";

/**
 * The coded-taxonomy id set lives in three homes: the backend's `klass2-taxonomy.json`,
 * both message catalogues, and `coded-taxonomy.ts`. `jobads-parity.test.ts` already holds
 * the two catalogues to each other. This file holds all three to the backend source.
 *
 * It reads across the frontend boundary deliberately, and it is the only frontend test that
 * does. The alternative was measuring the two catalogues against a hand-copied list, which
 * is the same drift one indirection later: `codedTaxonomyName` falls back rather than
 * throwing, so an id the catalogue has no key for renders Swedish to an English user and
 * reports nothing. Silent is the failure mode worth a boundary crossing (CTO 2026-08-28).
 *
 * The byte-identity assertion is the load-bearing one. It makes the "honest 8" constraint
 * MECHANICAL instead of conventional — the Swedish rendering cannot drift from the source
 * label without a red test — and it is what proves this change moved no Swedish output.
 */

const KLASS2_SOURCE = join(
  dirname(fileURLToPath(import.meta.url)),
  "../../../../../src/Jobbliggaren.Infrastructure/Taxonomy/klass2-taxonomy.json",
);

interface Klass2Option {
  readonly conceptId: string;
  readonly label: string;
}

function readSourceOptions(): Klass2Option[] {
  const parsed: unknown = JSON.parse(readFileSync(KLASS2_SOURCE, "utf8"));
  if (parsed === null || typeof parsed !== "object") {
    throw new Error("klass2-taxonomy.json is not an object");
  }
  const file = parsed as Record<string, unknown>;
  const out: Klass2Option[] = [];
  for (const branch of ["employmentTypes", "worktimeExtents"]) {
    const values = file[branch];
    if (!Array.isArray(values)) throw new Error(`missing source branch: ${branch}`);
    for (const value of values) {
      if (value === null || typeof value !== "object") {
        throw new Error(`non-object option in ${branch}`);
      }
      const { conceptId, label } = value as Record<string, unknown>;
      if (typeof conceptId !== "string" || typeof label !== "string") {
        throw new Error(`malformed option in ${branch}`);
      }
      out.push({ conceptId, label });
    }
  }
  return out;
}

/** The `enums.codedTaxonomy` branch as a plain record, so a runtime concept id can index it. */
function catalogueBranch(catalogue: unknown): Record<string, string> {
  const enums = (catalogue as { enums?: Record<string, unknown> }).enums;
  const value = enums?.codedTaxonomy;
  if (value === null || typeof value !== "object") {
    throw new Error("missing catalogue branch: enums.codedTaxonomy");
  }
  const out: Record<string, string> = {};
  for (const [key, v] of Object.entries(value as Record<string, unknown>)) {
    if (typeof v !== "string") throw new Error(`non-string catalogue value: ${key}`);
    out[key] = v;
  }
  return out;
}

/** Reads a catalogue the way a next-intl caller does, so the test exercises real keys. */
function translatorFor(catalogue: unknown): (key: CodedTaxonomyKey) => string {
  const branch = catalogueBranch(catalogue);
  return (key) => {
    const value = branch[key.slice("codedTaxonomy.".length)];
    if (value === undefined) throw new Error(`missing catalogue key: ${key}`);
    return value;
  };
}

const source = readSourceOptions();
const sv = catalogueBranch(svJobads);
const en = catalogueBranch(enJobads);

describe("coded-taxonomy — source, union and catalogues agree", () => {
  it("the TypeScript union is exactly the backend's concept-id set", () => {
    expect([...CODED_TAXONOMY_IDS].sort()).toEqual(source.map((o) => o.conceptId).sort());
  });

  it("every source concept id has a key in both catalogues, and neither has an orphan", () => {
    const expected = source.map((o) => o.conceptId).sort();
    expect(Object.keys(sv).sort()).toEqual(expected);
    expect(Object.keys(en).sort()).toEqual(expected);
  });

  it("every Swedish value is byte-identical to the source label", () => {
    // The "honest 8" constraint, mechanised: no omission, no grouping, and no Swedish
    // relabelling of what the ACL emits. Also the proof that `sv` rendering did not move.
    for (const option of source) {
      expect(sv[option.conceptId]).toBe(option.label);
    }
  });

  it("no English value is still the Swedish one", () => {
    // A half-done translation is what this catches: the key exists in `en`, the parity test
    // passes, and the English user still reads Swedish. Every one of these is a common noun
    // with a real English form, so identity here means untranslated.
    for (const option of source) {
      expect(en[option.conceptId]).not.toBe("");
      expect(en[option.conceptId]).not.toBe(option.label);
    }
  });
});

describe("codedTaxonomyOptions", () => {
  const tSv = translatorFor(svJobads);
  const tEn = translatorFor(enJobads);
  const svOrder = new Intl.Collator("sv");
  const enOrder = new Intl.Collator("en");
  // The order the backend ships: klass 2 sorted by the SWEDISH label, Ordinal.
  const asShipped = [...source].sort((a, b) => (a.label < b.label ? -1 : 1));

  it("is a no-op in Swedish — the shipped order survives byte for byte", () => {
    // The property Klas's decision rests on: reordering by the displayed name moves
    // nothing under `sv`, because the displayed name IS the label the backend sorted by.
    expect(codedTaxonomyOptions(tSv, svOrder, asShipped).map((o) => o.label)).toEqual(
      asShipped.map((o) => o.label),
    );
  });

  it("orders English by the English name, not by the Swedish one", () => {
    const names = codedTaxonomyOptions(tEn, enOrder, asShipped).map((o) => o.label);
    expect(names).toEqual([...names].sort((a, b) => enOrder.compare(a, b)));
    // Negative, and this is the half that fails a reversion: shipped order put the
    // English name for `Deltid` ahead of the one for `Heltid`.
    expect(names.indexOf("Full-time")).toBeLessThan(names.indexOf("Part-time"));
  });

  it("leaves a concept id outside the coded set on its source label", () => {
    const mixed = [
      { conceptId: "PVZL_BQT_XtL", label: "Göteborg" },
      { conceptId: "6YE1_gAC_R2G", label: "Heltid" },
    ];
    expect(codedTaxonomyOptions(tEn, enOrder, mixed).map((o) => o.label)).toEqual([
      "Full-time",
      "Göteborg",
    ]);
  });
});

describe("codedTaxonomyName", () => {
  const tSv = translatorFor(svJobads);
  const tEn = translatorFor(enJobads);

  it("resolves through the catalogue rather than echoing the fallback", () => {
    expect(codedTaxonomyName(tEn, "gro4_cWF_6D7", "Vikariat")).toBe("Substitute position");
    expect(codedTaxonomyName(tEn, "6YE1_gAC_R2G", "Heltid")).toBe("Full-time");
  });

  it("renders Swedish byte-identically to the label the backend ships", () => {
    for (const option of source) {
      expect(codedTaxonomyName(tSv, option.conceptId, option.label)).toBe(option.label);
    }
  });

  it("passes a concept id outside the coded set through to the fallback", () => {
    // Register data — a municipality here — stays Swedish in every locale (#1430), and
    // reaches this resolver because `buildTaxonomyLabelResolver` maps one mixed list.
    expect(codedTaxonomyName(tEn, "PVZL_BQT_XtL", "Göteborg")).toBe("Göteborg");
  });

});
