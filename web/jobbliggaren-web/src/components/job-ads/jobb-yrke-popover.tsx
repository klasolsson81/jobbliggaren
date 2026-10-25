"use client";

// "use client": the typed word is local state, and the search is a debounced browser fetch.

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { OccupationSearchField } from "@/components/occupation-search/occupation-search-field";
import {
  OccupationSearchResults,
  answerHoldsNames,
  occupationSearchBox,
} from "@/components/occupation-search/occupation-search-results";
import { useFacetCounts, type FacetCountsFilterState } from "@/lib/hooks/use-facet-counts";
import { useOccupationSearch } from "@/lib/hooks/use-occupation-search";
import { JobbFilterPopover, toggle, type PopoverGroup } from "./jobb-filter-popover";

interface JobbYrkePopoverProps {
  readonly open: boolean;
  readonly groups: ReadonlyArray<PopoverGroup>;
  /** The selected occupation-group ids: the URL's `occupationGroup` axis. */
  readonly selected: ReadonlyArray<string>;
  /** Live commit of the next group-id list, as the browse rows make it. */
  readonly onChange: (next: string[]) => void;
  /** Facet counts by group id, for the browse items and the search's Group rows. */
  readonly counts: Record<string, number> | null;
  /** The selected occupation-name ids: the URL's `occupationName` axis (#2199). */
  readonly selectedNames: ReadonlyArray<string>;
  /** Live commit of the next occupation-name list. */
  readonly onNamesChange: (next: string[]) => void;
  /** Clears groups and names in one commit. */
  readonly onClearAll: () => void;
  /** The other dimensions' state, which the occupation-name counts are taken under. */
  readonly facetFilter: FacetCountsFilterState;
  readonly footer: React.ReactNode;
  readonly onClose: () => void;
  readonly triggerRef: React.RefObject<HTMLButtonElement | null>;
}

/**
 * /jobb's Yrke popover with the structured occupation search (#2198, ADR 0180): the field above the
 * columns, and the answer in the columns' place while it has rows.
 *
 * The typed word lives here and nowhere else. It starts empty on every opening, because the host
 * remounts this component by key. A group pick goes out through `onChange` as group ids, like a
 * browse pick; an occupation-name pick goes out through `onNamesChange` by its own id (#2199).
 *
 * The occupation-name counts are fetched only once an answer in this opening holds a name a tick
 * could select.
 */
export function JobbYrkePopover({
  open,
  groups,
  selected,
  onChange,
  counts,
  selectedNames,
  onNamesChange,
  onClearAll,
  facetFilter,
  footer,
  onClose,
  triggerRef,
}: JobbYrkePopoverProps) {
  const t = useTranslations("jobads.ui");
  const fieldId = `jp-yrke-search-${useId()}`;
  const [term, setTerm] = useState("");
  const search = useOccupationSearch(term);
  const box = occupationSearchBox(search);

  const [sawNames, setSawNames] = useState(false);
  if (!sawNames && box.kind === "rows" && answerHoldsNames(box.result)) setSawNames(true);
  const nameCounts = useFacetCounts(
    "OccupationName",
    { ...facetFilter, occupationGroup: [], occupationName: [] },
    open && sawNames,
  );

  return (
    <JobbFilterPopover
      open={open}
      leftTitle={t("heroFilters.yrkeLeftTitle")}
      dialogLabel={t("heroFilters.yrke")}
      rightTitle={t("heroFilters.yrkeRightTitle")}
      selectAllLabel={() => t("heroFilters.yrkeSelectAll")}
      emptyText={t("heroFilters.yrkeEmpty")}
      rightEmptyText={t("heroFilters.yrkeRightEmpty")}
      groups={groups}
      selected={selected}
      onChange={onChange}
      counts={counts}
      footer={footer}
      onClose={onClose}
      onClearAll={onClearAll}
      triggerRef={triggerRef}
      search={{
        field: (
          <OccupationSearchField
            id={fieldId}
            value={term}
            onChange={setTerm}
            state={search}
            idleStatus={
              selectedNames.length > 0
                ? t("heroFilters.yrkeNamesSelected", { count: selectedNames.length })
                : undefined
            }
          />
        ),
        holdsHiddenSelection: selectedNames.length > 0,
        results:
          box.kind === "browse" ? null : (
            <div className="jp-occsearch">
              <OccupationSearchResults
                result={box.kind === "rows" ? box.result : null}
                selected={selected}
                onToggle={(groupConceptId) => toggle(selected, groupConceptId, onChange)}
                counts={counts}
                nameSelection={{
                  selected: selectedNames,
                  onToggle: (occupationNameConceptId) =>
                    toggle(selectedNames, occupationNameConceptId, onNamesChange),
                  counts: nameCounts,
                  wordHint: t("heroFilters.yrkeNameHint"),
                  groupHint: t("heroFilters.yrkeGroupNameHint"),
                }}
              />
            </div>
          ),
      }}
    />
  );
}
