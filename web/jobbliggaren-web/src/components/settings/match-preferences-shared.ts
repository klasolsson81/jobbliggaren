import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import type { ResumeListItemDto } from "@/lib/dto/resumes";
import type { SkillGroup } from "@/lib/dto/skills";

/** Platt taxonomi-val (concept-id → svenskt namn). */
export interface Option {
  readonly conceptId: string;
  readonly label: string;
}

/**
 * Väljer det CV att härleda yrke ur: primärt (`isPrimary === true`), annars
 * det senast uppdaterade. `updatedAt` är ISO-8601 → lexikografisk jämförelse
 * är korrekt sortering (ingen Date-parsning behövs). Ren funktion (testbar).
 * Bor här (inte i `"use server"`-actionfilen, som bara får exportera async).
 */
export function pickPrimaryResume(
  resumes: ReadonlyArray<ResumeListItemDto>
): ResumeListItemDto | null {
  if (resumes.length === 0) return null;
  const primary = resumes.find((r) => r.isPrimary);
  if (primary) return primary;
  return resumes.reduce((latest, r) =>
    r.updatedAt > latest.updatedAt ? r : latest
  );
}

/**
 * Plattar `occupationFields[].occupationGroups[]` till en enkel
 * `{conceptId,label}`-lista. Ren funktion (testbar utan rendering).
 */
export function flattenOccupationGroups(
  fields: ReadonlyArray<TaxonomyOccupationField>
): ReadonlyArray<Option> {
  return fields.flatMap((field) =>
    field.occupationGroups.map((group) => ({
      conceptId: group.conceptId,
      label: group.label,
    }))
  );
}

/**
 * Substring-filtrerar options på label (case-insensitive, locale "sv").
 * Tom/blank query → hela listan. Ren funktion (testbar utan rendering).
 */
export function filterOptions(
  options: ReadonlyArray<Option>,
  query: string
): ReadonlyArray<Option> {
  const q = query.trim().toLocaleLowerCase("sv");
  if (q.length === 0) return options;
  return options.filter((o) => o.label.toLocaleLowerCase("sv").includes(q));
}

/** Lägg till/ta bort ett concept-id ur en vald-lista (immutabelt). */
export function toggle(
  selected: ReadonlyArray<string>,
  conceptId: string
): string[] {
  return selected.includes(conceptId)
    ? selected.filter((v) => v !== conceptId)
    : [...selected, conceptId];
}

/**
 * Slår upp svenska visningsnamn för en lista valda concept-id i samma ordning.
 * Okänt id (stale val mot ny taxonomi) faller tillbaka på id-strängen så
 * chippen aldrig renderas tom. Ren funktion (testbar utan rendering).
 */
export function labelsForSelected(
  selected: ReadonlyArray<string>,
  options: ReadonlyArray<Option>
): ReadonlyArray<Option> {
  const byId = new Map(options.map((o) => [o.conceptId, o.label]));
  return selected.map((conceptId) => ({
    conceptId,
    label: byId.get(conceptId) ?? conceptId,
  }));
}

/**
 * exp-per-occ (ADR 0079-amendment PR-4): en per-yrke-erfarenhets-rad.
 * `years` är `null` när angiven men ospecificerad (skilt från `0` = noll år och
 * från en utelämnad rad = ingen åsikt). Speglar backend/profil-DTO:n.
 */
export interface OccupationExperienceEntry {
  readonly conceptId: string;
  readonly years: number | null;
}

/**
 * exp-per-occ (ADR 0079-amendment PR-4): bygg draft-overlay-mapen ur den
 * persisterade `{conceptId, years}[]`-listan. `years` (inkl. `null`/`0`)
 * bevaras oförändrat. Delas av wizarden + dialogen (DRY). Ren funktion.
 */
export function recordFromOccupationExperience(
  overlay: ReadonlyArray<OccupationExperienceEntry>
): Record<string, number | null> {
  const record: Record<string, number | null> = {};
  for (const entry of overlay) record[entry.conceptId] = entry.years;
  return record;
}

/**
 * exp-per-occ (ADR 0079-amendment PR-4): projicera overlay-mapen till backend-
 * wire-formen (`{conceptId, years}[]`) — ENBART för yrken som fortfarande är
 * valda (subset-regeln; backend avvisar en orphan-rad med 400). Ett yrke utan
 * overlay-nyckel utelämnas helt (≠ en `{years: null}`-rad): "ingen rad" bär
 * ingen åsikt, en `null`-rad bär "angiven men ospecificerad". Ordningen följer
 * den valda yrkes-listan (determinism). Delas av wizarden + dialogen. Ren
 * funktion (testbar utan rendering).
 */
export function projectOccupationExperience(
  overlay: Readonly<Record<string, number | null>>,
  selectedOccupations: ReadonlyArray<string>
): ReadonlyArray<OccupationExperienceEntry> {
  const result: Array<OccupationExperienceEntry> = [];
  for (const conceptId of selectedOccupations) {
    if (conceptId in overlay) {
      // `in`-guarden bevisar nyckeln finns; under noUncheckedIndexedAccess är
      // indexerings-typen ändå `number | null | undefined`, så `?? null`
      // normaliserar bort `undefined` (kan inte inträffa här) utan att kollapsa
      // ett giltigt `0` (`??` triggar bara på null/undefined, aldrig på 0).
      const years = overlay[conceptId] ?? null;
      result.push({ conceptId, years });
    }
  }
  return result;
}

