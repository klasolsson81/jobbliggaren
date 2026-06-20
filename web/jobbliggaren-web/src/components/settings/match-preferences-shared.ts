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
