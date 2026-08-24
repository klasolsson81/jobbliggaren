"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";

/**
 * #1092 — the ONE persistent live region for `/foretag/sok`'s load cycle (WCAG 4.1.3).
 *
 * The surface has two status messages in the criterion's own vocabulary: the "searching" sentence
 * at the start and the result count at the end. Both used to carry `role="status"` on the element
 * that renders the text, and both are mounted together with it — the skeleton IS the Suspense
 * fallback, and the count line exists only once the results do. ARIA22's test procedure requires
 * the opposite: the container must hold the role BEFORE the status message occurs. A region
 * injected with its content already in place is not reliably announced, and this repo has ruled on
 * that twice already — `jobb-hero-search.tsx`, and this folder's own `foretag-sok-searchbar.tsx`.
 *
 * So the region lives here, outside the Suspense boundary, empty at first paint, and the two
 * transient subtrees push their sentence into it. `useEffect` is what makes that correct rather
 * than incidental: it runs after the region is committed to the DOM, never in the same commit.
 *
 * Deliberately SEPARATE from the searchbar's region, which announces filter changes. Two regions
 * with two jobs, never one region with two writers: the searchbar sets its sentence at commit time
 * and this one at load time, roughly a frame apart, so sharing would let the load overwrite the
 * filter change before it is read.
 */
const AnnounceContext = createContext<((message: string) => void) | null>(null);

export function ForetagSokAnnouncer({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState("");
  return (
    <AnnounceContext.Provider value={setMessage}>
      {/* `aria-atomic` so a swapped sentence is read as one sentence rather than diffed. */}
      <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {message}
      </p>
      {children}
    </AnnounceContext.Provider>
  );
}

/**
 * Renders nothing; announces `message` through the surrounding region while it is mounted.
 *
 * The cleanup blank is load-bearing, not tidiness. React bails out on `Object.is`, so two identical
 * consecutive sentences are a single DOM mutation and the second is never announced — the trap
 * `foretag-sok-searchbar.tsx` declares as a known limitation. Clearing on unmount guarantees every
 * sentence is preceded by an empty region, and an empty live region utters nothing itself, so it
 * costs no announcement. Without it a search returning the same count as the previous one would be
 * silent at the moment its results arrive.
 *
 * Null context is tolerated rather than thrown on, so the skeleton stays renderable anywhere; both
 * of its current hosts provide one.
 */
export function Announce({ message }: { message: string }) {
  const setMessage = useContext(AnnounceContext);
  useEffect(() => {
    if (!setMessage) return;
    setMessage(message);
    return () => setMessage("");
  }, [message, setMessage]);
  return null;
}
