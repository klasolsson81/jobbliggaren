"use client";

// The matching arm on `/foretag/bevakade/nya` HAS to be client state, and that is not a preference.
// Arriving on the route moves the seen-watermark in `after()`, and the read is `CreatedAt > lastSeen`
// — so a `<Link href="?matchande=on">` would re-render the route server-side against a watermark the
// first render already advanced, and land on the EMPTY state instead of the filtered view
// (design-reviewer Blocker 1, PR #1614). One request, one set, switched in the browser.
//
// `JobAdList`/`JobAdCard` are Server Components, so this component cannot render them: both views
// arrive as already-rendered `ReactNode` slots and it only chooses which one to mount. Cost: the
// matching subset is serialised into the RSC payload a second time — bounded by the server's row cap
// (`ListNewFollowedCompanyAdsQueryHandler.MaxRows`) and paid once per page load. The alternative is a
// second request, which is the divergence this whole route exists to close.

import { useState } from "react";
import type { ReactNode } from "react";
import { Filter } from "lucide-react";
import { Segment } from "@/components/ui/segment";

type NewAdsView = "all" | "matching";

interface NewAdsViewSwitchProps {
  /** Radiogroup-label (a11y): names the whole control, not one option. */
  groupLabel: string;
  allLabel: string;
  matchingLabel: string;
  /** The house's delivered filter vocabulary (`jobads.companyWatches.filter.activeOnlyMatched`). */
  filteredNote: string;
  emptyTitle: string;
  emptyBody: string;
  emptyAction: string;
  /** Every new ad, server-rendered. */
  all: ReactNode;
  /** The matching subset, server-rendered. Rendered only when `matchingCount > 0`. */
  matching: ReactNode;
  /** Counted over the WHOLE fetched set on the server — never re-derived here. */
  matchingCount: number;
}

export function NewAdsViewSwitch({
  groupLabel,
  allLabel,
  matchingLabel,
  filteredNote,
  emptyTitle,
  emptyBody,
  emptyAction,
  all,
  matching,
  matchingCount,
}: NewAdsViewSwitchProps) {
  // `all` is the default on purpose: with no client JS the page still shows the WHOLE set and merely
  // lacks the filter, rather than showing a subset it cannot explain or leave.
  const [view, setView] = useState<NewAdsView>("all");
  const filtered = view === "matching";

  return (
    <>
      {/* Own control row, off the status line: a border + 16px keep "what you can do" from reading
          as a continuation of "what is" (design-reviewer Major 6). */}
      <div className="mt-4 flex justify-end border-t border-border pt-4">
        <Segment<NewAdsView>
          value={view}
          onChange={setView}
          aria-label={groupLabel}
          options={[
            { value: "all", label: allLabel },
            { value: "matching", label: matchingLabel },
          ]}
        />
      </div>

      {/* Persistent live region, so switching INTO the filtered view is announced and not only
          shown. Absence = no filter — the same rule the per-watch filter line follows (no
          "Inget filter" row, no empty chip). */}
      <div role="status" aria-live="polite">
        {filtered && (
          <p className="jp-transparency-note jp-transparency-note--compact mt-4">
            <Filter size={14} aria-hidden="true" />
            <span>{filteredNote}</span>
          </p>
        )}
      </div>

      {/* `.jp-jobs` carries no top margin of its own, so the gap that keeps the list off the row
          above lives here — one home, whichever of the three bodies renders. */}
      <div className="mt-4">
        {!filtered && all}
        {filtered && matchingCount > 0 && matching}
        {filtered && matchingCount === 0 && (
          // The route's OWN empty state. `JobAdList`'s would name filters and a search box that do
          // not exist on this surface (design-reviewer Major 8), and it would leave the user inside
          // a view with nothing in it and no way back stated.
          <div className="jp-empty">
            <div className="jp-empty__title">{emptyTitle}</div>
            <p className="jp-empty__body">{emptyBody}</p>
            <div className="jp-empty__actions">
              <button
                type="button"
                className="jp-btn jp-btn--ghost"
                onClick={() => setView("all")}
              >
                {emptyAction}
              </button>
            </div>
          </div>
        )}
      </div>
    </>
  );
}
