"use client";

// "use client": pinnade chips + kryssrute-lista med onToggle/onClear. Delad
// presentations-sektion för Regioner OCH Anställningsformer (de är strukturellt
// identiska: en platt option-lista + pinnade chips). Extraherad ur
// match-preferences-dialog (ADR 0077 STEG 5), delad med match-setup-wizard.

import { labelsForSelected, type Option } from "./match-preferences-shared";
import { CheckItem, PinnedChips } from "./section-helpers";

interface FacetSectionProps {
  /** Sektions-rubrik ("Regioner" / "Anställningsformer"). */
  readonly title: string;
  /** Alla valbara options (concept-id + svenskt namn). */
  readonly options: ReadonlyArray<Option>;
  /** Valda concept-id (draft). */
  readonly selected: ReadonlyArray<string>;
  /** Toggla ett concept-id i draften. */
  readonly onToggle: (conceptId: string) => void;
  /** Töm valet helt. */
  readonly onClear: () => void;
  /** aria-label för de pinnade chipsen ("Valda regioner" osv.). */
  readonly pinnedAriaLabel: string;
  /** rubrik-id som värden kopplar `aria-labelledby` mot (för role=group). */
  readonly headingId?: string;
}

/**
 * En enkel facet-sektion (lista + pinnade chips). Driver både REGIONER och
 * ANSTÄLLNINGSFORMER — samma markup/roller/etiketter som dialogen renderade
 * inline tidigare (behållet för regressionsvakt).
 */
export function FacetSection({
  title,
  options,
  selected,
  onToggle,
  onClear,
  pinnedAriaLabel,
  headingId,
}: FacetSectionProps) {
  const chips = labelsForSelected(selected, options);
  return (
    <>
      <div className="jp-matchdialog__sectionhead">
        <span id={headingId} className="jp-popover__title">
          {title}
        </span>
        {selected.length > 0 && (
          <button type="button" className="jp-clearlink" onClick={onClear}>
            Rensa
          </button>
        )}
      </div>
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
