"use client";

// "use client": a layout-effect hook that moves DOM focus, and the live region it reports through —
// both browser-only.

import { useCallback, useLayoutEffect, useRef, useState } from "react";
import type { RefCallback } from "react";

export interface RemovalReceipt {
  readonly seq: number;
  readonly text: string;
}

export interface RowRemoval {
  /** The ref for a row's first focus stop. One stable callback per id. */
  readonly firstStopRef: (id: string) => RefCallback<HTMLElement>;
  /** Called when the user starts removing a row: the point a confirmation step would precede. */
  readonly started: (id: string) => void;
  /** The removal failed and the row stays, so it can take focus again. */
  readonly failed: (id: string) => void;
  readonly announce: (text: string) => void;
  readonly receipt: RemovalReceipt;
}

/**
 * What follows when a list loses a row (WCAG 2.1 SC 2.4.3, 4.1.3). The row's remove control unmounts
 * with it and the browser drops focus to `<body>`.
 *
 * The trigger is the commit in which a started row's id is no longer rendered, whichever update removed
 * it: the owner's optimistic state or the refreshed server payload. Commits
 * that still render the id do nothing, which is why `useFocusAfterCommit` (resolved on the next commit)
 * cannot carry this. Focus then goes to the next row's first stop, else the previous row's, else
 * `fallback()`, and only when it was lost: a user who has moved on keeps their place.
 */
export function useRowRemoval(
  ids: ReadonlyArray<string>,
  fallback: () => HTMLElement | null
): RowRemoval {
  const stops = useRef(new Map<string, HTMLElement>());
  const refs = useRef(new Map<string, RefCallback<HTMLElement>>());
  const intents = useRef(new Set<string>());
  const committed = useRef<ReadonlyArray<string>>(ids);
  const [receipt, setReceipt] = useState<RemovalReceipt>({ seq: 0, text: "" });

  useLayoutEffect(() => {
    const previous = committed.current;
    committed.current = ids;
    const present = new Set(ids);
    const gone = [...intents.current].filter((id) => !present.has(id));
    if (gone.length === 0) return;
    for (const id of gone) intents.current.delete(id);
    const active = document.activeElement;
    if (active !== null && active !== document.body) return;
    // The most recently started removal is the anchor: `started` re-inserts, so it is last in the set.
    const at = previous.indexOf(gone[gone.length - 1] ?? "");
    const usable = (id: string) =>
      present.has(id) && !intents.current.has(id) && stops.current.has(id);
    const next =
      previous.slice(at + 1).find(usable) ??
      previous.slice(0, Math.max(at, 0)).findLast(usable);
    (next === undefined ? fallback() : stops.current.get(next))?.focus();
  });

  const firstStopRef = useCallback((id: string) => {
    const cached = refs.current.get(id);
    if (cached) return cached;
    const ref: RefCallback<HTMLElement> = (el) => {
      if (el) stops.current.set(id, el);
      else stops.current.delete(id);
    };
    refs.current.set(id, ref);
    return ref;
  }, []);

  const started = useCallback((id: string) => {
    intents.current.delete(id);
    intents.current.add(id);
  }, []);

  const failed = useCallback((id: string) => {
    intents.current.delete(id);
  }, []);

  const announce = useCallback((text: string) => {
    setReceipt((r) => ({ seq: r.seq + 1, text }));
  }, []);

  return { firstStopRef, started, failed, announce, receipt };
}

/**
 * The receipt's own region (announcer.tsx: one region, one job). The owner renders it at the same tree
 * position whether or not its list is empty, so it is one node from first paint, empty, before any
 * message reaches it (ARIA22). Each receipt is a new keyed node, so the same sentence twice — two erased
 * ads — is still an addition.
 */
export function RemovalStatus({ receipt }: { readonly receipt: RemovalReceipt }) {
  return (
    <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
      {receipt.seq > 0 && <span key={receipt.seq}>{receipt.text}</span>}
    </p>
  );
}
