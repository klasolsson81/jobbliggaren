"use client";

import { useRef } from "react";
import { X } from "lucide-react";
import type { SuggestionDto } from "@/lib/dto/job-ads";
import type { SearchChip } from "@/lib/job-ads/chip-models";
import { JobAdTypeahead } from "./job-ad-typeahead";

/**
 * Chips-i-sökfältet (Fas E2h, CTO VAL 4 = Variant B — komposition, inte
 * ombyggnad). Presentations-komponent: renderar aktiva sök-chips + typeahead-
 * inputen i SAMMA visuella fält. Föräldern (`JobbHeroSearch`) äger URL-staten
 * och all semantik — chips kommer deriverade ur URL:en (E2g-principen),
 * borttagning/val/tokenisering går upp via callbacks.
 *
 * Chip-× är riktiga knappar (tangentbords-borttagbara); Backspace i tomt
 * input tar sista chipen (via JobAdTypeahead.onEmptyBackspace). Tab väljer
 * markerat förslag (selectOnTab — dokumenterat APG-avsteg per Klas-spec).
 */

interface ChipSearchFieldProps {
  id: string;
  chips: ReadonlyArray<SearchChip>;
  onRemoveChip: (chip: SearchChip) => void;
  /** Utkast-ordet (enda lokala staten i föräldern — chips bor i URL:en). */
  value: string;
  onChange: (next: string) => void;
  onSelect: (suggestion: SuggestionDto) => void;
  onRemoveLast: () => void;
  ariaDescribedBy?: string;
}

export function ChipSearchField({
  id,
  chips,
  onRemoveChip,
  value,
  onChange,
  onSelect,
  onRemoveLast,
  ariaDescribedBy,
}: ChipSearchFieldProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  // WCAG 2.4.3 (design-reviewer B2): chipen försvinner ur DOM vid × —
  // fokus återförs till inputen så tangentbordsanvändaren inte faller
  // till body och måste tabba om från dokumentstart.
  function removeChip(chip: SearchChip) {
    onRemoveChip(chip);
    inputRef.current?.focus();
  }

  return (
    <div className="jp-chipfield">
      {chips.map((chip) => (
        <span
          key={`${chip.axis}-${chip.value}`}
          className="jp-filterchip jp-filterchip--field"
        >
          {chip.label}
          <button
            type="button"
            className="jp-filterchip__rm"
            onClick={() => removeChip(chip)}
            aria-label={`Ta bort ${chip.label}`}
          >
            <X size={12} aria-hidden="true" />
          </button>
        </span>
      ))}
      <JobAdTypeahead
        id={id}
        value={value}
        onChange={onChange}
        onSelect={onSelect}
        selectOnTab
        onEmptyBackspace={onRemoveLast}
        inputRef={inputRef}
        wrapperClassName="jp-chipfield__typeahead"
        inputClassName="jp-hero__input jp-chipfield__input"
        ariaDescribedBy={ariaDescribedBy}
      />
    </div>
  );
}
