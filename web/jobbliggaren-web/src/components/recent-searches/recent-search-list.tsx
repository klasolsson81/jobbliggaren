"use client";

import { useMemo, useState } from "react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { useRecentSearchCounts } from "@/lib/hooks/use-recent-search-counts";
import { RecentSearchRow } from "./recent-search-row";

interface RecentSearchListProps {
  items: ReadonlyArray<RecentJobSearchDto>;
}

interface DeleteError {
  id: string;
  message: string;
}

export function RecentSearchList({ items }: RecentSearchListProps) {
  const [optimisticDeletedIds, setOptimisticDeletedIds] = useState<Set<string>>(
    () => new Set()
  );
  const [error, setError] = useState<DeleteError | null>(null);
  // Lat-hämtad träffräknare on mount (B, CTO 2026-06-13) — off-critical-path,
  // graceful null. Listan visas direkt; talen "poppar in" när de laddats.
  const counts = useRecentSearchCounts(true);

  const visibleItems = useMemo(
    () => items.filter((it) => !optimisticDeletedIds.has(it.id)),
    [items, optimisticDeletedIds]
  );

  function handleDeleted(id: string) {
    setError(null);
    setOptimisticDeletedIds((prev) => {
      const next = new Set(prev);
      next.add(id);
      return next;
    });
  }

  function handleDeleteFailed(id: string, message: string) {
    setError({ id, message });
  }

  if (visibleItems.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">Inga senaste sökningar</div>
        Gör en sökning under Jobb. Den sparas här automatiskt.
      </div>
    );
  }

  return (
    <>
      {error && (
        <div
          role="alert"
          className="rounded-md border border-danger-600/30 bg-danger-50 px-4 py-3 mb-3 text-danger-700 text-body-sm"
        >
          {error.message}
        </div>
      )}
      <ul className="jp-jobs" aria-label="Senaste sökningar">
        {visibleItems.map((item) => (
          <RecentSearchRow
            key={item.id}
            item={item}
            count={counts?.get(item.id)}
            onDeleted={handleDeleted}
            onDeleteFailed={handleDeleteFailed}
          />
        ))}
      </ul>
    </>
  );
}
