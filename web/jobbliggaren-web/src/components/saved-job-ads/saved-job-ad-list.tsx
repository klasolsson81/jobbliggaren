"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import { RemovalStatus, useRowRemoval } from "@/components/common/row-removal";
import { SavedJobAdRow } from "./saved-job-ad-row";

interface SavedJobAdListProps {
  items: ReadonlyArray<SavedJobAdDto>;
  /** The page's h1, which takes focus when the last row is removed. It carries `tabIndex={-1}`. */
  headingId: string;
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
export function SavedJobAdList({ items, headingId }: SavedJobAdListProps) {
  const t = useTranslations("jobads.saved");
  const [optimisticUnsavedIds, setOptimisticUnsavedIds] = useState<Set<string>>(
    () => new Set()
  );
  const [error, setError] = useState<UnsaveError | null>(null);

  const visibleItems = useMemo(
    () => items.filter((it) => !optimisticUnsavedIds.has(it.jobAdId)),
    [items, optimisticUnsavedIds]
  );
  const removal = useRowRemoval(
    visibleItems.map((it) => it.jobAdId),
    () => document.getElementById(headingId)
  );

  function handleUnsaved(jobAdId: string, receipt: string) {
    setError(null);
    removal.announce(receipt);
    setOptimisticUnsavedIds((prev) => {
      const next = new Set(prev);
      next.add(jobAdId);
      return next;
    });
  }

  function handleUnsaveFailed(jobAdId: string, message: string) {
    removal.failed(jobAdId);
    setError({ jobAdId, message });
  }

  return (
    <>
      <RemovalStatus receipt={removal.receipt} />
      {visibleItems.length === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          <p className="jp-empty__body">{t.rich("emptyBody", { i: (chunks) => <i>{chunks}</i> })}</p>
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
          <ul className="jp-jobs" aria-label={t("listLabel")}>
            {visibleItems.map((item) => (
              <SavedJobAdRow
                key={item.id}
                item={item}
                firstStopRef={removal.firstStopRef(item.jobAdId)}
                onUnsaveStart={removal.started}
                onUnsaved={handleUnsaved}
                onUnsaveFailed={handleUnsaveFailed}
              />
            ))}
          </ul>
        </>
      )}
    </>
  );
}
