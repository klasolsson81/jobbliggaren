"use client";

import { useId, useMemo, useState } from "react";
import type { TaxonomyOccupationField } from "@/lib/dto/taxonomy";
import { MAX_CONCEPT_IDS } from "@/lib/dto/job-ads";
import { TaxonomyChipList } from "./taxonomy-chip-list";

interface OccupationPickerProps {
  occupationFields: ReadonlyArray<TaxonomyOccupationField>;
  // concept-id-lista (occupation-name-nivå → job_ads.ssyk_concept_id).
  // ADR 0042 Beslut B-kontrakt OFÖRÄNDRAT — emitterar string[] concept-id.
  values: ReadonlyArray<string>;
  onChange: (next: string[]) => void;
  resolvedLabels: ReadonlyMap<string, string>;
}

/**
 * ADR 0043 — Yrkes-väljare, hierarkisk Yrkesområde→Yrke (Beslut E Variant
 * A, tvånivå). Användaren väljer först ett yrkesområde, därefter ett eller
 * flera yrken under det. Vald yrke visas som namn-chippar. concept-id
 * (occupation-name-nivå) emitteras till URL/VO — kontrakt oförändrat.
 *
 * Civic-utility (Platsbanken-regel): rena fält, namn ej kod, ingen
 * jargong, ingen "OR-bevakning". Två rena native `<select>` (robust
 * tangentbord/skärmläsare utan custom combobox-ARIA), labels via htmlFor,
 * hint via aria-describedby (a11y §5). Yrkes-select är inaktiv tills ett
 * yrkesområde valts (progressiv disclosure inom väljaren).
 */
export function OccupationPicker({
  occupationFields,
  values,
  onChange,
  resolvedLabels,
}: OccupationPickerProps) {
  const fieldSelectId = useId();
  const occSelectId = useId();
  const hintId = useId();

  const [activeFieldId, setActiveFieldId] = useState("");

  const atCap = values.length >= MAX_CONCEPT_IDS;

  // Namn-uppslag över ALLA yrken i trädet (oavsett valt yrkesområde) +
  // reverse-lookup-fallback för id som inte längre finns i snapshoten.
  const labelFor = useMemo(() => {
    const map = new Map<string, string>();
    for (const f of occupationFields) {
      for (const o of f.occupations) map.set(o.conceptId, o.label);
    }
    return (conceptId: string): string =>
      map.get(conceptId) ??
      resolvedLabels.get(conceptId) ??
      `Okänd kod (${conceptId})`;
  }, [occupationFields, resolvedLabels]);

  const selectedSet = useMemo(() => new Set(values), [values]);

  const activeField = useMemo(
    () => occupationFields.find((f) => f.conceptId === activeFieldId) ?? null,
    [occupationFields, activeFieldId]
  );

  // Yrken under valt yrkesområde, redan-valda bortfiltrerade.
  const availableOccupations = useMemo(() => {
    if (!activeField) return [];
    return activeField.occupations.filter(
      (o) => !selectedSet.has(o.conceptId)
    );
  }, [activeField, selectedSet]);

  const chips = useMemo(
    () =>
      values.map((conceptId) => ({
        conceptId,
        label: labelFor(conceptId),
      })),
    [values, labelFor]
  );

  function handleSelectOccupation(conceptId: string) {
    if (conceptId === "" || atCap) return;
    if (selectedSet.has(conceptId)) return;
    onChange([...values, conceptId]);
  }

  return (
    <fieldset className="flex min-w-0 flex-col gap-1.5 border-0 p-0">
      {/* <legend> ger gruppen ett tillgängligt namn utan att kollidera med
          input-label-queryn (legend != label). Civic-utility: ren rubrik,
          ingen box-chrome (border-0 p-0). */}
      <legend className="mb-1.5 text-label font-medium text-text-primary">
        Yrke
      </legend>

      <TaxonomyChipList
        items={chips}
        ariaLabel="Valda yrken"
        onRemove={(conceptId) =>
          onChange(values.filter((v) => v !== conceptId))
        }
      />

      <div className="grid gap-2.5 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor={fieldSelectId}
            className="text-body-sm text-text-secondary"
          >
            Yrkesområde
          </label>
          <select
            id={fieldSelectId}
            value={activeFieldId}
            onChange={(e) => setActiveFieldId(e.target.value)}
            className="h-11 rounded-md border border-border-default bg-surface-primary px-2.5 text-body text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
          >
            <option value="">Välj yrkesområde</option>
            {occupationFields.map((f) => (
              <option key={f.conceptId} value={f.conceptId}>
                {f.label}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor={occSelectId}
            className="text-body-sm text-text-secondary"
          >
            Yrke
          </label>
          <select
            id={occSelectId}
            value=""
            disabled={
              !activeField || atCap || availableOccupations.length === 0
            }
            onChange={(e) => handleSelectOccupation(e.target.value)}
            aria-describedby={hintId}
            className="h-11 rounded-md border border-border-default bg-surface-primary px-2.5 text-body text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring disabled:opacity-60"
          >
            <option value="">
              {activeField ? "Välj yrke" : "Välj yrkesområde först"}
            </option>
            {availableOccupations.map((o) => (
              <option key={o.conceptId} value={o.conceptId}>
                {o.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      <p id={hintId} className="text-body-sm text-text-secondary">
        {atCap
          ? `Du har valt ${MAX_CONCEPT_IDS} yrken (max). Ta bort ett för att lägga till fler.`
          : "Välj ett yrkesområde och sedan ett eller flera yrken. Annonser för något av de valda yrkena visas."}
      </p>
    </fieldset>
  );
}
