"use client";

import { useEffect, useRef } from "react";
import type { ReactNode } from "react";

/**
 * Segment — radiogroup-stil "tab-pill" som primärt används för 2–3 alternativ
 * (Tema, Språk i Inställningar). Civic-utility-stil per HANDOVER §5.1/§5.2.
 *
 * Aktiv option får navy-800-fyll + vit text i båda lägena (matchar primary-
 * knappens "aldrig inverterad"-regel). Disabled-option har opacity 0.55 +
 * cursor not-allowed och triggar ingen state-ändring vid klick eller tangent.
 *
 * Tangentbord (W3C ARIA Authoring Practices, radiogroup-pattern):
 *  - Tab/Shift+Tab navigerar in/ut av gruppen (fokus landar på aktiv option)
 *  - Vänster/Höger (och Upp/Ned) växlar value till nästa enabled option
 *  - Disabled options hoppas över i piltangent-navigation
 *  - Mellanslag/Enter på fokuserad option = klick (default button-beteende)
 */

export interface SegmentOption<T extends string> {
  value: T;
  label: string;
  icon?: ReactNode;
  disabled?: boolean;
}

interface SegmentProps<T extends string> {
  value: T;
  onChange: (value: T) => void;
  options: ReadonlyArray<SegmentOption<T>>;
  /** Tillgänglighets-label för hela gruppen. Krävs (radiogroup-kontrakt). */
  "aria-label": string;
  /** Sätt true för att stänga av alla optioner (t.ex. under spar-pending). */
  disabled?: boolean;
}

export function Segment<T extends string>({
  value,
  onChange,
  options,
  "aria-label": ariaLabel,
  disabled = false,
}: SegmentProps<T>) {
  const groupRef = useRef<HTMLDivElement>(null);

  // Piltangent-nav: hitta nästa enabled option och flytta fokus + value.
  function handleKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight" && e.key !== "ArrowUp" && e.key !== "ArrowDown") {
      return;
    }
    e.preventDefault();
    const enabled = options
      .map((o, i) => ({ o, i }))
      .filter(({ o }) => !o.disabled && !disabled);
    if (enabled.length === 0) return;
    const currentIdx = enabled.findIndex(({ o }) => o.value === value);
    const dir = e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 1;
    const nextIdx = (currentIdx + dir + enabled.length) % enabled.length;
    const next = enabled[nextIdx];
    if (next) onChange(next.o.value);
  }

  // När value ändras: flytta fokus till den nu-aktiva knappen så piltangenter
  // fortsätter inom gruppen utan att tappa fokus.
  useEffect(() => {
    if (!groupRef.current) return;
    const active = groupRef.current.querySelector<HTMLButtonElement>(
      `button[aria-checked="true"]`,
    );
    // Endast om gruppen redan har fokus — annars stjäl vi inte fokus från
    // andra komponenter på sidan vid första render.
    if (
      active &&
      groupRef.current.contains(document.activeElement) &&
      document.activeElement !== active
    ) {
      active.focus();
    }
  }, [value]);

  return (
    <div
      ref={groupRef}
      role="radiogroup"
      aria-label={ariaLabel}
      onKeyDown={handleKeyDown}
      className="jp-segment"
    >
      {options.map((opt) => {
        const isActive = opt.value === value;
        const isDisabled = disabled || opt.disabled === true;
        return (
          <button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={isActive}
            aria-disabled={isDisabled || undefined}
            disabled={isDisabled}
            tabIndex={isActive ? 0 : -1}
            className="jp-segment__opt"
            data-active={isActive}
            onClick={() => {
              if (!isDisabled) onChange(opt.value);
            }}
          >
            {opt.icon && (
              <span className="jp-segment__icon" aria-hidden="true">
                {opt.icon}
              </span>
            )}
            <span>{opt.label}</span>
          </button>
        );
      })}
    </div>
  );
}
