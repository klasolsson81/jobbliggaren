"use client";

// "use client": rena presentations-helpers (kryssrute-rad + pinnade chips) som
// bär onClick/onKeyDown. De delas mellan match-preferences-dialog OCH
// match-setup-rail-modal (DRY, ADR 0077 STEG 5, amendad #526) — extraherade ur dialogen utan
// beteendeändring (samma roller/etiketter/markup som tidigare).

import { useCallback, useRef } from "react";
import { Check } from "lucide-react";
import { useFocusAfterCommit } from "@/lib/hooks/use-focus-after-commit";
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

/**
 * Where focus goes when a chip is removed (#1918 B1, WCAG 2.4.3): the next chip's ⨯, else the
 * previous one's, else `fallback`, whichever key or pointer removed it. `buttons` holds each chip's
 * ⨯ by id; the owner fills it from the chips' ref callbacks.
 */
export function useChipRemovalFocus() {
  const focusAfterCommit = useFocusAfterCommit();
  const buttons = useRef(new Map<string, HTMLButtonElement>());
  const beforeRemove = useCallback(
    (
      ids: ReadonlyArray<string>,
      removed: string,
      fallback: () => HTMLElement | null | undefined
    ) => {
      const at = ids.indexOf(removed);
      const neighbour = ids[at + 1] ?? ids[at - 1];
      focusAfterCommit(
        () => (neighbour === undefined ? undefined : buttons.current.get(neighbour)) ?? fallback()
      );
    },
    [focusAfterCommit]
  );
  return { buttons, beforeRemove };
}

/**
 * Removable chips in a wrapping row. Stays mounted while empty so focus can still move after the
 * last chip goes; it renders nothing then.
 */
export function ChipList<T extends Option>({
  items,
  onRemove,
  focusAfterLast,
  className,
  ariaLabel,
}: {
  readonly items: ReadonlyArray<T>;
  readonly onRemove: (item: T) => void;
  /** Where focus goes once the last chip is removed: the owner's own add or change control. */
  readonly focusAfterLast: () => HTMLElement | null | undefined;
  readonly className: string;
  readonly ariaLabel?: string;
}) {
  const { buttons, beforeRemove } = useChipRemovalFocus();
  if (items.length === 0) return null;
  const ids = items.map((item) => item.conceptId);
  return (
    <ul className={className} aria-label={ariaLabel}>
      {items.map((item) => (
        <li key={item.conceptId}>
          <PreferenceChip
            ref={(el) => {
              if (el) buttons.current.set(item.conceptId, el);
              else buttons.current.delete(item.conceptId);
            }}
            label={item.label}
            onRemove={() => {
              beforeRemove(ids, item.conceptId, focusAfterLast);
              onRemove(item);
            }}
          />
        </li>
      ))}
    </ul>
  );
}

/** Pinnade, borttagbara chips överst i en sektion. Tom mängd → inget renderas. */
export function PinnedChips({
  items,
  onRemove,
  ariaLabel,
  focusAfterLast,
}: {
  readonly items: ReadonlyArray<Option>;
  readonly onRemove: (conceptId: string) => void;
  readonly ariaLabel: string;
  readonly focusAfterLast: () => HTMLElement | null | undefined;
}) {
  return (
    <ChipList
      className="jp-chiplist jp-matchdialog__pinned"
      items={items}
      onRemove={(item) => onRemove(item.conceptId)}
      ariaLabel={ariaLabel}
      focusAfterLast={focusAfterLast}
    />
  );
}
