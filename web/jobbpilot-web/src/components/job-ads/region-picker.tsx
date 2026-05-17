"use client";

import { useId, useMemo } from "react";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import { MAX_CONCEPT_IDS } from "@/lib/dto/job-ads";
import { TaxonomyChipList } from "./taxonomy-chip-list";

interface RegionPickerProps {
  regions: ReadonlyArray<TaxonomyRegion>;
  // concept-id-lista (ADR 0042 Beslut B-kontrakt OFÖRÄNDRAT — väljaren
  // emitterar fortfarande string[] concept-id till URL/VO).
  values: ReadonlyArray<string>;
  onChange: (next: string[]) => void;
  // Reverse-lookup-namn för redan-valda id (sparad sökning kan bära id som
  // inte längre finns i trädet — då används denna fallback-label).
  resolvedLabels: ReadonlyMap<string, string>;
}

/**
 * ADR 0043 — Län-väljare (enkelnivå, INGEN kommun; Beslut E Variant A).
 * Användaren väljer svenska län-NAMN; concept-id syns aldrig. Vald län
 * visas som namn-chippar. URL-driven server-state ägs av föräldern
 * (JobAdFilters); denna komponent håller ingen egen submit-state.
 *
 * Civic-utility (Platsbanken-regel): rent fält, ingen jargong, ingen
 * "OR-bevakning". Ren native `<select>` (samma mönster som Sortering) —
 * robust tangentbords-/skärmläsar-stöd utan custom combobox-ARIA, label
 * via htmlFor, hint via aria-describedby (a11y §5).
 */
export function RegionPicker({
  regions,
  values,
  onChange,
  resolvedLabels,
}: RegionPickerProps) {
  const selectId = useId();
  const hintId = useId();

  const atCap = values.length >= MAX_CONCEPT_IDS;

  // Namn-uppslag: trädet är primärkälla; reverse-lookup-fallback för id
  // som inte längre finns i snapshoten ("Okänd kod (<id>)" från backend).
  const labelFor = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of regions) map.set(r.conceptId, r.label);
    return (conceptId: string): string =>
      map.get(conceptId) ??
      resolvedLabels.get(conceptId) ??
      `Okänd kod (${conceptId})`;
  }, [regions, resolvedLabels]);

  const selectedSet = useMemo(() => new Set(values), [values]);

  const available = useMemo(
    () => regions.filter((r) => !selectedSet.has(r.conceptId)),
    [regions, selectedSet]
  );

  const chips = useMemo(
    () =>
      values.map((conceptId) => ({
        conceptId,
        label: labelFor(conceptId),
      })),
    [values, labelFor]
  );

  function handleSelect(conceptId: string) {
    if (conceptId === "" || atCap) return;
    if (selectedSet.has(conceptId)) return;
    onChange([...values, conceptId]);
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={selectId}
        className="text-label font-medium text-text-primary"
      >
        Län
      </label>

      <TaxonomyChipList
        items={chips}
        ariaLabel="Valda län"
        onRemove={(conceptId) =>
          onChange(values.filter((v) => v !== conceptId))
        }
      />

      <select
        id={selectId}
        value=""
        disabled={atCap || available.length === 0}
        onChange={(e) => handleSelect(e.target.value)}
        aria-describedby={hintId}
        className="h-11 rounded-md border border-border-default bg-surface-primary px-2.5 text-body text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring disabled:opacity-60"
      >
        {/* Funktionell unselected-display, ej exempeltext (Platsbanken-
            regel-undantaget för select unselected value). */}
        <option value="">Välj län</option>
        {available.map((r) => (
          <option key={r.conceptId} value={r.conceptId}>
            {r.label}
          </option>
        ))}
      </select>

      <p id={hintId} className="text-body-sm text-text-secondary">
        {atCap
          ? `Du har valt ${MAX_CONCEPT_IDS} län (max). Ta bort ett för att lägga till fler.`
          : "Välj ett eller flera län. Annonser från något av de valda länen visas."}
      </p>
    </div>
  );
}
