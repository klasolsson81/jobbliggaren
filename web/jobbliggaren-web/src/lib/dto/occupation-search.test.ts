import { describe, expect, it } from "vitest";
import { occupationSearchResultSchema } from "./occupation-search";

// Real v30 labels and ids (taxonomy-snapshot.json + occupation-name-to-ssyk-level-4.v30.json).
const groupRow = {
  kind: "Group",
  groupConceptId: "DJh5_yyF_hEM",
  groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
  matchedNames: [{ occupationNameConceptId: "7wdX_4rv_33z", nameLabel: "Backend-utvecklare" }],
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
    {
      occupationNameConceptId: "z5AM_ayf_WcL",
      nameLabel: "Projektledare, IT",
      groupConceptId: "DJh5_yyF_hEM",
      groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
    },
    {
      occupationNameConceptId: "1jm3_XFQ_TdJ",
      nameLabel: "Projektledare, el",
      groupConceptId: "nDaB_vdy_eAy",
      groupLabel: "Ingenjörer och tekniker inom elektroteknik",
    },
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

  // #2199 PR 3 — the actor: the previous release's API image, which answers a Group row's names as
  // plain strings, met during a rollback's recreate or by a tab opened before the release. This
  // asserts only that the read side refuses it, so the search degrades to unavailable.
  it("refuses a Group row whose names are plain strings", () => {
    const previous = { ...groupRow, matchedNames: ["Backend-utvecklare"] };
    expect(occupationSearchResultSchema.safeParse({ rows: [previous], totalRows: 1 }).success).toBe(false);
  });

  it("refuses a name or a variant without its own occupation-name id", () => {
    const nameWithoutId = { ...groupRow, matchedNames: [{ nameLabel: "Backend-utvecklare" }] };
    const variantWithoutId = {
      ...wordRow,
      variants: wordRow.variants.map(({ occupationNameConceptId: _id, ...rest }) => rest),
    };
    expect(occupationSearchResultSchema.safeParse({ rows: [nameWithoutId], totalRows: 1 }).success).toBe(false);
    expect(occupationSearchResultSchema.safeParse({ rows: [variantWithoutId], totalRows: 1 }).success).toBe(false);
  });
});
