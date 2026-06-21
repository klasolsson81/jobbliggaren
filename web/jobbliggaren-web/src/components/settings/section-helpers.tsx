"use client";

// "use client": rena presentations-helpers (kryssrute-rad + pinnade chips) som
// bär onClick/onKeyDown. De delas mellan match-preferences-dialog OCH
// match-setup-wizard (DRY, ADR 0077 STEG 5) — extraherade ur dialogen utan
// beteendeändring (samma roller/etiketter/markup som tidigare).

import { Check } from "lucide-react";
import { PreferenceChip } from "./preference-chip";
import type { Option } from "./match-preferences-shared";

/**
 * En kryssrute-rad (.jp-checkitem-mönstret, delat med kortet/jobb-panelen).
 * `isAll` ger "Välj alla"-radens framträdande stil (samma som jobbsidans
 * popover) och `indeterminate` annonserar `aria-checked="mixed"` (WAI-ARIA
 * tri-state) vid partiellt val — skärmläsaren hör "delvis markerad", inte
 * "omarkerad". Båda är opt-in (default = vanlig rad, oförändrat beteende).
 */
export function CheckItem({
  label,
  checked,
  onToggle,
  isAll,
  indeterminate,
}: {
  readonly label: string;
  readonly checked: boolean;
  readonly onToggle: () => void;
  readonly isAll?: boolean;
  readonly indeterminate?: boolean;
}) {
  return (
    <div
      className={isAll ? "jp-checkitem jp-checkitem--all" : "jp-checkitem"}
      role="checkbox"
      aria-checked={indeterminate ? "mixed" : checked}
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
