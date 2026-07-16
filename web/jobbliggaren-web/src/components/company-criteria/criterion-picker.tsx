"use client";

// "use client": holds a label-filter string and delegates selection to the parent's draft. One axis
// of the criterion dialog (SNI branches or kommuner), composed from the generic CriterionTree.

import { useId, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CriterionTree, CheckBox, type CriterionTreeNode } from "./criterion-tree";

/** A flat leaf option for the filter view (name + wire code). */
export interface CriterionLeafOption {
  readonly code: string;
  readonly name: string;
}

interface CriterionPickerProps {
  readonly nodes: ReadonlyArray<CriterionTreeNode>;
  /** Every leaf across the tree, for the label-filter view. */
  readonly leaves: ReadonlyArray<CriterionLeafOption>;
  readonly selected: ReadonlySet<string>;
  /** Toggle a group's leaf codes (a tree node, or a single filtered leaf as `[code]`). */
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  readonly onClear: () => void;
  readonly heading: string;
  readonly help: string;
  readonly filterLabel: string;
  readonly filterHint: string;
  readonly groupAria: string;
  /** Axis-specific "3 branscher valda" / "2 kommuner valda", resolved by the dialog with the count. */
  readonly selectedCountLabel: string;
  /** Axis-specific message when the reference tree is empty (degraded load). */
  readonly optionsUnavailable: string;
}

export function CriterionPicker({
  nodes,
  leaves,
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

  const filteredLeaves = useMemo(() => {
    if (!isFiltering) return [];
    return leaves.filter((leaf) => leaf.name.toLowerCase().includes(trimmed));
  }, [leaves, trimmed, isFiltering]);

  const hasSelection = selected.size > 0;

  return (
    <section
      className="flex flex-col gap-2"
      role="group"
      aria-label={groupAria}
    >
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-label font-medium text-text-primary">{heading}</h3>
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
      <p className="text-body-sm text-text-primary">{help}</p>

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
          filteredLeaves.length === 0 ? (
            <p className="px-4 py-3 text-body-sm text-text-primary">{t("noMatch")}</p>
          ) : (
            <div role="group" aria-label={groupAria}>
              {filteredLeaves.map((leaf) => {
                const checked = selected.has(leaf.code);
                return (
                  <div
                    key={leaf.code}
                    role="checkbox"
                    aria-checked={checked}
                    tabIndex={0}
                    onClick={() => onToggle([leaf.code])}
                    onKeyDown={(e) => {
                      if (e.key === " " || e.key === "Enter") {
                        e.preventDefault();
                        onToggle([leaf.code]);
                      }
                    }}
                    className="flex cursor-pointer items-center gap-2.5 border-b border-border px-3 py-2 text-body-sm text-text-primary last:border-b-0"
                  >
                    <CheckBox state={checked ? "checked" : "unchecked"} />
                    <span>{leaf.name}</span>
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
