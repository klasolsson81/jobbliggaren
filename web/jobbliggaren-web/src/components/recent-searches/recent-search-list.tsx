"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { useRecentSearchCounts } from "@/lib/hooks/use-recent-search-counts";
import { RemovalStatus, useRowRemoval } from "@/components/common/row-removal";
import { hasQuickLinkRoom } from "@/lib/job-ads/quick-links";
import { RecentSearchRow } from "./recent-search-row";

interface RecentSearchListProps {
  items: ReadonlyArray<RecentJobSearchDto>;
  /** The page's h1, which takes focus when the last row is removed. It carries `tabIndex={-1}`. */
  headingId: string;
  /** ADR 0188 (#2265): whether the junior quick link is on, which counts toward the cap of three. */
  juniorQuickLinkOn: boolean;
}

interface DeleteError {
  id: string;
  message: string;
}

export function RecentSearchList({ items, headingId, juniorQuickLinkOn }: RecentSearchListProps) {
  const t = useTranslations("jobads.recent");
  const tq = useTranslations("jobads.quickLinks");
  const [optimisticDeletedIds, setOptimisticDeletedIds] = useState<Set<string>>(
    () => new Set()
  );
  const [error, setError] = useState<DeleteError | null>(null);
  // Lat-hämtad träffräknare on mount (B, CTO 2026-06-13) — off-critical-path.
  const counts = useRecentSearchCounts(true);

  const visibleItems = useMemo(
    () => items.filter((it) => !optimisticDeletedIds.has(it.id)),
    [items, optimisticDeletedIds]
  );
  const removal = useRowRemoval(
    visibleItems.map((it) => it.id),
    () => document.getElementById(headingId)
  );

  function handleDeleted(id: string, receipt: string) {
    setError(null);
    removal.announce(receipt);
    setOptimisticDeletedIds((prev) => {
      const next = new Set(prev);
      next.add(id);
      return next;
    });
  }

  function handleDeleteFailed(id: string, message: string) {
    removal.failed(id);
    setError({ id, message });
  }

  function handleQuickLinkChanged(receipt: string) {
    setError(null);
    removal.announce(receipt);
  }

  function handleQuickLinkFailed(id: string, message: string) {
    setError({ id, message });
  }

  const canAddQuickLink = hasQuickLinkRoom(
    juniorQuickLinkOn,
    visibleItems.filter((it) => it.isQuickLink).length
  );

  return (
    <>
      <RemovalStatus receipt={removal.receipt} />
      {visibleItems.length === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          <p className="jp-empty__body">{t("emptyBody")}</p>
        </div>
      ) : (
        <>
          {error && (
            <div
              role="alert"
              className="rounded-md border border-danger-600/30 bg-danger-50 px-4 py-3 mb-3 text-danger-700 text-body-sm"
            >
              {error.message}
            </div>
          )}
          {!canAddQuickLink && visibleItems.some((it) => !it.isQuickLink) && (
            <p className="mb-3 text-body-sm text-text-primary">{tq("full")}</p>
          )}
          <ul className="jp-jobs" aria-label={t("listLabel")}>
            {visibleItems.map((item) => (
              <RecentSearchRow
                key={item.id}
                item={item}
                count={counts?.get(item.id)}
                countsPending={counts === undefined}
                firstStopRef={removal.firstStopRef(item.id)}
                onDeleteStart={removal.started}
                onDeleted={handleDeleted}
                onDeleteFailed={handleDeleteFailed}
                canAddQuickLink={canAddQuickLink}
                onQuickLinkChanged={handleQuickLinkChanged}
                onQuickLinkFailed={handleQuickLinkFailed}
              />
            ))}
          </ul>
        </>
      )}
    </>
  );
}
