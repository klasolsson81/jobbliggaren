"use client";

// "use client": pinnade chips + kryssrute-lista med onToggle/onClear. Generisk
// presentations-sektion för en platt facet (option-lista + pinnade chips).
// Driver i dag Anställningsformer; ort flyttades till RegionMunicipalityCascade
// (Spår 3 PR-D) men formen är densamma för vilken platt facet som helst.
// Extraherad ur match-preferences-dialog (ADR 0077 STEG 5), delad med
// match-setup-rail-modal.

import { useRef } from "react";
import { useTranslations } from "next-intl";
import { useFocusAfterCommit } from "@/lib/hooks/use-focus-after-commit";
import { labelsForSelected, type Option } from "./match-preferences-shared";
import { CheckItem, PinnedChips } from "./section-helpers";

interface FacetSectionProps {
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
}

/**
 * En enkel facet-sektion (lista + pinnade chips). Driver i dag
 * ANSTÄLLNINGSFORMER; generisk nog för vilken platt facet som helst.
 */
export function FacetSection({
  options,
  selected,
  onToggle,
  onClear,
  pinnedAriaLabel,
}: FacetSectionProps) {
  const t = useTranslations("settings");
  const chips = labelsForSelected(selected, options);
  // The list is the section's own control: where focus goes when the last chip, or everything by
  // "Rensa", goes.
  const listRef = useRef<HTMLDivElement | null>(null);
  const firstOption = () => listRef.current?.querySelector<HTMLElement>('[role="checkbox"]');
  const focusAfterCommit = useFocusAfterCommit();
  return (
    <>
      {selected.length > 0 && (
        <div className="jp-matchdialog__sectionhead jp-matchdialog__sectionhead--clearonly">
          <button
            type="button"
            className="jp-clearlink"
            onClick={() => {
              focusAfterCommit(firstOption);
              onClear();
            }}
          >
            {t("matchPrefs.clear")}
          </button>
        </div>
      )}
      <PinnedChips
        items={chips}
        onRemove={onToggle}
        ariaLabel={pinnedAriaLabel}
        focusAfterLast={firstOption}
      />
      <div ref={listRef} className="jp-matchdialog__list">
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
