"use client";

import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";

interface ApplicationsControlsProps {
  query: string;
  onQueryChange: (value: string) => void;
  // Etikett för den aktiva stegfilter-chipen ("FILTER: SKICKAD ✕"); null = inget
  // aktivt filter → ingen chip.
  activeFilterLabel: string | null;
  onClearFilter: () => void;
}

/**
 * Kontrollrad ovanför Lista-vyn (design 2a §6) — sökfält + ev. aktiv
 * stegfilter-chip. Vy-växlaren (Lista/Tavla/Tabell) INFÖRS I PR 8 (CTO D1: en
 * ensam växel utan Tavla/Tabell vore en falsk affordans) och saknas därför här.
 *
 * Sökfältet bär en `aria-label`, INGEN placeholder-exempeltext (Klas-mandat
 * §15 / civic-utility — etiketten bär instruktionen). `type="search"` ger
 * inbyggd rensning + korrekt semantik. Sök/filter gäller enbart Lista-
 * sektionerna, aldrig "Kräver åtgärd"-kön (den är en alltid-på accelerator).
 */
export function ApplicationsControls({
  query,
  onQueryChange,
  activeFilterLabel,
  onClearFilter,
}: ApplicationsControlsProps) {
  const tUi = useTranslations("applications.ui");
  return (
    <div className="jp-appcontrols">
      <div className="jp-appcontrols__search">
        <Search
          size={16}
          className="jp-appcontrols__searchicon"
          aria-hidden="true"
        />
        <input
          type="search"
          className="jp-appcontrols__input"
          aria-label={tUi("controls.searchAriaLabel")}
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
        />
      </div>

      {activeFilterLabel != null && (
        <span className="jp-listfilterchip">
          <span className="jp-listfilterchip__label">
            {tUi("controls.filterPrefix")} {activeFilterLabel}
          </span>
          <button
            type="button"
            className="jp-listfilterchip__rm"
            onClick={onClearFilter}
            aria-label={tUi("controls.clearFilterAriaLabel", {
              step: activeFilterLabel,
            })}
          >
            <X size={14} aria-hidden="true" />
          </button>
        </span>
      )}
    </div>
  );
}
