"use client";

import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";
import { Segment } from "@/components/ui/segment";
import type { ApplicationsView } from "@/lib/applications/view";

interface ApplicationsControlsProps {
  query: string;
  onQueryChange: (value: string) => void;
  // Etikett för den aktiva stegfilter-chipen ("FILTER: SKICKAD ✕"); null = inget
  // aktivt filter → ingen chip (även Tavla skickar null — stegfiltret gäller ej där).
  activeFilterLabel: string | null;
  onClearFilter: () => void;
  // Aktiv vy + växlingscallback (#630 PR 8). Tabell utelämnas tills PR 10 (CTO
  // D-D: en disabled/platshållar-vy vore en falsk affordans) — växlaren visar
  // Lista/Tavla och växer honestly till tre när Tabell faktiskt levereras.
  view: ApplicationsView;
  onViewChange: (view: ApplicationsView) => void;
}

/**
 * Kontrollrad (design 2a §6, ADR 0092 D1) — sökfält + ev. aktiv stegfilter-chip
 * + VY-växlaren (Lista/Tavla) till höger. Delad chrome ovanför båda vyerna.
 *
 * Växlaren återbrukar `Segment` (role=radiogroup, piltangent-nav, aktiv =
 * accent-800-fyll) — samma primitiv som Inställningarnas tema/språk-växlar (DRY,
 * CTO-bind D-A). Aktiv-etiketten bärs via `aria-label` ("Visa ansökningar som");
 * den synliga mono-"VY" är dekorativ kontext.
 *
 * Sökfältet bär en `aria-label`, INGEN placeholder-exempeltext (Klas-mandat
 * §15 / civic-utility — etiketten bär instruktionen). `type="search"` ger
 * inbyggd rensning + korrekt semantik. Sök gäller Lista-sektionerna OCH Tavla-
 * korten, aldrig "Kräver åtgärd"-kön (den är en alltid-på accelerator).
 */
export function ApplicationsControls({
  query,
  onQueryChange,
  activeFilterLabel,
  onClearFilter,
  view,
  onViewChange,
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

      <div className="jp-appcontrols__vy">
        <span className="jp-vylabel jp-mono" aria-hidden="true">
          {tUi("controls.viewLabel")}
        </span>
        <Segment
          value={view}
          onChange={onViewChange}
          aria-label={tUi("controls.viewSwitchAriaLabel")}
          options={[
            { value: "lista", label: tUi("view.lista") },
            { value: "tavla", label: tUi("view.tavla") },
          ]}
        />
      </div>
    </div>
  );
}
