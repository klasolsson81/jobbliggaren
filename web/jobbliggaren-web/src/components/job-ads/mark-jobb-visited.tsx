"use client";

import { useEffect } from "react";
import { markJobbVisited } from "./use-last-seen-jobs";

/**
 * Osynlig client-island som markerar /jobb-listan som besökt vid mount.
 * Renderas en gång per sid-laddning i `/jobb/page.tsx` så lastSeen-baseline
 * uppdateras till "nu" — nästa sid-besök ser bara annonser publicerade
 * sedan dess som NY. INTE per kort (skulle ge 20+ skrivningar per render).
 *
 * `useEffect` med tom dep-array kör en gång efter första hydration och
 * körs inte om vid re-render. Effekten är write-only — den lästa snapshot:en
 * som driver NY-taggens render läses separat via `useLastSeenJobs` vid mount.
 */
export function MarkJobbVisited() {
  useEffect(() => {
    markJobbVisited();
  }, []);
  return null;
}
