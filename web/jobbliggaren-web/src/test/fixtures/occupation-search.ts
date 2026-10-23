import type {
  OccupationSearchGroupRow,
  OccupationSearchResult,
  OccupationSearchWordRow,
} from "@/lib/dto/occupation-search";

/**
 * #2198 — occupation search answers built from real v30 labels and ids
 * (`src/Jobbliggaren.Infrastructure/Taxonomy/taxonomy-snapshot.json` and
 * `…/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json`). The row SHAPES
 * are the backend factories' (`OccupationSearchRow.ForGroup` / `ForWord`); which rows a given word
 * returns, and in what order, is the server's and is not asserted from here.
 */

export const IT_GROUP = { id: "DJh5_yyF_hEM", label: "Mjukvaru- och systemutvecklare m.fl." } as const;
export const EL_GROUP = { id: "nDaB_vdy_eAy", label: "Ingenjörer och tekniker inom elektroteknik" } as const;
export const BYGG_GROUP = { id: "thZP_oR7_WrY", label: "Ingenjörer och tekniker inom bygg och anläggning" } as const;
export const UX_GROUP = { id: "UxT1_tPF_Kbg", label: "Övriga IT-specialister" } as const;
export const MASKINBEFAL_GROUP = { id: "Rsoy_sS6_6c4", label: "Maskinbefäl" } as const;
export const USK_HEMTJANST_GROUP = {
  id: "jY19_knH_MJp",
  label: "Undersköterskor, hemtjänst, hemsjukvård, äldreboende och habilitering",
} as const;
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
    matchedNames: [...matchedNames],
    labelHit: options.labelHit ?? false,
    headWord: null,
    variants: [],
    exactMatch: options.exactMatch ?? false,
  };
}

export function wordRow(
  headWord: string,
  variants: ReadonlyArray<readonly [string, { readonly id: string; readonly label: string }]>,
  exactMatch = false,
): OccupationSearchWordRow {
  return {
    kind: "Word",
    groupConceptId: null,
    groupLabel: null,
    matchedNames: [],
    labelHit: false,
    headWord,
    variants: variants.map(([nameLabel, group]) => ({
      nameLabel,
      groupConceptId: group.id,
      groupLabel: group.label,
    })),
    exactMatch,
  };
}

/** "Projektledare": four of its names, in three groups; el and telekom share one. */
export const projektledareRow = wordRow("Projektledare", [
  ["Projektledare, IT", IT_GROUP],
  ["Projektledare, el", EL_GROUP],
  ["Projektledare, telekom", EL_GROUP],
  ["Projektledare, bygg och anläggning", BYGG_GROUP],
]);

/** "Elingenjör": a name that lands only inside a Word row, so its row holds the exact word. */
export const elingenjorRow = wordRow(
  "Elingenjör",
  [
    ["Elingenjör", EL_GROUP],
    ["Elingenjör, fartyg/Fartygseltekniker", MASKINBEFAL_GROUP],
  ],
  true,
);

export const underskoterskaWordRow = wordRow("Undersköterska", [
  ["Undersköterska, hemtjänst, äldreboende och habilitering", USK_HEMTJANST_GROUP],
  ["Undersköterska, vård- och specialavdelning och mottagning", USK_VARD_GROUP],
]);

export function answer(
  rows: OccupationSearchResult["rows"],
  totalRows: number = rows.length,
): OccupationSearchResult {
  return { rows, totalRows };
}
