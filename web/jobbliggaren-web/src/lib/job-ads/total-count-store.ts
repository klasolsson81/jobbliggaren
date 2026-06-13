"use client";

import { useSyncExternalStore } from "react";

/**
 * Fas E2c (CTO VAL 2) — delning av list-svarets totalCount mellan två
 * client-öar i samma sidträd: `JobbResultsToolbar` (inne i resultat-
 * Suspensen, äger talet via RSC-fetchen) publicerar; popoverns
 * "Visa N annonser"-knapp (hero-ön, utanför Suspensen) prenumererar.
 *
 * Modul-lokal store + useSyncExternalStore i stället för Context:
 * öarna saknar gemensam client-förälder (hero renderas synkront, toolbaren
 * streamas — F6 P4 B1), och att lyfta state skulle bryta streaming-
 * arkitekturen. Talet ägs av PagedResult.TotalCount (SPOT — ingen extra
 * request, ingen summering av facett-counts som vore semantiskt fel).
 * `null` = inget list-svar publicerat ännu (knappen visar då "Visa
 * annonser" utan tal — ärlig degradering).
 */
let currentTotalCount: number | null = null;
const listeners = new Set<() => void>();

export function publishTotalCount(totalCount: number): void {
  if (currentTotalCount === totalCount) return;
  currentTotalCount = totalCount;
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getSnapshot(): number | null {
  return currentTotalCount;
}

// Server-snapshot: hero-ön SSR:as innan något list-svar publicerats.
function getServerSnapshot(): number | null {
  return null;
}

export function useTotalCount(): number | null {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}

// Endast för tester — återställer modul-state mellan fall.
export function resetTotalCountForTest(): void {
  currentTotalCount = null;
  listeners.clear();
}
