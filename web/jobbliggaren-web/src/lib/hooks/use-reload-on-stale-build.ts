"use client";

// "use client": a hook that reads sessionStorage and replaces the document — browser-only.

import { useEffect, useState } from "react";
import {
  mayReloadForStaleBuild,
  stampAndReload,
} from "@/lib/stale-build/stale-build-reload";

/**
 * useReloadOnStaleBuild — called by every error boundary with the error it
 * caught. True while the document is being replaced, so the boundary renders
 * nothing instead of its error surface (ADR 0148 D6); false for every other
 * error, and when the guard refuses, so the surface shows.
 *
 * Decided ONCE per mount, in render (ADR 0148 D5): a lazy initialiser reads the
 * predicate and the stamp and nothing else, so the first commit already renders
 * nothing when a reload is coming and the error surface never flashes before
 * it; the effect's own stamp cannot flip a later render back to "refuse". The
 * effect writes the stamps and calls the seam; a write that fails flips the
 * state to the surface (fail-closed on the write, ADR 0148 D3).
 *
 * A call site that catches its action's rejection instead of letting it reach
 * a boundary calls the core's `reloadIfStaleBuild` — this hook cannot reach it.
 */
export function useReloadOnStaleBuild(error: unknown): boolean {
  const [reloading, setReloading] = useState(() => mayReloadForStaleBuild(error, Date.now()));

  useEffect(() => {
    if (!reloading || stampAndReload(Date.now())) return;
    // The write failed: flip to the surface. Scheduled, not synchronous, which
    // is the house form for a state change an effect must make
    // (react-hooks/set-state-in-effect; `job-ad-typeahead.tsx`'s idle reset).
    const id = setTimeout(() => setReloading(false), 0);
    return () => clearTimeout(id);
  }, [reloading]);

  return reloading;
}
