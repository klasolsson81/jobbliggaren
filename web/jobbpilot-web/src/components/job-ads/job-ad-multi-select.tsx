"use client";

import { useId, useState } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MAX_CONCEPT_IDS } from "@/lib/dto/job-ads";

const CONCEPT_ID_PATTERN = /^[A-Za-z0-9_-]{1,32}$/;

interface JobAdMultiSelectProps {
  label: string;
  hint: string;
  values: ReadonlyArray<string>;
  onChange: (next: string[]) => void;
}

/**
 * Multi-select för JobTech-taxonomi (ssyk/region), ADR 0042 Beslut B.
 * Ersätter den tidigare concept-id-fritext-inputen med flera värden som
 * chips + avbockning. URL-driven server-state ägs av föräldern (JobAdFilters);
 * denna komponent håller bara en lokal "lägg till"-buffert.
 *
 * Cap MAX_CONCEPT_IDS speglar backend SearchCriteria.MaxConceptIds — visas i
 * copy och blockerar tillägg över taket (ingen krasch). Per-element-format
 * valideras innan tillägg (defense-in-depth; backend är sanningskälla).
 *
 * Civic-utility: chips är text + dismiss-X (regel 3 inga fyllnadselement),
 * ingen färgad bakgrund (neutral surface), inga ikoner som dekoration.
 */
export function JobAdMultiSelect({
  label,
  hint,
  values,
  onChange,
}: JobAdMultiSelectProps) {
  const inputId = useId();
  const hintId = useId();
  const errorId = useId();
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);

  const atCap = values.length >= MAX_CONCEPT_IDS;

  function addValue() {
    const candidate = draft.trim();
    if (candidate === "") return;
    if (!CONCEPT_ID_PATTERN.test(candidate)) {
      setError(
        "Koden måste vara 1–32 tecken (bokstäver, siffror, _ eller -)."
      );
      return;
    }
    if (values.includes(candidate)) {
      setError("Koden är redan tillagd.");
      return;
    }
    if (atCap) {
      setError(`Max ${MAX_CONCEPT_IDS} val per lista.`);
      return;
    }
    onChange([...values, candidate]);
    setDraft("");
    setError(null);
  }

  function removeValue(value: string) {
    onChange(values.filter((v) => v !== value));
    setError(null);
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={inputId}
        className="text-label font-medium text-text-primary"
      >
        {label}
      </label>

      {values.length > 0 && (
        <ul
          className="flex flex-wrap gap-2"
          aria-label={`Valda: ${label}`}
        >
          {values.map((value) => (
            <li key={value}>
              <span className="inline-flex items-center gap-1.5 rounded-md border border-border-default bg-surface-secondary px-2 py-1 font-mono text-body-sm text-text-secondary">
                {value}
                <button
                  type="button"
                  onClick={() => removeValue(value)}
                  className="inline-flex items-center justify-center rounded-sm p-0.5 text-text-secondary hover:text-text-primary"
                  aria-label={`Ta bort ${value}`}
                >
                  <X className="size-3.5" aria-hidden="true" />
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <div className="flex items-start gap-2">
        <Input
          id={inputId}
          type="text"
          value={draft}
          disabled={atCap}
          onChange={(e) => {
            setDraft(e.target.value);
            if (error) setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              // Förhindra form-submit — Enter lägger till värdet i listan.
              e.preventDefault();
              addValue();
            }
          }}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : hintId}
        />
        <Button
          type="button"
          variant="outline"
          onClick={addValue}
          disabled={atCap}
        >
          Lägg till
        </Button>
      </div>

      {error ? (
        <p id={errorId} role="alert" className="text-body-sm text-danger-700">
          {error}
        </p>
      ) : (
        <p id={hintId} className="text-body-sm text-text-secondary">
          {atCap ? `Max ${MAX_CONCEPT_IDS} val tillagda. Ta bort ett för att lägga till fler.` : hint}
        </p>
      )}
    </div>
  );
}
