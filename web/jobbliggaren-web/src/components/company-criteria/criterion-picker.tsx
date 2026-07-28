"use client";

// "use client": holds a label-filter string and delegates selection to the parent's draft. One axis
// of the criterion dialog (SNI branches or kommuner), composed from the generic CriterionTree.

import { useId, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CriterionTree, CheckBox } from "./criterion-tree";
import { groupTriState } from "@/lib/company-criteria/criterion-selection";
import type {
  CriterionOption,
  CriterionTreeNode,
} from "@/lib/company-criteria/criterion-options";

/**
 * Above this many matches the filter list is NOT rendered — the tree is, with the count and a stated
 * reason. The ceiling is on RESULT CARDINALITY, never on query length, and it is never silent.
 *
 * The control this replaced gated on length (`MIN_QUERY = 2`) and its own docblock said why: "a 1-char
 * query would match hundreds of the ~900 options". Measured over all 944 names, that number does not
 * do that job — it blocks `c` (507 matches) while admitting `er` (665), and ten characters
 * (`verksamhet`, 152) narrow better than two. Length does not predict result size, which is why the
 * guard read as arbitrary.
 *
 * 300 sits in a measured gap: it excludes every non-selective query (`er` 665, `in` 649, `ni` 516,
 * `an` 405, `ve` 397) and admits every query that has actually narrowed something (`st` 275,
 * `ha` 181, `tr` 167, `verksamhet` 152, `dat` 22, `sys` 2). Above roughly a third of the catalogue the
 * filter has selected nothing, and the tree is the better rendering of "most of the catalogue".
 */
const MAX_FILTER_MATCHES = 300;

interface CriterionPickerProps {
  readonly nodes: ReadonlyArray<CriterionTreeNode>;
  /**
   * Every node at EVERY level, flattened, for the filter view — build it with
   * `flattenCriterionOptions(nodes)` so the two views can never describe different catalogues.
   */
  readonly options: ReadonlyArray<CriterionOption>;
  readonly selected: ReadonlySet<string>;
  /** Toggle a group's leaf codes (a tree node, or one filtered option's `leafCodes`). */
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  /**
   * The four props below are optional so a caller can host the axis's own chrome OUTSIDE the picker.
   * The popover puts its title, selection count and clear control in the panel header, where they have
   * reserved height and cannot shift the tree under the pointer; the criterion dialog stacks two
   * pickers in one scroll column and keeps all four inline. Parameterised by data, not by a mode flag.
   */
  readonly onClear?: () => void;
  readonly heading?: string;
  readonly help?: string;
  readonly selectedCountLabel?: string;
  readonly filterLabel: string;
  /** Omitted where the field's own label already says it (the popover). */
  readonly filterHint?: string;
  readonly groupAria: string;
  /** Axis-specific message when the reference tree is empty (degraded load). */
  readonly optionsUnavailable: string;
}

