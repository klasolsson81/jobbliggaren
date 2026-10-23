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
});
