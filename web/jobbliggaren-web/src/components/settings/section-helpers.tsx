"use client";

// "use client": rena presentations-helpers (kryssrute-rad + pinnade chips) som
// bär onClick/onKeyDown. De delas mellan match-preferences-dialog OCH
// match-setup-rail-modal (DRY, ADR 0077 STEG 5, amendad #526) — extraherade ur dialogen utan
// beteendeändring (samma roller/etiketter/markup som tidigare).

import { Check } from "lucide-react";
import { PreferenceChip } from "./preference-chip";
import type { Option } from "./match-preferences-shared";

/**
 * En kryssrute-rad (.jp-checkitem-mönstret, delat med kortet/jobb-panelen).
 * `isAll` ger "Välj alla"-radens framträdande stil (samma som jobbsidans
 * popover) och `indeterminate` annonserar `aria-checked="mixed"` (WAI-ARIA
 * tri-state) vid partiellt val — skärmläsaren hör "delvis markerad", inte
 * "omarkerad". `describedBy` kopplar en förklaring till kontrollen: i en
 * skärmläsares forms-mode läses bara namnet + beskrivningen, så en text som
 * BARA står bredvid kontrollen når aldrig fram (bevakning F4b: skälet till att
 * ett filter är inert). Alla tre är opt-in — default = oförändrat beteende.
 */
export function CheckItem({
  label,
  checked,
  onToggle,
  isAll,
  indeterminate,
  describedBy,
}: {
  readonly label: string;
  readonly checked: boolean;
  readonly onToggle: () => void;
  readonly isAll?: boolean;
  readonly indeterminate?: boolean;
  readonly describedBy?: string;
}) {
  return (
    <div
      className={isAll ? "jp-checkitem jp-checkitem--all" : "jp-checkitem"}
      role="checkbox"
      aria-checked={indeterminate ? "mixed" : checked}
      aria-describedby={describedBy}
      tabIndex={0}
      onClick={onToggle}
      onKeyDown={(e) => {
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          onToggle();
        }
      }}
    >
      <span className="jp-checkitem__box">
        {checked && <Check size={14} aria-hidden="true" />}
      </span>
      {label}
    </div>
  );
}

/** Pinnade, borttagbara chips överst i en sektion. Tom mängd → inget renderas. */
export function PinnedChips({
  items,
  onRemove,
  ariaLabel,
}: {
  readonly items: ReadonlyArray<Option>;
  readonly onRemove: (conceptId: string) => void;
  readonly ariaLabel: string;
}) {
  if (items.length === 0) return null;
  return (
    <ul className="jp-chiplist jp-matchdialog__pinned" aria-label={ariaLabel}>
      {items.map((it) => (
        <li key={it.conceptId}>
          <PreferenceChip label={it.label} onRemove={() => onRemove(it.conceptId)} />
        </li>
      ))}
    </ul>
  );
}
