"use client";

import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { useRecentSearchCounts } from "@/lib/hooks/use-recent-search-counts";

interface SavedSearchNoticeTextProps {
  /** Recent-search id used to look up the lazily-fetched hit count. */
  readonly searchId: string;
  /** Real recent-search label (no longer a mock). */
  readonly name: string;
}

/**
 * #294 — the Översikt saved-search notice text. The "N new hits since your last
 * run" count is deliberately NOT computed server-side (TD-94: the per-search
 * COUNT is an expensive N+1 the Översikt fetch skips with `includeCount=false`).
 * It is fetched lazily off the critical path via `useRecentSearchCounts` (the
 * same proxy `/sokningar` and the hero-chip use, CTO 2026-06-13 Variant B).
 *
 * Render contract: the row renders immediately with the real search name (no
 * mock, no row pop-in); when the count resolves AND there are new hits, the
 * "har N nya träffar" phrase is appended in place. Until then (and when the
 * count is 0 or the fetch degrades to `null`) only the name shows — never a
 * fabricated or misleading "0 new hits".
 */
export function SavedSearchNoticeText({
  searchId,
  name,
}: SavedSearchNoticeTextProps): ReactNode {
  const t = useTranslations("oversikt");
  const counts = useRecentSearchCounts(true);
  const newCount = counts?.get(searchId)?.newCount ?? 0;
  const bold = (chunks: ReactNode) => <b>{chunks}</b>;

  // aria-live so the lazily-appended "har N nya träffar" is announced to a
  // screen-reader user who has already read the row (design-reviewer #294): the
  // row is fully usable at first render with the no-count text; when the count
  // resolves with new hits the in-place text change is announced politely.
  return (
    <span aria-live="polite" aria-atomic="true">
      {newCount > 0
        ? t.rich("notices.savedSearchText", { name, count: newCount, b: bold })
        : t.rich("notices.savedSearchTextNoCount", { name, b: bold })}
    </span>
  );
}
