"use client";

import { useMemo, useState } from "react";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import { SavedJobAdRow } from "./saved-job-ad-row";

interface SavedJobAdListProps {
  items: ReadonlyArray<SavedJobAdDto>;
}

interface UnsaveError {
  jobAdId: string;
  message: string;
}

/**
 * F6 P5 Punkt 2 Del A — listan på `/sparade`. Optimistic delete-mönster
 * speglat från RecentSearchList (paritet ADR 0060 FE-arbetet). Server-side
 * revalidatePath körs i action; lokala state-flytt håller UI:t responsivt
 * mellan POST och re-render.
 */
export function SavedJobAdList({ items }: SavedJobAdListProps) {
  const [optimisticUnsavedIds, setOptimisticUnsavedIds] = useState<Set<string>>(
    () => new Set()
  );
  const [error, setError] = useState<UnsaveError | null>(null);

  const visibleItems = useMemo(
    () => items.filter((it) => !optimisticUnsavedIds.has(it.jobAdId)),
    [items, optimisticUnsavedIds]
  );

  function handleUnsaved(jobAdId: string) {
    setError(null);
    setOptimisticUnsavedIds((prev) => {
      const next = new Set(prev);
      next.add(jobAdId);
      return next;
    });
  }

  function handleUnsaveFailed(jobAdId: string, message: string) {
    setError({ jobAdId, message });
  }

  if (visibleItems.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">Inga sparade annonser</div>
        Hitta en annons under Jobb och välj <i>Spara</i> i annonsdetaljen. Den
        sparas här.
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
      <ul className="jp-jobs" aria-label="Sparade annonser">
        {visibleItems.map((item) => (
          <SavedJobAdRow
            key={item.id}
            item={item}
            onUnsaved={handleUnsaved}
            onUnsaveFailed={handleUnsaveFailed}
          />
        ))}
      </ul>
    </>
  );
}
