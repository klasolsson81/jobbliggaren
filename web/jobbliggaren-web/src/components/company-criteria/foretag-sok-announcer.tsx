"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";

/**
 * #1092 — the live region `/foretag/sok`'s load cycle announces through (WCAG 4.1.3).
 *
 * The surface has two status messages in the criterion's own vocabulary: the "searching" sentence
 * at the start and the result count at the end. Both used to carry `role="status"` on the element
 * that renders the text, and both are mounted together with it — the skeleton IS the Suspense
 * fallback, and the count line exists only once the results do. ARIA22's test procedure requires
 * the opposite: the container must hold the role BEFORE the status message occurs. A region
 * injected with its content already in place is not reliably announced, and this repo has ruled on
 * that twice already — `jobb-hero-search.tsx`, and this folder's own `foretag-sok-searchbar.tsx`.
 *
 * The region therefore sits outside the Suspense boundary, empty, and the transient subtrees push
 * their sentence into it. `useEffect` is what makes that correct rather than incidental: passive
 * effects run after the mutation phase, so the region is committed to the DOM one commit before any
 * message reaches it — including on the route-level path, where the provider and the skeleton mount
 * together.
 *
 * On the client-search path the node is persistent in the literal sense: `page.tsx` holds one
 * provider outside a `key`-remounted boundary, so every search swaps content beneath the same
 * element. A cross-route navigation is NOT one node — `loading.tsx` and `page.tsx` each mount their
 * own provider, so the start and end sentences land in different regions. Both are empty when they
 * mount, which is what the criterion asks for; neither is one region spanning the whole cycle.
 *
 * Deliberately SEPARATE from the searchbar's region, which announces filter changes. Two regions
 * with two jobs, never one region with two writers: the searchbar sets its sentence when a filter
 * commits and this one when the load resolves, and React would batch away the intermediate state if
 * they shared, losing the filter sentence before it could be read.
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
 * EVERY branch that ends a load sets a sentence of its own — results, empty state and the error
 * shell alike. There is no clearing on unmount, and adding one would not help: React runs passive
 * unmount effects and passive mount effects in the same flush, so a blank written by a departing
 * subtree is batched away by the arriving one and never reaches the DOM. What keeps two identical
 * consecutive counts audible is the skeleton's own differing sentence between them.
 *
 * Null context is tolerated rather than thrown on, so the skeleton stays renderable anywhere. Both
 * of its hosts provide one, and the call sites are pinned in `page.test.tsx` / `loading.test.tsx` —
 * without those, removing a provider would leave the surface silent with every unit test green.
 */
export function Announce({ message }: { message: string }) {
  const setMessage = useContext(AnnounceContext);
  useEffect(() => {
    setMessage?.(message);
  }, [message, setMessage]);
  return null;
}
