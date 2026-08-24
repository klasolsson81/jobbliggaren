"use client";

import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";

/**
 * The live region a streamed surface announces its load cycle through (WCAG 4.1.3).
 *
 * A search surface has status messages in the criterion's own vocabulary: a "searching" sentence at
 * the start and an outcome at the end. The natural way to write them puts `role="status"` on the
 * element that renders the text — and that element is mounted together with it, because the
 * skeleton IS the Suspense fallback and the outcome exists only once the data does. ARIA22's test
 * procedure requires the opposite: the container must hold the role BEFORE the status message
 * occurs. A region injected with its content already in place is not reliably announced.
 *
 * The region therefore sits outside the Suspense boundary, empty, and the transient subtrees push
 * their sentence into it. `useEffect` is what makes that correct rather than incidental: passive
 * effects run after the mutation phase, so the region is committed to the DOM one commit before any
 * message reaches it — including on the route-level path, where the provider and the skeleton mount
 * together.
 *
 * On the in-page search path the node is persistent in the literal sense: the page holds one
 * provider outside a `key`-remounted boundary, so every search swaps content beneath the same
 * element. A cross-route navigation is NOT one node — `loading.tsx` and `page.tsx` each mount their
 * own provider, so the start and end sentences land in different regions. Both are empty when they
 * mount, which is what the criterion asks for; neither is one region spanning the whole cycle.
 *
 * ONE REGION, ONE JOB. A surface that also announces something else — a filter commit, a saved
 * search — gives that its own region rather than routing it through this one. Two regions with two
 * jobs, never one region with two writers: `/jobb`'s hero search and `/foretag/sok`'s searchbar
 * each keep their own for exactly that reason.
 *
 * Surface-neutral by construction: it takes no copy and knows no route. Every sentence is the
 * caller's, so nothing here needs to change when a surface changes what it says.
 */
const AnnounceContext = createContext<((message: string) => void) | null>(null);

export function Announcer({ children }: { children: ReactNode }) {
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
 * consecutive outcomes audible is the skeleton's own differing sentence between them.
 *
 * Null context is tolerated rather than thrown on, so a skeleton stays renderable anywhere. Every
 * host provides one, and the call sites are pinned in the hosts' own tests — without those,
 * removing a provider would leave the surface silent with every unit test green.
 */
export function Announce({ message }: { message: string }) {
  const setMessage = useContext(AnnounceContext);
  useEffect(() => {
    setMessage?.(message);
  }, [message, setMessage]);
  return null;
}
