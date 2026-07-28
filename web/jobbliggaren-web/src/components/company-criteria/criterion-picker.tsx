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
  readonly onClear: () => void;
  /**
   * Optional (#999): inside a popover whose dialog label already names the axis, a heading and a help
   * paragraph repeat what the panel header just said — filler the civic-utility rules reject. The
   * criterion dialog stacks two pickers in one scroll column and still needs both.
   */
  readonly heading?: string;
  readonly help?: string;
  readonly filterLabel: string;
  readonly filterHint: string;
  readonly groupAria: string;
  /** Axis-specific "3 branscher valda" / "2 kommuner valda", resolved by the caller with the count. */
  readonly selectedCountLabel: string;
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
  filterLabel,
  filterHint,
  groupAria,
  selectedCountLabel,
  optionsUnavailable,
}: CriterionPickerProps) {
  const t = useTranslations("pages.foretag.criteria.dialog");
  const filterId = useId();
  const filterHelpId = useId();
  const [filter, setFilter] = useState("");

  const trimmed = filter.trim().toLowerCase();
  const isFiltering = trimmed.length > 0;

  // Matches at EVERY level (#999): a section, a division and a leaf can all carry the searched word,
  // and the control this replaced searched all three. Leaf-only matching is why "hard to find" survived
  // the last two rounds — you had to already know the detail code's exact wording.
  const filteredOptions = useMemo(() => {
    if (!isFiltering) return [];
    return options.filter((option) => option.name.toLowerCase().includes(trimmed));
  }, [options, trimmed, isFiltering]);

  const hasSelection = selected.size > 0;

  return (
    <section
      className="flex flex-col gap-2"
      role="group"
      aria-label={groupAria}
    >
      {(heading !== undefined || hasSelection) && (
        <div className="flex items-center justify-between gap-3">
          {heading !== undefined ? (
            <h3 className="text-label font-medium text-text-primary">{heading}</h3>
          ) : (
            <span />
          )}
          {hasSelection && (
            <button
              type="button"
              className="text-body-sm font-medium text-brand-700 hover:underline"
              onClick={onClear}
            >
              {t("clear")}
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
          aria-describedby={filterHelpId}
        />
        <p id={filterHelpId} className="text-body-sm text-text-primary">
          {filterHint}
        </p>
      </div>

      {hasSelection && (
        <p className="text-body-sm font-medium text-text-primary" aria-live="polite">
          {selectedCountLabel}
        </p>
      )}

      <div className="max-h-72 overflow-y-auto rounded-md border border-border">
        {nodes.length === 0 ? (
          <p className="px-4 py-3 text-body-sm text-text-primary">
            {optionsUnavailable}
          </p>
        ) : isFiltering ? (
          filteredOptions.length === 0 ? (
            <p className="px-4 py-3 text-body-sm text-text-primary">{t("noMatch")}</p>
          ) : (
            <div role="group" aria-label={groupAria}>
              {filteredOptions.map((option) => {
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
                    // Indented by level so a division and the leaf of nearly the same name are
                    // distinguishable — SNI 2025 has "Dataprogrammering" at both levels, and two
                    // identical-looking rows that select different amounts is a trap.
                    style={{ paddingInlineStart: 12 + option.depth * 20 }}
                    className="jp-criterionrow flex cursor-pointer items-center gap-2.5 border-b border-border py-2 pe-3 text-body-sm text-text-primary last:border-b-0"
                  >
                    <CheckBox state={state} />
                    <span>{option.name}</span>
                  </div>
                );
              })}
            </div>
          )
        ) : (
          <CriterionTree
            nodes={nodes}
            selected={selected}
            onToggle={onToggle}
            groupAriaLabel={groupAria}
            expandAria={(name) => t("expandAria", { name })}
            collapseAria={(name) => t("collapseAria", { name })}
          />
        )}
      </div>
    </section>
  );
}
