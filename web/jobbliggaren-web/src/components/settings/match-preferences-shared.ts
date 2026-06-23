import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import type { ResumeListItemDto } from "@/lib/dto/resumes";

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
