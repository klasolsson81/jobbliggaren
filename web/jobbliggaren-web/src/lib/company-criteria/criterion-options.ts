// Pure data shaping for the criterion pickers: the SCB reference tree in, picker-ready structures out.
// No React, no DOM — so every rule below is unit-testable without rendering anything.
//
// It lives in `lib/` rather than beside the components because three surfaces now read it: the criterion
// dialog (Smarta bevakningar), the bransch popover (/foretag/sok), and their tests. Before #999 the two
// tree builders were inline `useMemo`s in `criterion-dialog.tsx` and a second, differently-shaped
// flattener (`buildBranschOptions`) lived in `foretag-sok-searchbar.tsx` — the same knowledge, "how the
// SCB reference becomes selectable options", written twice with two different answers about which levels
// are selectable. That divergence is exactly what #999 surfaced as "hard to find" (Hunt/Thomas 1999).

import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * One node in a picker tree. `leafCodes` are the WIRE codes this node covers (a leaf carries its own
 * code as the single element), so toggling any node toggles that whole group and the selection state
 * stays a flat Set of leaves at every level. `children` is absent/empty for a leaf.
 *
 * Depth-agnostic on purpose: the same shape carries 3-level SNI (section → division → leaf) and
 * 2-level geography (län → kommun).
 */
export interface CriterionTreeNode {
  readonly code: string;
  readonly name: string;
  readonly leafCodes: ReadonlyArray<string>;
  readonly children?: ReadonlyArray<CriterionTreeNode>;
}

/**
 * A flattened, selectable option — one row of the filter view, or one chip of a selection summary.
 * `depth` is the level it came from (0 = top), kept so the UI can distinguish a division from the leaf
 * of nearly the same name ("Dataprogrammering" exists at both levels in SNI 2025).
 */
export interface CriterionOption {
  /** Stable React key. Level-prefixed so two levels can never collide, whatever the code scheme. */
  readonly key: string;
  readonly code: string;
  readonly name: string;
  readonly depth: number;
  readonly leafCodes: ReadonlyArray<string>;
}

/**
 * Acronyms that must survive sentence-casing. Kept as an explicit list rather than a heuristic: the
 * input domain is 22 strings, so an enumerated exception set is verifiable and a heuristic is not.
 * "O.D." needs no entry — lowercasing already yields the correct "o.d.".
 */
const PRESERVED_ACRONYMS = /(?<!\p{L})tv(?!\p{L})/gu;

/**
 * Sentence-case a name that the source asset shipped in ALL CAPS.
 *
 * SCB writes SNI's 22 **section** names in caps ("VATTENFÖRSÖRJNING; AVLOPPSRENING, AVFALLSHANTERING
 * OCH SANERING") while its 87 divisions and 835 leaves are already sentence-case. DESIGN.md §4 bans
 * all-caps sans, and the rule governs the rendered result rather than its cause — 22 shouting rows are
 * the default view of this control, and they also flatten the hierarchy the panel's own caps label
 * (`.jp-popover__title`) depends on to read as a system signal.
 *
 * PRESENTATION ONLY. `code` and `leafCodes` are untouched, so nothing on the wire changes, and the
 * picker's filter lowercases both sides before comparing — matching behaviour is provably unchanged.
 *
 * The guard is what makes this safe to apply to every node: a string that is not entirely uppercase is
 * returned as-is, so divisions and leaves pass through and the function is idempotent.
 *
 * The semicolon case is not a judgement call — the asset is its own oracle. Section P is "OFFENTLIG
 * FÖRVALTNING OCH FÖRSVAR; OBLIGATORISK SOCIALFÖRSÄKRING" and division 84 is the same string already
 * sentence-cased, so SCB itself publishes the intended result. Same for 49, 71 and 94.
 */
export function toSentenceCase(name: string): string {
  if (name !== name.toLocaleUpperCase("sv-SE")) return name;
  const lowered = name
    .toLocaleLowerCase("sv-SE")
    .replace(PRESERVED_ACRONYMS, "TV");
  return lowered.charAt(0).toLocaleUpperCase("sv-SE") + lowered.slice(1);
}