export function CriterionPicker({
  nodes,
  options,
  selected,
  onToggle,
  onClear,
  heading,
  help,
  selectedCountLabel,
  filterLabel,
  filterHint,
  groupAria,
  optionsUnavailable,
}: CriterionPickerProps) {
  const t = useTranslations("pages.foretag.criteria");
  const filterId = useId();
  const filterHelpId = useId();
  const [filter, setFilter] = useState("");

  const trimmed = filter.trim().toLocaleLowerCase("sv-SE");
  const isFiltering = trimmed.length > 0;

  // Matches at EVERY level (#999): a section, a division and a leaf can all carry the searched word,
  // and the control this replaced searched all three. Leaf-only matching is why "hard to find" survived
  // the last two rounds — you had to already know the detail code's exact wording.
  const filteredOptions = useMemo(() => {
    if (!isFiltering) return [];
    return options.filter((option) =>
      option.name.toLocaleLowerCase("sv-SE").includes(trimmed),
    );
  }, [options, trimmed, isFiltering]);

  const tooMany = filteredOptions.length > MAX_FILTER_MATCHES;
  const showFilterList = isFiltering && !tooMany && filteredOptions.length > 0;
  const hasSelection = selected.size > 0;
  const showInlineHeader = heading !== undefined || (onClear !== undefined && hasSelection);

  return (
    <section className="flex flex-col gap-2" role="group" aria-label={groupAria}>
      {showInlineHeader && (
        <div className="flex items-center justify-between gap-3">
          {heading !== undefined ? (
            <h3 className="text-label font-medium text-text-primary">{heading}</h3>
          ) : (
            <span />
          )}
          {onClear !== undefined && hasSelection && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("dialog.clear")}
            </button>
          )}
        </div>
      )}
      {help !== undefined && <p className="text-body-sm text-text-primary">{help}</p>}

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={filterId}>{filterLabel}</Label>
        <Input
          id={filterId}
          type="text"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          maxLength={80}
          aria-describedby={filterHint !== undefined ? filterHelpId : undefined}
        />
        {filterHint !== undefined && (
          <p id={filterHelpId} className="text-body-sm text-text-primary">
            {filterHint}
          </p>
        )}
      </div>

      {/* The result count is the load-bearing half of the ceiling: without it a sighted user cannot see
          that a hundred rows sit below the fold, and a screen-reader user gets no signal that the list
          changed at all while typing. Rendered for EVERY non-empty query, zero matches included, in a
          height-reserved slot so appearing and disappearing does not move the list. */}
      <p
        className="min-h-5 text-body-sm tabular-nums text-text-primary"
        role="status"
        aria-live="polite"
      >
        {isFiltering
          ? tooMany
            ? t("filterTooMany", { count: filteredOptions.length })
            : t("filterMatches", { count: filteredOptions.length })
          : ""}
      </p>

      {selectedCountLabel !== undefined && hasSelection && (
        <p className="text-body-sm font-medium text-text-primary">{selectedCountLabel}</p>
      )}

      <div className="max-h-72 overflow-y-auto rounded-md border border-border">
        {nodes.length === 0 ? (
          <p className="px-4 py-3 text-body-sm text-text-primary">{optionsUnavailable}</p>
        ) : isFiltering && filteredOptions.length === 0 ? (
          <p className="px-4 py-3 text-body-sm text-text-primary">{t("dialog.noMatch")}</p>
        ) : showFilterList ? (
          // No nested `role="group"` here: the <section> above already carries `groupAria`, and two
          // nested groups with the same label make AT announce the axis name three times over.
          filteredOptions.map((option) => {
            // Tri-state, not a boolean: a matched division is "mixed" when only some of its leaves
            // are selected, and rendering that as unchecked would let a click silently deselect the
            // part already chosen. `groupTriState` is the same derivation the tree rows use.
            const state = groupTriState(selected, option.leafCodes);
            return (
              <div
                key={option.key}
                role="checkbox"
                aria-checked={state === "indeterminate" ? "mixed" : state === "checked"}
                tabIndex={0}
                onClick={() => onToggle(option.leafCodes)}
                onKeyDown={(e) => {
                  if (e.key === " " || e.key === "Enter") {
                    e.preventDefault();
                    onToggle(option.leafCodes);
                  }
                }}
                // Indentation is a SECONDARY cue only. In a filtered list the ancestors are not
                // rendered, so equal indent on two rows can suggest a sibling relationship that does
                // not exist — and padding reaches no screen reader at all (WCAG 1.3.1). The CODE
                // carries the level in text: its length says which level it is (`A` / `62` / `62010`),
                // it lands in the row's accessible name, and two codes side by side settle whether the
                // rows are related. SNI 2025 has "Dataprogrammering" at two levels; its codes differ.
                style={{ paddingInlineStart: 12 + option.depth * 20 }}
                className="jp-criterionrow flex cursor-pointer items-center gap-2.5 border-b border-border py-2 pe-3 text-body-sm text-text-primary last:border-b-0"
              >
                <CheckBox state={state} />
                <span className="jp-mono shrink-0 text-caption tabular-nums text-text-secondary">
                  {option.code}
                </span>
                {/* An explicit space, not the flex `gap`: accessible-name computation concatenates
                    adjacent text nodes without one, so the row would announce as "62Dataprogrammering"
                    — the code fused onto the name it is there to distinguish. */}
                {" "}
                <span>{option.name}</span>
              </div>
            );
          })
        ) : (
          <CriterionTree
            nodes={nodes}
            selected={selected}
            onToggle={onToggle}
            groupAriaLabel={groupAria}
            expandAria={(name) => t("dialog.expandAria", { name })}
            collapseAria={(name) => t("dialog.collapseAria", { name })}
          />
        )}
      </div>
    </section>
  );
}
