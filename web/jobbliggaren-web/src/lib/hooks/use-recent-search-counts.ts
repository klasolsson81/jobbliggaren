"use client";

import { useEffect, useRef, useState } from "react";
import { z } from "zod";

/**
 * B (CTO-beslut 2026-06-13) — lat klient-hämtning av per-sökning-träffräknaren
 * för recent-search-ytorna. Ingen debounce — engångshämtning när `enabled` flippar true
 * (hero-chip när dropdownen öppnas / `/sokningar` on mount), aldrig blockerande
 * sidladdning.
 *
 * Återanvänder befintliga `GET /api/v1/me/recent-searches?includeCount=true`
 * via FE-proxyn `/api/me/recent-searches/counts` (noll backend-churn — den slow
 * N+1-COUNT:en, TD-94, sker off-critical-path). Vid timeout/fel → `null` →
 * konsumenten visar inga tal (samma slutläge som interim #77, ALDRIG falsk
 * "(0)").
 */
export interface RecentSearchCount {
  currentCount: number;
  newCount: number;
}

/**
 * `undefined`: an enabled hook's first fetch has not answered yet. `null`: the fetch failed, or the
 * hook has not fetched. A map: the counts by recent-search id.
 */
export type RecentSearchCounts = ReadonlyMap<string, RecentSearchCount> | null | undefined;

const countsResponseSchema = z.array(
  z.object({
    id: z.string(),
    currentCount: z.number().int().nonnegative(),
    newCount: z.number().int().nonnegative(),
  }),
);

export function useRecentSearchCounts(enabled: boolean): RecentSearchCounts {
  const [counts, setCounts] = useState<RecentSearchCounts>(enabled ? undefined : null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      // Behåll senaste counts under stängning (ingen flimmer-nollning);
      // nästa enabled-flipp re-hämtar.
      return;
    }

    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    (async () => {
      try {
        const res = await fetch("/api/me/recent-searches/counts", {
          signal: controller.signal,
        });
        if (!res.ok) {
          setCounts(null);
          return;
        }
        const parsed = countsResponseSchema.safeParse(await res.json());
        if (!parsed.success) {
          setCounts(null);
          return;
        }
        setCounts(
          new Map(
            parsed.data.map((c) => [
              c.id,
              { currentCount: c.currentCount, newCount: c.newCount },
            ]),
          ),
        );
      } catch {
        // Abort/nätverksfel → tyst degradering (ingen krasch, inga tal).
        if (!controller.signal.aborted) setCounts(null);
      }
    })();

    return () => {
      abortRef.current?.abort();
    };
  }, [enabled]);

  return counts;
}