// ── #277 (twin chips) — skill GROUP helpers ──────────────────────────────────
// The unit of selection for skills is now a GROUP (one chip per shared
// exact-label surface), but the persisted/saved set + the PUT payload stay a
// FLAT `string[]` of ALL member ids (grade-inert). These pure helpers map
// between the flat selected set and the group chips, consuming the BE-provided
// `memberConceptIds` VERBATIM (the FE never re-derives membership from raw
// taxonomy). They are used at EVERY chip-render site (skill-section, the wizard
// step-5 review, the dialog/card, and the cold-load) so a saved twin-pair always
// renders as ONE chip. Pure (testable without rendering).

/** A rendered skill chip: the canonical id (chip key + remove target) + label +
 *  the flat member ids the chip stands for (removing the chip removes them all). */
export interface SkillChip {
  readonly conceptId: string;
  readonly label: string;
  readonly memberConceptIds: ReadonlyArray<string>;
}

/**
 * Build a `memberId → group` lookup from known group metadata (search results ∪
 * CV proposals ∪ the cold-load/seed store). Every member id of a group maps to
 * that group, so a flat selected id can be resolved to its chip. When the same
 * member id appears in more than one known group (e.g. a singleton seed later
 * superseded by a real twin-group from search), the LAST one wins — callers pass
 * the richer (search/CV) groups after the seed so the fuller group prevails.
 */
function indexGroupsByMember(
  groups: ReadonlyArray<SkillGroup>
): ReadonlyMap<string, SkillGroup> {
  const byMember = new Map<string, SkillGroup>();
  for (const g of groups) {
    for (const memberId of g.memberConceptIds) byMember.set(memberId, g);
  }
  return byMember;
}

/**
 * Derive ONE chip per group from the FLAT selected set + the known group
 * metadata. Walks `selected` in order; the first time a selected id is seen it
 * emits its group's chip (canonical id + label + member ids) and marks every
 * member emitted, so the twin partner already in `selected` does NOT produce a
 * second chip. A selected id with no known group is its own singleton chip whose
 * label falls back to the id (graceful — same id-fallback as `labelsForSelected`,
 * so a saved id whose group metadata never loaded still renders, never blank).
 *
 * Determinism: chip order follows first-appearance in `selected`.
 */
export function groupsForSelected(
  selected: ReadonlyArray<string>,
  knownGroups: ReadonlyArray<SkillGroup>
): ReadonlyArray<SkillChip> {
  const byMember = indexGroupsByMember(knownGroups);
  const emitted = new Set<string>();
  const chips: SkillChip[] = [];
  for (const id of selected) {
    if (emitted.has(id)) continue;
    const group = byMember.get(id);
    if (group) {
      // Emit the group ONCE; mark only members that are actually selected so a
      // partially-selected group (one twin saved, the other not) still collapses
      // to one chip yet only removes the ids the user actually holds.
      for (const memberId of group.memberConceptIds) {
        if (selected.includes(memberId)) emitted.add(memberId);
      }
      chips.push({
        conceptId: group.conceptId,
        label: group.label,
        memberConceptIds: group.memberConceptIds.filter((m) =>
          selected.includes(m)
        ),
      });
    } else {
      // Unknown id → singleton chip, id-fallback label (never blank).
      emitted.add(id);
      chips.push({ conceptId: id, label: id, memberConceptIds: [id] });
    }
  }
  return chips;
}

/**
 * Add a group to the flat selected set: the UNION of the current selection and
 * ALL the group's member ids (idempotent — re-adding an already-present group is
 * a no-op). Order: existing ids first, then new members in group order.
 */
export function addSkillGroup(
  selected: ReadonlyArray<string>,
  group: SkillGroup
): string[] {
  const next = [...selected];
  const present = new Set(selected);
  for (const memberId of group.memberConceptIds) {
    if (!present.has(memberId)) {
      present.add(memberId);
      next.push(memberId);
    }
  }
  return next;
}

/**
 * Remove a group from the flat selected set: the DIFFERENCE — drops EVERY member
 * id. Removing a twin chip therefore removes BOTH twin ids in one action.
 */
export function removeSkillGroup(
  selected: ReadonlyArray<string>,
  memberConceptIds: ReadonlyArray<string>
): string[] {
  const drop = new Set(memberConceptIds);
  return selected.filter((id) => !drop.has(id));
}

/**
 * "Already added" for a search result group: TRUE only when ALL the group's
 * member ids are in the selected set (a half-selected twin is still addable so
 * the user can complete the pair). Empty member list defends as not-added.
 */
export function isSkillGroupSelected(
  selected: ReadonlyArray<string>,
  group: SkillGroup
): boolean {
  if (group.memberConceptIds.length === 0) return false;
  const present = new Set(selected);
  return group.memberConceptIds.every((id) => present.has(id));
}
