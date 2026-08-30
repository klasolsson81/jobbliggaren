/**
 * Locale copy for the taxonomy concepts the wire carries as a CODE rather than a name.
 *
 * Two kinds of taxonomy value reach the frontend, and the difference is not cosmetic:
 *
 * - **Register data** — municipality, region and occupation-group names. Proper nouns, so
 *   they stay Swedish in every locale (#1430).
 * - **Coded terms** — the klass 2 set: employment type and worktime extent. Common nouns
 *   (`Heltid`, `Vikariat`, `Behovsanställning`), so they translate. `klass2-taxonomy.json`
 *   says so of itself: relabelling them is "an FE presentation concern (PR-2), not a
 *   data-layer concern (ACL stays honest)". This module is that concern.
 *
 * Keyed on `conceptId`, flat and axis-agnostic. Three of the four emit points hand over a
 * naked concept id with no axis attached — the recent-search label's `Coded` part, the match
 * detail's `employmentFit`, and `buildTaxonomyLabelResolver`'s mixed map — so an
 * axis-split key set would force them to guess an axis or try both (CTO 2026-08-28).
 *
 * The English wording is authored, not sourced: JobTech publishes no English labels for these
 * concepts (measured against `taxonomy.api.jobtechdev.se/v1/taxonomy/main/concepts?type=
 * employment-type`, 2026-08-28). Where Arbetsförmedlingen's own English pages name a form,
 * that wording is used rather than a fresh invention — read 2026-08-28 at
 * `arbetsformedlingen.se/other-languages/english-engelska/working-in-sweden/forms-of-
 * employment`: permanent employment, trial employment, **limited-time employment**,
 * substitute position, seasonal work, summer jobs, full time, part time. `Limited-time`
 * rather than the EU directive's `fixed-term` is that page's own rendering of
 * `tidsbegränsad anställning`, and it is a deliberate choice of the civic register over the
 * HR convention (design-reviewer Minor, 2026-08-28). `Full-time`/`Part-time` are hyphenated
 * against that page, because as LABELS they sit beside `On-call employment` and
 * `Limited-time employment`; the page's own use is adverbial.
 *
 * The id set is FROZEN and hand-curated. `coded-taxonomy.test.ts` reads
 * `src/Jobbliggaren.Infrastructure/Taxonomy/klass2-taxonomy.json` directly and fails on any
 * disagreement between it, this union, and either catalogue — including a Swedish value that
 * is no longer byte-identical to the source label, which is what makes the "honest 8"
 * constraint mechanical rather than conventional.
 */

/**
 * Every coded-taxonomy concept id, in the source file's own order — the eight employment
 * types, then the two worktime extents. The ids are opaque JobTech identifiers, so the
 * Swedish name is given beside each one; without it the union is a wall of nonce strings and
 * a reviewer cannot tell a typo from a value.
 */
export const CODED_TAXONOMY_IDS = [
  "PFZr_Syz_cUq", // Vanlig anställning
  "kpPX_CNN_gDU", // Tillsvidareanställning (inkl. eventuell provanställning)
  "1paU_aCR_nGn", // Behovsanställning
  "sTu5_NBQ_udq", // Tidsbegränsad anställning
  "Jh8f_q9J_pbJ", // Sommarjobb / feriejobb
  "EBhX_Qm2_8eX", // Säsongsanställning
  "gro4_cWF_6D7", // Vikariat
  "9Wuo_2Yb_36E", // Arbete utomlands
  "6YE1_gAC_R2G", // Heltid
  "947z_JGS_Uk2", // Deltid
] as const;

export type CodedTaxonomyId = (typeof CODED_TAXONOMY_IDS)[number];

/**
 * The catalogue keys this module reads, under the `jobads.enums` namespace — the same block
 * `applicationStatusLabel` reads its enum copy from, and for the same reason: a backend
 * identifier resolving to locale copy is what that block already holds.
 *
 * A template-literal union over the frozen id list rather than `string`, exactly as
 * `applicationStatusLabel` types its translator: under `strictFunctionTypes` a `t` that
 * accepts only real keys is not assignable to one that accepts any string, so the narrow
 * type is what actually type-checks.
 */
export type CodedTaxonomyKey = `codedTaxonomy.${CodedTaxonomyId}`;

function isCodedTaxonomyId(id: string): id is CodedTaxonomyId {
  return (CODED_TAXONOMY_IDS as readonly string[]).includes(id);
}

/**
 * Resolves one taxonomy concept id to its locale name.
 *
 * Accepts any concept id, not only a coded one, so a single call site can serve a mixed list
 * — `buildTaxonomyLabelResolver` maps region, municipality, occupation-group and coded ids
 * through one resolver, and the id sets are disjoint. Anything outside the coded set falls
 * to `fallback`, which is what keeps register data Swedish in every locale.
 *
 * `fallback` is also the drift answer, and the caller chooses it because the two channels
 * have different ones: where the wire still carries the resolved name, it is that name
 * (honest Swedish beats a blank); where the wire carries only the code, it is the locale's
 * unknown-code copy. Either way an id this file does not know renders something a reader can
 * act on rather than an empty string or a thrown missing-key error (ADR 0043).
 */
export function codedTaxonomyName(
  t: (key: CodedTaxonomyKey) => string,
  conceptId: string,
  fallback: string,
): string {
  return isCodedTaxonomyId(conceptId) ? t(`codedTaxonomy.${conceptId}`) : fallback;
}

/** A taxonomy option as the picker surfaces carry it. */
export interface CodedTaxonomyOption {
  readonly conceptId: string;
  readonly label: string;
}

/**
 * Names every option in the reader's locale AND orders them by the name it just gave them.
 *
 * The backend orders klass 2 by the Swedish label's ordinal. Before this module, name and
 * order came out of the same language — the wrong one for an English reader, but a list that
 * scanned. Translating the names alone would have separated them, leaving `en` with no order
 * at all: not alphabetical, not semantic, not frequency (design-reviewer Major, 2026-08-28).
 * Klas decided 2026-08-28 that the order follows the name shown.
 *
 * Measured no-op in Swedish: for exactly these ten strings `Intl.Collator("sv")` yields the
 * same sequence as the backend's `StringComparer.Ordinal`, so no Swedish rendering moves.
 * That equivalence is asserted in `coded-taxonomy.test.ts`, not assumed.
 */
export function codedTaxonomyOptions(
  t: (key: CodedTaxonomyKey) => string,
  collator: Intl.Collator,
  options: readonly CodedTaxonomyOption[],
): CodedTaxonomyOption[] {
  return options
    .map((o) => ({
      conceptId: o.conceptId,
      label: codedTaxonomyName(t, o.conceptId, o.label),
    }))
    .sort((a, b) => collator.compare(a.label, b.label));
}
