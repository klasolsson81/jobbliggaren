/**
 * #560 PR-3 — hierarchical leaf-selection for the criterion picker (the SCB equivalent of
 * `job-ads/ort-selection.ts`, but a DIFFERENT shape). The wire contract is LEAVES ONLY: SNI leaf
 * codes and kommun codes. Unlike the JobTech ort picker — where a whole-län pick is stored as ONE län
 * concept-id and is NEVER expanded (an ad may be tagged at län granularity) — the register has no
 * län-level match: a company sits in exactly one kommun. So here BOTH axes are a plain Set of leaf
 * codes, and selecting a parent (an SNI section/division, or a whole län) EXPANDS to all its leaf
 * codes. The parent's checkbox state is DERIVED upward from the leaf set (checked / indeterminate /
 * unchecked); there is no separate parent id in the state.
 *
 * These helpers are pure and hold no reference to the tree — the caller passes the leaf codes a given
 * group covers (built once from the reference tree). Order is not significant (a Set), so callers sort
 * when they build the wire arrays.
 */

export type TriState = "checked" | "indeterminate" | "unchecked";

/** Toggle a single leaf code in the selection (add if absent, remove if present). */
export function toggleLeaf(
  selected: ReadonlySet<string>,
  leafCode: string,
): Set<string> {
  const next = new Set(selected);
  if (next.has(leafCode)) next.delete(leafCode);
  else next.add(leafCode);
  return next;
}

/**
 * Toggle a whole group (a section, a division, or a whole län) by its leaf codes — the standard
 * tri-state parent click:
 * - all of the group's leaves already selected → DESELECT all of them,
 * - otherwise (none or some selected) → EXPAND: select all of them.
 *
 * An empty group (no leaves) is a no-op. Leaves outside the group are never touched, so a section
 * click leaves an unrelated section's selection intact.
 */
export function toggleGroup(
  selected: ReadonlySet<string>,
  groupLeafCodes: ReadonlyArray<string>,
): Set<string> {
  if (groupLeafCodes.length === 0) return new Set(selected);

  const allSelected = groupLeafCodes.every((code) => selected.has(code));
  const next = new Set(selected);
  if (allSelected) {
    for (const code of groupLeafCodes) next.delete(code);
  } else {
    for (const code of groupLeafCodes) next.add(code);
  }
  return next;
}

/**
 * The tri-state of a group derived from the leaf selection:
 * - "checked" — every leaf in the group is selected,
 * - "indeterminate" — some but not all are selected (WAI-ARIA "mixed"),
 * - "unchecked" — none are selected (also the state of an empty group).
 */
export function groupTriState(
  selected: ReadonlySet<string>,
  groupLeafCodes: ReadonlyArray<string>,
): TriState {
  if (groupLeafCodes.length === 0) return "unchecked";

  let selectedCount = 0;
  for (const code of groupLeafCodes) {
    if (selected.has(code)) selectedCount++;
  }
  if (selectedCount === 0) return "unchecked";
  if (selectedCount === groupLeafCodes.length) return "checked";
  return "indeterminate";
}