/** SNI: section → division → leaf. Every level selectable; a parent expands to its leaves. */
export function buildSniNodes(reference: CriterionReference): CriterionTreeNode[] {
  return reference.sni.map((section) => ({
    code: section.code,
    name: toSentenceCase(section.name),
    leafCodes: section.divisions.flatMap((d) => d.leaves.map((l) => l.code)),
    children: section.divisions.map((division) => ({
      code: division.code,
      name: division.name,
      leafCodes: division.leaves.map((l) => l.code),
      children: division.leaves.map((leaf) => ({
        code: leaf.code,
        name: leaf.name,
        leafCodes: [leaf.code],
      })),
    })),
  }));
}

/** Geography: län → kommun. The wire axis is kommun codes only, so a län expands to its kommuner. */
export function buildKommunNodes(reference: CriterionReference): CriterionTreeNode[] {
  return reference.lan.map((lan) => ({
    code: lan.code,
    name: lan.name,
    leafCodes: lan.kommuner.map((k) => k.code),
    children: lan.kommuner.map((kommun) => ({
      code: kommun.code,
      name: kommun.name,
      leafCodes: [kommun.code],
    })),
  }));
}

/**
 * Every node at EVERY level as a flat option list, in tree order.
 *
 * This is the #999 fix for findability. The picker's filter used to match leaves only, so typing "data"
 * surfaced the 5-digit detail codes but never the division or the section that carry the same word —
 * while the typeahead it replaces searched all three levels. Filtering over the flattened tree makes the
 * filter view a view OF the tree rather than a different, narrower catalogue.
 *
 * Nodes with no leaf codes are dropped: they are unselectable, so a row for one would be a control that
 * does nothing.
 */
export function flattenCriterionOptions(
  nodes: ReadonlyArray<CriterionTreeNode>,
  depth = 0,
): CriterionOption[] {
  const out: CriterionOption[] = [];
  for (const node of nodes) {
    if (node.leafCodes.length > 0) {
      out.push({
        key: `${depth}:${node.code}`,
        code: node.code,
        name: node.name,
        depth,
        leafCodes: node.leafCodes,
      });
    }
    if (node.children?.length) {
      out.push(...flattenCriterionOptions(node.children, depth + 1));
    }
  }
  return out;
}

/**
 * The canonical top-down decomposition of a selected leaf set into the FEWEST nodes that describe it:
 * a node whose leaves are all selected becomes one option and its subtree is not descended into;
 * otherwise its children are examined; a leaf is emitted when its own code is selected.
 *
 * This is what makes an arbitrary or shared URL readable. Its predecessor (`seedBranch`) looked for a
 * single option whose leaf set EQUALLED the URL's, and fell back to one generic "Vald bransch" chip
 * otherwise — honest but blunt, and with multi-select the fallback would have fired constantly and
 * hidden what is actually selected. The same upward derivation `groupTriState` performs, run downward.
 *
 * Selected codes that appear nowhere in the tree are ignored here; the server already drops unknown
 * codes against the SCB allowlist (`normalizeCodes`), so the axis and the chips cannot disagree.
 */
export function decomposeSelection(
  nodes: ReadonlyArray<CriterionTreeNode>,
  selected: ReadonlySet<string>,
  depth = 0,
): CriterionOption[] {
  const out: CriterionOption[] = [];
  for (const node of nodes) {
    if (node.leafCodes.length === 0) continue;
    const allSelected = node.leafCodes.every((code) => selected.has(code));
    if (allSelected) {
      out.push({
        key: `${depth}:${node.code}`,
        code: node.code,
        name: node.name,
        depth,
        leafCodes: node.leafCodes,
      });
      continue;
    }
    if (node.children?.length) {
      out.push(...decomposeSelection(node.children, selected, depth + 1));
    }
  }
  return out;
}
