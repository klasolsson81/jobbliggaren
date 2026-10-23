"use client";

// "use client": the typed word is local state, and the search is a debounced browser fetch.

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { OccupationSearchField } from "@/components/occupation-search/occupation-search-field";
import {
  OccupationSearchResults,
  occupationSearchBox,
} from "@/components/occupation-search/occupation-search-results";
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
  readonly footer: React.ReactNode;
  readonly onClose: () => void;
  readonly triggerRef: React.RefObject<HTMLButtonElement | null>;
}

/**
 * /jobb's Yrke popover with the structured occupation search (#2198, ADR 0180): the field above the
 * columns, and the answer in the columns' place while it has rows.
 *
 * The typed word lives here and nowhere else. It starts empty on every opening, because the host
 * remounts this component by key, and it never leaves this component: what a row selects goes out
 * through `onChange` as group ids, like a browse pick.
 */
export function JobbYrkePopover({
  open,
  groups,
  selected,
  onChange,
  counts,
  footer,
  onClose,
  triggerRef,
}: JobbYrkePopoverProps) {
  const t = useTranslations("jobads.ui");
  const fieldId = `jp-yrke-search-${useId()}`;
  const [term, setTerm] = useState("");
  const search = useOccupationSearch(term);
  const box = occupationSearchBox(search);

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
      onClearAll={() => onChange([])}
      triggerRef={triggerRef}
      search={{
        field: <OccupationSearchField id={fieldId} value={term} onChange={setTerm} state={search} />,
        results:
          box.kind === "browse" ? null : (
            <div className="jp-occsearch">
              <OccupationSearchResults
                result={box.kind === "rows" ? box.result : null}
                selected={selected}
                onToggle={(groupConceptId) => toggle(selected, groupConceptId, onChange)}
                counts={counts}
              />
            </div>
          ),
      }}
    />
  );
}
