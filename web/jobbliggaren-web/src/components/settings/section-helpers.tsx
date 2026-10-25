"use client";

// "use client": presentations-helpers (kryssrute-rad + pinnade chips) som
// bär onClick/onKeyDown. De delas mellan match-preferences-dialog OCH
// match-setup-rail-modal (DRY, ADR 0077 STEG 5, amendad #526) — extraherade ur dialogen utan
// beteendeändring (samma roller/etiketter/markup som tidigare).

import { type KeyboardEvent, useCallback, useId, useRef } from "react";
import { Check } from "lucide-react";
import { useFormatter } from "next-intl";
import { formatNumber } from "@/lib/i18n/format";
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
 *
 * `secondary` (#2198) gives the row a second line, the evidence the user chooses by. The row's name
 * is then the first line alone and the second becomes its description, so a screen reader does not
 * run the two together. Without it the row renders exactly as before.
 *
 * `count` (#2198 PR 2) is a facet count, "(n)", shown on the first line and part of the row's name in
 * both forms, as the /jobb popover's own rows show it. Without it the row renders exactly as before.
 *
 * `included` (#2199) marks a row that is already part of a wider choice: checked, `aria-disabled`,
 * still focusable, and inert to click, Space and Enter. The type admits it only beside `secondary`,
 * because the second line is where the row says why. Without it the row renders exactly as before.
 */
export function CheckItem(props: CheckItemProps) {
  if (props.secondary !== undefined) {
    return <TwoLineCheckItem {...props} secondary={props.secondary} />;
  }
  const { label, checked, onToggle, isAll, indeterminate, describedBy, count } = props;
  return (
    <div
      className={isAll ? "jp-checkitem jp-checkitem--all" : "jp-checkitem"}
      role="checkbox"
      aria-checked={indeterminate ? "mixed" : checked}
      aria-describedby={describedBy}
      tabIndex={0}
      onClick={onToggle}
      onKeyDown={(e) => toggleOnSpaceOrEnter(e, onToggle)}
    >
      <span className="jp-checkitem__box">
        {checked && <Check size={14} aria-hidden="true" />}
      </span>
      {count === undefined ? (
        label
      ) : (
        <span className="flex min-w-0 flex-1 items-baseline gap-3">
          {label}
          <CheckItemCount count={count} />
        </span>
      )}
    </div>
  );
}

interface CheckItemBaseProps {
  readonly label: string;
  readonly checked: boolean;
  readonly onToggle: () => void;
  readonly isAll?: boolean;
  readonly indeterminate?: boolean;
  readonly describedBy?: string;
  readonly count?: number;
}

type CheckItemProps = CheckItemBaseProps &
  (
    | { readonly secondary?: undefined; readonly included?: never }
    | { readonly secondary: string; readonly included?: boolean }
  );

function toggleOnSpaceOrEnter(e: KeyboardEvent<HTMLElement>, onToggle?: () => void) {
  if (e.key === " " || e.key === "Enter") {
    e.preventDefault();
    onToggle?.();
  }
}

// Its own component so the formatter is called only where a count is shown.
function CheckItemCount({ id, count }: { readonly id?: string; readonly count: number }) {
  const format = useFormatter();
  return (
    <span id={id} className="jp-checkitem__count">
      ({formatNumber(format, count)})
    </span>
  );
}

// Its own component so the ids it needs are created only where a second line exists: the one-line
// row keeps its markup, and calls no hook it did not call before.
function TwoLineCheckItem({
  label,
  checked,
  onToggle,
  isAll,
  indeterminate,
  describedBy,
  secondary,
  count,
  included,
}: CheckItemBaseProps & { readonly secondary: string; readonly included?: boolean }) {
  const id = useId();
  const nameId = `${id}-name`;
  const secondaryId = `${id}-secondary`;
  const countId = `${id}-count`;
  const toggle = included ? undefined : onToggle;
  const className = included
    ? "jp-checkitem jp-checkitem--included"
    : isAll
      ? "jp-checkitem jp-checkitem--all"
      : "jp-checkitem";
  return (
    <div
      className={className}
      role="checkbox"
      aria-checked={included ? true : indeterminate ? "mixed" : checked}
      aria-disabled={included || undefined}
      aria-labelledby={count === undefined ? nameId : `${nameId} ${countId}`}
      aria-describedby={describedBy === undefined ? secondaryId : `${secondaryId} ${describedBy}`}
      tabIndex={0}
      onClick={toggle}
      onKeyDown={(e) => toggleOnSpaceOrEnter(e, toggle)}
    >
      <span className="jp-checkitem__box">
        {(checked || included) && <Check size={14} aria-hidden="true" />}
      </span>
      <span className={count === undefined ? "flex min-w-0 flex-col" : "flex min-w-0 flex-1 flex-col"}>
        {count === undefined ? (
          <span id={nameId}>{label}</span>
        ) : (
          <span className="flex items-baseline gap-3">
            <span id={nameId}>{label}</span>
            <CheckItemCount id={countId} count={count} />
          </span>
        )}
        <span id={secondaryId} className="text-body-sm text-text-secondary">
          {secondary}
        </span>
      </span>
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
