"use client";

import { useEffect, useRef, useState } from "react";
import {
  facetCountsSchema,
  FACET_COUNTS_DEBOUNCE_MS,
  type FacetCounts,
  type FacetDimension,
} from "@/lib/dto/job-ads";

/**
 * ADR 0067 Beslut 4 (Fas E2c) — debouncad klient-hämtning av per-option
 * facet-counts mot `/api/jobb/facet-counts` (proxy → backend
 * FacetCountsPolicy 30/10s). Self-contained debounce ≥300 ms +
 * AbortController (typeahead-prejudikatet — INTE TanStack Query, ADR 0042-
 * notatet; §4.3 reglerar mutations/pollar, ej en popover-read).
 *
 * Returnerar `null` tills första lyckade svaret (eller vid degradering) —
 * konsumenten visar då inga counts; popovern förblir användbar
 * (counts är en hint, aldrig en förutsättning). `enabled=false` (stängd
 * popover) avbryter in-flight och behåller `null` — ingen bakgrunds-poll.
 */
export interface FacetCountsFilterState {
  occupationGroup: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  // ADR 0067 PR-3 — Klass 2 ingår i facett-filtret så varje dimensions facett
  // reflekterar de andra (backend exkluderar den facetterade dimensionen själv).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  q: string;
}

export function useFacetCounts(
  dimension: FacetDimension,
  filter: FacetCountsFilterState,
  enabled: boolean,
): FacetCounts | null {
  const [counts, setCounts] = useState<FacetCounts | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Stabil dependency-nyckel — listorna är nya referenser per render.
  // Värdena läses ur ref-spegeln nedan (synkron med nyckeln när effekten
  // kör) — ingen JSON-rundtur/cast (code-reviewer Minor 1 E2c).
  const filterKey = JSON.stringify([
    filter.occupationGroup,
    filter.municipality,
    filter.region,
    filter.employmentType,
    filter.worktimeExtent,
    filter.q,
  ]);
  const filterRef = useRef(filter);
  // Ref-spegeln uppdateras i en effect (react-hooks/refs förbjuder skrivning
  // under render). Deklarerad FÖRE fetch-effekten → körs först i samma
  // commit-fas, så fetch-effekten alltid läser färska värden.
  useEffect(() => {
    filterRef.current = filter;
  });

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort();
      // Behåll senaste counts under stängning (ingen flimmer-nollning);
      // nästa öppning re-fetchar via enabled-flippen.
      return;
    }

    const timer = setTimeout(async () => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const current = filterRef.current;
      const params = new URLSearchParams({ dimension });
      for (const v of current.occupationGroup)
        params.append("occupationGroup", v);
      for (const v of current.municipality) params.append("municipality", v);
      for (const v of current.region) params.append("region", v);
      for (const v of current.employmentType)
        params.append("employmentType", v);
      for (const v of current.worktimeExtent)
        params.append("worktimeExtent", v);
      const trimmedQ = current.q.trim();
      if (trimmedQ.length >= 2) params.set("q", trimmedQ);

      try {
        const res = await fetch(`/api/jobb/facet-counts?${params}`, {
          signal: controller.signal,
        });
        if (!res.ok) {
          setCounts(null);
          return;
        }
        const parsed = facetCountsSchema.safeParse(await res.json());
        setCounts(parsed.success ? parsed.data : null);
      } catch {
        // Abort/nätverksfel → tyst degradering (behåll/nolla utan krasch).
        if (!controller.signal.aborted) setCounts(null);
      }
    }, FACET_COUNTS_DEBOUNCE_MS);

    // Cleanup vid dep-change OCH unmount: rensa debounce-timern OCH avbryt
    // in-flight (typeahead-prejudikatets CTO-krav — annars kan ett gammalt
    // svar för FEL filter landa transient, och setState köras mot
    // avmonterad komponent; code-reviewer Major 2 E2c).
    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [dimension, filterKey, enabled]);

  return counts;
}
