"use client";

import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";

/**
 * #997 (S2) — one branch option for the single-select bransch typeahead. Built CLIENT-SIDE from the
 * SCB `CriterionReference` already loaded into the page (`section` + `division` + `leaf` names are all
 * searchable, CTO granularity decision), each carrying the SNI leaf codes it expands to. `key` is a
 * level-prefixed SCB code so the three levels never collide as DOM ids / React keys.
 */
export interface BranschOption {
  readonly key: string;
  readonly label: string;
  readonly leafCodes: ReadonlyArray<string>;
}

interface BranschTypeaheadProps {
  /** DOM id for the input (the parent's `<label htmlFor>` targets it). */
  readonly id: string;
  /** All searchable branch options, built from the reference by the parent (no fetch, no network). */
  readonly options: ReadonlyArray<BranschOption>;
  /** Called when the user picks an option (click / Enter on the active row). Replaces the parent's chip. */
  readonly onSelect: (option: BranschOption) => void;
  /** Degraded reference → the field disables civilly; the parent shows the "list unavailable" notice. */
  readonly disabled?: boolean;
  /** Ids of the hint (+ degraded notice) the parent renders, wired via `aria-describedby`. */
  readonly ariaDescribedBy?: string;
}

/** Below this the client list is not shown (a 1-char query would match hundreds of the ~900 options). */
const MIN_QUERY = 2;
/** The listbox is capped so it stays a scannable dropdown, never a 900-row DOM dump. */
const MAX_RESULTS = 50;

/**
 * #997 (S2) — the single-select bransch combobox. It reuses the WAI-ARIA 1.2 combobox a11y PATTERN of
 * `job-ad-typeahead.tsx` (role=combobox + aria-expanded/controls/autocomplete/activedescendant, a
 * role=listbox of role=option rows, ArrowUp/Down/Enter/Escape, `pointerdown` outside-dismissal, option
 * `onMouseDown`+preventDefault to keep input focus) but SWAPS its `/api/jobb/suggest` fetch datasource
 * for a pure client-side `label.includes(query)` filter over the SCB reference (parity
 * `criterion-picker.tsx`) — a design-reviewer Blocker: NO fetch, NO network per keystroke.
 *
 * The input is a transient search box: on select it clears itself and the chosen bransch becomes a
 * removable chip in the parent (a new pick REPLACES the single existing chip). No placeholder example
 * text (Klas hard rule) — the parent's label + hint carry the instruction.
 */
export function BranschTypeahead({
  id,
  options,
  onSelect,
  disabled,
  ariaDescribedBy,
}: BranschTypeaheadProps) {
  const t = useTranslations("pages.foretag.sok");
  const listId = useId();
  const optionBaseId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  // Active option for keyboard navigation (-1 = none → Enter is inert, never a stray form submit).
  const [active, setActive] = useState(-1);

  const trimmed = query.trim().toLowerCase();
  const isFiltering = trimmed.length >= MIN_QUERY;

  const matches = useMemo(() => {
    if (!isFiltering) return [];
    const hits = options.filter((o) => o.label.toLowerCase().includes(trimmed));
    // A light ranking on top of the includes() predicate: prefix matches first, then alphabetical (sv).
    // The cap keeps the listbox scannable; the sort makes the visible slice the most relevant one.
    hits.sort((a, b) => {
      const ap = a.label.toLowerCase().startsWith(trimmed) ? 0 : 1;
      const bp = b.label.toLowerCase().startsWith(trimmed) ? 0 : 1;
      if (ap !== bp) return ap - bp;
      return a.label.localeCompare(b.label, "sv");
    });
    return hits.slice(0, MAX_RESULTS);
  }, [options, trimmed, isFiltering]);

  const showList = open && matches.length > 0;
  const showNoMatch = open && isFiltering && matches.length === 0;

  // Outside-press dismissal on `pointerdown` (NOT mousedown): input-modality-agnostic (mouse/pen/touch),
  // fires before the focus/blur chain and before an option row's own onMouseDown, so a press inside the
  // widget is never misread as "outside". Attached only while open — no idle global handler.
  useEffect(() => {
    if (!open) return;
    function onDocumentPointerDown(e: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
        setActive(-1);
      }
    }
    document.addEventListener("pointerdown", onDocumentPointerDown);
    return () =>
      document.removeEventListener("pointerdown", onDocumentPointerDown);
  }, [open]);

  function choose(option: BranschOption) {
    onSelect(option);
    setQuery("");
    setOpen(false);
    setActive(-1);
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "Escape") {
      setOpen(false);
      setActive(-1);
      return;
    }
    if (!showList) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((i) => (i + 1) % matches.length);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((i) => (i <= 0 ? matches.length - 1 : i - 1));
    } else if (e.key === "Enter") {
      // Enter picks the active row; with no active row it just closes the open list (and is
      // preventDefaulted so it never leaks to a surrounding form as an accidental company search).
      e.preventDefault();
      const chosen = active >= 0 ? matches[active] : undefined;
      if (chosen) choose(chosen);
      else {
        setOpen(false);
        setActive(-1);
      }
    }
  }

  const optionId = (i: number) => `${optionBaseId}-${i}`;

  return (
    <div
      ref={rootRef}
      className="relative"
      onBlur={(e) => {
        // Focus leaving the widget (Tab away) closes the list; an option press keeps input focus
        // (its onMouseDown preventDefault), so a real selection is never mistaken for a focus-out.
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) {
          setOpen(false);
          setActive(-1);
        }
      }}
    >
      <input
        id={id}
        type="text"
        autoComplete="off"
        className="jp-input"
        role="combobox"
        aria-expanded={showList}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={
          showList && active >= 0 ? optionId(active) : undefined
        }
        aria-describedby={ariaDescribedBy}
        disabled={disabled}
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          setActive(-1);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
      />

      {showNoMatch && (
        <p
          className="absolute top-full left-0 z-10 mt-1 w-full rounded-md border border-border bg-surface-primary px-3 py-2 text-body-sm text-text-primary"
          style={{ boxShadow: "var(--jp-shadow-pop)" }}
        >
          {t("branschNoMatch")}
        </p>
      )}

      {showList && (
        <ul
          id={listId}
          role="listbox"
          aria-label={t("branschListLabel")}
          className="absolute top-full left-0 z-10 mt-1 max-h-72 w-full overflow-y-auto rounded-md border border-border bg-surface-primary"
          style={{ boxShadow: "var(--jp-shadow-pop)" }}
          // Clear the active row when the pointer leaves the list so a parked hover cannot leave a
          // stale highlight that Enter would then pick blind.
          onMouseLeave={() => setActive(-1)}
        >
          {matches.map((option, i) => (
            <li
              key={option.key}
              id={optionId(i)}
              role="option"
              aria-selected={i === active}
              className={`cursor-pointer px-3 py-2 text-body-sm ${
                i === active
                  ? "bg-surface-tertiary text-text-primary"
                  : "text-text-primary"
              }`}
              // onMouseDown (not onClick) → preventDefault keeps the input focused so blur does not
              // close the list before the pick registers.
              onMouseDown={(e) => {
                e.preventDefault();
                choose(option);
              }}
              onMouseEnter={() => setActive(i)}
            >
              {option.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
