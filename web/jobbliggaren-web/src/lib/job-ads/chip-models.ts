import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import type { JobbUrlState } from "./search-params";

/**
 * Delad chip-modell + borttagnings-helpers (Fas E2h, architect F6 +
 * E2d-CTO F4-mandatet — SPOT). Konsumeras av BÅDE results-toolbarens
 * filter-chips och hero-sökfältets in-field-chips: × i fältet och × i
 * toolbaren är SAMMA state-operation, två renderingar. Label-källorna
 * skiljer (toolbar: server-resolverade labels per ADR 0043; fältet:
 * taxonomy-trädet) → injicerad resolver, inte två deriveringar.
 *
 * q-orden renderas som EN chip PER ORD (architect F1): q har ingen
 * fras-semantik på wire — `websearch_to_tsquery` AND:ar ociterade ord som
 * lexem (ADR 0062). Per-ord-chips är den wire-ärliga renderingen, och ger
 * per-ord-borttagning (Klas-spec: allt blir taggar).
 */

export type DimensionAxis =
  | "occupationGroup"
  | "region"
  | "municipality"
  // Klass 2 (2026-06-13) — anställningsform + omfattning. removeChipFromState
  // är generisk över DimensionAxis (state[chip.axis]) — JobbUrlState bär nu
  // dessa nycklar, så borttagning fungerar utan en egen gren.
  | "employmentType"
  | "worktimeExtent";
export type ChipAxis = DimensionAxis | "q";

export interface SearchChip {
  axis: ChipAxis;
  /** conceptId för dimension-chips; själva ordet för q-chips. */
  value: string;
  label: string;
}

export type ConceptLabelResolver = (
  axis: DimensionAxis,
  conceptId: string,
) => string;

/** q → ord-lista (whitespace-separerad; tomma segment bort). */
export function splitQWords(q: string): string[] {
  return q.split(/\s+/).filter((w) => w.length > 0);
}

/**
 * Bygger chip-listan ur URL-staten (E2g-principen — chips DERIVERAS, ingen
 * egen lista). Ordning: region → municipality → occupationGroup (geografin
 * samlad, E2b-architect-dom) → employmentType → worktimeExtent (Klass 2) →
 * q-ord sist.
 */
export function buildChipModels(
  state: JobbUrlState,
  resolveLabel: ConceptLabelResolver,
  opts?: { includeQ?: boolean },
): SearchChip[] {
  const chips: SearchChip[] = [
    ...state.region.map<SearchChip>((id) => ({
      axis: "region",
      value: id,
      label: resolveLabel("region", id),
    })),
    ...state.municipality.map<SearchChip>((id) => ({
      axis: "municipality",
      value: id,
      label: resolveLabel("municipality", id),
    })),
    ...state.occupationGroup.map<SearchChip>((id) => ({
      axis: "occupationGroup",
      value: id,
      label: resolveLabel("occupationGroup", id),
    })),
    ...state.employmentType.map<SearchChip>((id) => ({
      axis: "employmentType",
      value: id,
      label: resolveLabel("employmentType", id),
    })),
    ...state.worktimeExtent.map<SearchChip>((id) => ({
      axis: "worktimeExtent",
      value: id,
      label: resolveLabel("worktimeExtent", id),
    })),
  ];
  if (opts?.includeQ) {
    // Dedupe case-insensitivt (code-reviewer Minor 1 E2h): en extern URL
    // kan bära dubblett-ord ("?q=volvo Volvo") — utan dedupe blir det
    // duplicerade React-keys. removeChipFromState tar bort ALLA ci-
    // förekomster så chip-× alltid speglar renderingen.
    const seen = new Set<string>();
    for (const word of splitQWords(state.q)) {
      const key = word.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      chips.push({ axis: "q", value: word, label: word });
    }
  }
  return chips;
}

/**
 * Tar bort EN chip ur staten — samma operation oavsett rendering (fält/
 * toolbar). Dimension: filtrera bort conceptId. q: ta bort första
 * case-insensitiva ord-förekomsten och joina om (kanonisk form — backend-
 * parsern kollapsar whitespace ändå).
 */
export function removeChipFromState(
  state: JobbUrlState,
  chip: SearchChip,
): JobbUrlState {
  if (chip.axis === "q") {
    const key = chip.value.toLowerCase();
    const words = splitQWords(state.q).filter(
      (w) => w.toLowerCase() !== key,
    );
    const nextQ = words.join(" ");
    if (nextQ === state.q) return state;
    return { ...state, q: nextQ };
  }
  return {
    ...state,
    [chip.axis]: state[chip.axis].filter((v) => v !== chip.value),
  };
}

/**
 * Label-resolver ur FE-taxonomy-trädet (fält-sidan; toolbaren injicerar
 * sina server-resolverade labels i stället). Okänt id (stale URL/snapshot)
 * → "Okänd kod (<id>)" — samma graceful-fallback-text som ADR 0043
 * Beslut B/toolbaren, aldrig throw.
 */
export function buildTaxonomyLabelResolver(
  taxonomy: TaxonomyTree | null,
): ConceptLabelResolver {
  const map = new Map<string, string>();
  for (const r of taxonomy?.regions ?? []) {
    map.set(r.conceptId, r.label);
    for (const m of r.municipalities) map.set(m.conceptId, m.label);
  }
  for (const f of taxonomy?.occupationFields ?? []) {
    map.set(f.conceptId, f.label);
    for (const g of f.occupationGroups) map.set(g.conceptId, g.label);
  }
  // Klass 2 — platta listor (anställningsform/omfattning). Råa JobTech-labels
  // (Klas "honest 8" — ingen kurering). Fält-sidans resolver; toolbaren
  // injicerar i stället sina server-resolverade labels (/taxonomy/labels är
  // kind-agnostisk sedan PR-1).
  for (const e of taxonomy?.employmentTypes ?? []) map.set(e.conceptId, e.label);
  for (const w of taxonomy?.worktimeExtents ?? []) map.set(w.conceptId, w.label);
  return (_axis, conceptId) =>
    map.get(conceptId) ?? `Okänd kod (${conceptId})`;
}
