"use client";

import { useId } from "react";
import type { ReactNode } from "react";

/**
 * ToggleRow — label vänster, on/off-switch höger. Klick på hela raden togglar
 * (W3C-pattern: button som rätt-storlek-target).
 *
 * Switch-mönster (utan extern dep, civic-utility-stil per §5.1/§5.2): native
 * `<button role="switch" aria-checked>` med visuell track + thumb. Inga
 * `<input type="checkbox">` (visuell stil + a11y-paritet blir komplicerad).
 *
 * Acessibility: switch-knappen har aria-labelledby som pekar mot label-id så
 * screen readers läser "Veckosammanfattning via e-post, on/off" korrekt.
 */

interface ToggleRowProps {
  label: string;
  /** Optional sekundärtext under label (hint/description). */
  description?: ReactNode;
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
}

export function ToggleRow({
  label,
  description,
  checked,
  onChange,
  disabled = false,
}: ToggleRowProps) {
  const labelId = useId();
  const descriptionId = useId();
  return (
    <div className="jp-togglerow">
      <div className="jp-togglerow__text">
        <span id={labelId} className="jp-togglerow__label">
          {label}
        </span>
        {description && (
          <span id={descriptionId} className="jp-togglerow__desc">
            {description}
          </span>
        )}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={labelId}
        aria-describedby={description ? descriptionId : undefined}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className="jp-togglerow__switch"
        data-checked={checked}
      >
        <span className="jp-togglerow__thumb" aria-hidden="true" />
      </button>
    </div>
  );
}
