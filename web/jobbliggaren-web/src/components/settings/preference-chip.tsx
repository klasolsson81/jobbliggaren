"use client";

// "use client": chippen bär en interaktiv borttagnings-knapp med onClick +
// onKeyDown (Delete/Backspace) och tar emot en ref för fokus-flytt efter
// borttagning. Inget av detta går att göra i en Server Component.

import { forwardRef } from "react";
import { X } from "lucide-react";

interface PreferenceChipProps {
  /** Visningsnamnet (svenskt taxonomi-label). */
  readonly label: string;
  /** Ta bort detta val. */
  readonly onRemove: () => void;
  /**
   * Tangent-borttagning (Delete/Backspace) på den fokuserade ⨯-knappen. Ägaren
   * sköter fokus-flytt till grannen efteråt; chippen rapporterar bara intentet.
   */
  readonly onRemoveKey?: () => void;
}

/**
 * En borttagbar preferens-chip (.jp-chip--removable) — modern-civic RE-BIND
 * 2026-06-20 §1.4. Innehållsnamn (mixed-case 13px), outline, pill-radie. Den
 * enda interaktiva delen är ⨯-knappen (ett riktigt `<button>` med aria-label);
 * själva chip-texten är inte fokuserbar (ingen tvetydig "vad gör klick på
 * chippen"). Ref vidarebefordras till ⨯-knappen så ägaren kan flytta fokus
 * till grannen efter tangent-borttagning (WCAG 2.4.3).
 */
export const PreferenceChip = forwardRef<HTMLButtonElement, PreferenceChipProps>(
  function PreferenceChip({ label, onRemove, onRemoveKey }, ref) {
    return (
      <span className="jp-chip jp-chip--removable">
        <span className="jp-chip__label" title={label}>
          {label}
        </span>
        <button
          ref={ref}
          type="button"
          className="jp-chip__remove"
          aria-label={`Ta bort ${label}`}
          onClick={onRemove}
          onKeyDown={(e) => {
            if (e.key === "Delete" || e.key === "Backspace") {
              e.preventDefault();
              (onRemoveKey ?? onRemove)();
            }
          }}
        >
          <X size={14} aria-hidden="true" />
        </button>
      </span>
    );
  }
);
