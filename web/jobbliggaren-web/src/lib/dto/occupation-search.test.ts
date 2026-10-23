import { describe, expect, it } from "vitest";
import { occupationSearchResultSchema } from "./occupation-search";

// Real v30 labels and ids (taxonomy-snapshot.json + occupation-name-to-ssyk-level-4.v30.json).
const groupRow = {
  kind: "Group",
  groupConceptId: "DJh5_yyF_hEM",
  groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
  matchedNames: ["Backend-utvecklare"],
  labelHit: false,
  headWord: null,
  variants: [],
  exactMatch: false,
};
const wordRow = {
  kind: "Word",
  groupConceptId: null,
  groupLabel: null,
  matchedNames: [],
  labelHit: false,
  headWord: "Projektledare",
  variants: [
    { nameLabel: "Projektledare, IT", groupConceptId: "DJh5_yyF_hEM", groupLabel: "Mjukvaru- och systemutvecklare m.fl." },
    { nameLabel: "Projektledare, el", groupConceptId: "nDaB_vdy_eAy", groupLabel: "Ingenjörer och tekniker inom elektroteknik" },
  ],
  exactMatch: false,
};

describe("occupationSearchResultSchema (#2198)", () => {
  it("reads both row kinds as the backend writes them, every key present", () => {
    const parsed = occupationSearchResultSchema.parse({ rows: [wordRow, groupRow], totalRows: 2 });

    expect(parsed.rows.map((r) => r.kind)).toEqual(["Word", "Group"]);
    expect(parsed.rows[0]).toEqual(wordRow);
    expect(parsed.rows[1]).toEqual(groupRow);
  });

  it.each([
    ["a group row with a head word", { ...groupRow, headWord: "Backend" }],
    ["a group row with variants", { ...groupRow, variants: wordRow.variants }],
    ["a group row with its null keys left out", { kind: "Group", groupConceptId: "DJh5_yyF_hEM", groupLabel: "x", matchedNames: [], labelHit: true, exactMatch: false }],
    ["a word row with a group id", { ...wordRow, groupConceptId: "DJh5_yyF_hEM" }],
    ["a word row with matched names", { ...wordRow, matchedNames: ["Projektledare, IT"] }],
    ["a word row with one variant", { ...wordRow, variants: wordRow.variants.slice(0, 1) }],
    ["an unknown kind", { ...groupRow, kind: "Name" }],
  ])("refuses %s", (_name, row) => {
    expect(occupationSearchResultSchema.safeParse({ rows: [row], totalRows: 1 }).success).toBe(false);
  });

  it("refuses more rows than the searcher's cap and a negative or fractional total", () => {
    const fifty = Array.from({ length: 50 }, () => groupRow);
    expect(occupationSearchResultSchema.safeParse({ rows: fifty, totalRows: 60 }).success).toBe(true);
    expect(occupationSearchResultSchema.safeParse({ rows: [...fifty, groupRow], totalRows: 60 }).success).toBe(false);
    expect(occupationSearchResultSchema.safeParse({ rows: [], totalRows: -1 }).success).toBe(false);
    expect(occupationSearchResultSchema.safeParse({ rows: [], totalRows: 1.5 }).success).toBe(false);
  });
});
