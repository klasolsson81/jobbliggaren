import {
  occupationSearchResultSchema,
  type OccupationSearchGroupRow,
  type OccupationSearchResult,
  type OccupationSearchWordRow,
} from "@/lib/dto/occupation-search";
import dumped from "./occupation-search-answers.json";

/**
 * #2198 — occupation search answers built from real v30 labels and ids
 * (`src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json` and
 * `…/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json`). The row SHAPES
 * are the backend factories' (`OccupationSearchRow.ForGroup` / `ForWord`); which rows a given word
 * returns, and in what order, is the server's and is not asserted from here.
 */

// #2199 PR 3 — every name the rows below carry, with its real occupation-name id from the same
// sources (and `src/Jobbliggaren.Infrastructure/Taxonomy/occupation-name-lineage.json`). A label
// not in this table throws, so a fixture can never carry an id production does not emit.
const NAME_ID: Readonly<Record<string, { readonly id: string; readonly groupId: string }>> = {
  "Projektledare, IT": { id: "z5AM_ayf_WcL", groupId: "DJh5_yyF_hEM" },
  "Projektledare, el": { id: "1jm3_XFQ_TdJ", groupId: "nDaB_vdy_eAy" },
  "Projektledare, telekom": { id: "T55e_efV_Eqh", groupId: "nDaB_vdy_eAy" },
  "Projektledare, bygg och anläggning": { id: "pTTz_5DC_itW", groupId: "thZP_oR7_WrY" },
  "Backend-utvecklare": { id: "7wdX_4rv_33z", groupId: "DJh5_yyF_hEM" },
  "UX-designer": { id: "iwJU_gW4_SxT", groupId: "UxT1_tPF_Kbg" },
};

export function nameId(nameLabel: string, groupId?: string): string {
  const entry = NAME_ID[nameLabel];
  if (entry === undefined) throw new Error(`No real occupation-name id for "${nameLabel}" in the fixture table`);
  if (groupId !== undefined && entry.groupId !== groupId)
    throw new Error(`"${nameLabel}" is in group ${entry.groupId}, not ${groupId}`);
  return entry.id;
}

export const IT_GROUP = { id: "DJh5_yyF_hEM", label: "Mjukvaru- och systemutvecklare m.fl." } as const;
export const EL_GROUP = { id: "nDaB_vdy_eAy", label: "Ingenjörer och tekniker inom elektroteknik" } as const;
export const BYGG_GROUP = { id: "thZP_oR7_WrY", label: "Ingenjörer och tekniker inom bygg och anläggning" } as const;
export const UX_GROUP = { id: "UxT1_tPF_Kbg", label: "Övriga IT-specialister" } as const;
export const USK_VARD_GROUP = {
  id: "LP4Z_ABV_gRm",
  label: "Undersköterskor, vård- och specialavdelning och mottagning",
} as const;

export function groupRow(
  group: { readonly id: string; readonly label: string },
  matchedNames: ReadonlyArray<string>,
  options: { readonly labelHit?: boolean; readonly exactMatch?: boolean } = {},
): OccupationSearchGroupRow {
  return {
    kind: "Group",
    groupConceptId: group.id,
    groupLabel: group.label,
    matchedNames: matchedNames.map((nameLabel) => ({ occupationNameConceptId: nameId(nameLabel, group.id), nameLabel })),
    labelHit: options.labelHit ?? false,
    headWord: null,
    variants: [],
    exactMatch: options.exactMatch ?? false,
  };
}

export function wordRow(
  headWord: string,
  variants: ReadonlyArray<readonly [string, { readonly id: string; readonly label: string }]>,
): OccupationSearchWordRow {
  return {
    kind: "Word",
    groupConceptId: null,
    groupLabel: null,
    matchedNames: [],
    labelHit: false,
    headWord,
    variants: variants.map(([nameLabel, group]) => ({
      occupationNameConceptId: nameId(nameLabel, group.id),
      nameLabel,
      groupConceptId: group.id,
      groupLabel: group.label,
    })),
    exactMatch: false,
  };
}

/** "Projektledare": four of its names, in three groups; el and telekom share one. */
export const projektledareRow = wordRow("Projektledare", [
  ["Projektledare, IT", IT_GROUP],
  ["Projektledare, el", EL_GROUP],
  ["Projektledare, telekom", EL_GROUP],
  ["Projektledare, bygg och anläggning", BYGG_GROUP],
]);

// OccupationSearchRule's answers over v30, verbatim.
export const ruleAnswer = {
  chef: occupationSearchResultSchema.parse(dumped.chef),
  elingenjor: occupationSearchResultSchema.parse(dumped["elingenjör"]),
  gravor: occupationSearchResultSchema.parse(dumped["gravör"]),
  it: occupationSearchResultSchema.parse(dumped.it),
  sjukskoterska: occupationSearchResultSchema.parse(dumped["sjuksköterska"]),
  underskoterska: occupationSearchResultSchema.parse(dumped["undersköterska"]),
  ux: occupationSearchResultSchema.parse(dumped.ux),
  vuxenutbildning: occupationSearchResultSchema.parse(dumped.vuxenutbildning),
} as const;

export function answer(
  rows: OccupationSearchResult["rows"],
  totalRows: number = rows.length,
): OccupationSearchResult {
  return { rows, totalRows };
}
