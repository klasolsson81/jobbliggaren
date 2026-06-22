"use client";

// "use client": pinnade chips + kryssrute-lista med onToggle/onClear. Generisk
// presentations-sektion för en platt facet (option-lista + pinnade chips).
// Driver i dag Anställningsformer; ort flyttades till RegionMunicipalityCascade
// (Spår 3 PR-D) men formen är densamma för vilken platt facet som helst.
// Extraherad ur match-preferences-dialog (ADR 0077 STEG 5), delad med
// match-setup-wizard.

import { useTranslations } from "next-intl";
import { labelsForSelected, type Option } from "./match-preferences-shared";
import { CheckItem, PinnedChips } from "./section-helpers";

interface FacetSectionProps {
  /** Sektions-rubrik (t.ex. "Anställningsformer"). */
  readonly title: string;
  /** Alla valbara options (concept-id + svenskt namn). */
  readonly options: ReadonlyArray<Option>;
  /** Valda concept-id (draft). */
  readonly selected: ReadonlyArray<string>;
  /** Toggla ett concept-id i draften. */
  readonly onToggle: (conceptId: string) => void;
  /** Töm valet helt. */
  readonly onClear: () => void;
  /** aria-label för de pinnade chipsen (t.ex. "Valda anställningsformer"). */
  readonly pinnedAriaLabel: string;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
  /**
   * Visa sektionens egna rubrik. Default true (dialogen). Wizarden sätter false
   * (DialogTitle bär rubriken — en andra inline-rubrik vore en dubblett). När
   * false renderas bara Rensa-länken (när något är valt).
   */
  readonly showHeading?: boolean;
}

/**
 * En enkel facet-sektion (lista + pinnade chips). Driver i dag
 * ANSTÄLLNINGSFORMER; samma markup/roller/etiketter som dialogen renderade
 * inline tidigare (behållet för regressionsvakt). Generisk nog för vilken
 * platt facet som helst.
 */
export function FacetSection({
  title,
  options,
  selected,
  onToggle,
  onClear,
  pinnedAriaLabel,
  headingId,
  showHeading = true,
}: FacetSectionProps) {
  const t = useTranslations("settings");
  const chips = labelsForSelected(selected, options);
  return (
    <>
      {showHeading ? (
        <div className="jp-matchdialog__sectionhead">
          <span id={headingId} className="jp-popover__title">
            {title}
          </span>
          {selected.length > 0 && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          )}
        </div>
      ) : (
        selected.length > 0 && (
          <div className="jp-matchdialog__sectionhead jp-matchdialog__sectionhead--clearonly">
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("matchPrefs.clear")}
            </button>
          </div>
        )
      )}
      <PinnedChips items={chips} onRemove={onToggle} ariaLabel={pinnedAriaLabel} />
      <div className="jp-matchdialog__list">
        {options.map((o) => (
          <CheckItem
            key={o.conceptId}
            label={o.label}
            checked={selected.includes(o.conceptId)}
            onToggle={() => onToggle(o.conceptId)}
          />
        ))}
      </div>
    </>
  );
}
