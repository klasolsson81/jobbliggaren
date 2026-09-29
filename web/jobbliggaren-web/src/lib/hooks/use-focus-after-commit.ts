"use client";

// "use client": a layout-effect hook that moves DOM focus — browser-only.

import { useCallback, useLayoutEffect, useRef } from "react";

/**
 * For a control that removes itself when used — a chip's ⨯, a "Rensa" that hides once nothing is
 * left. The element that had focus is gone after the commit, and the browser drops focus to `<body>`
 * (WCAG 2.4.3). The caller names the new target when it acts; focus moves to it after the next
 * commit of the component that owns the hook, before paint. The target is resolved late because the
 * element it names may only exist after that commit.
 */
export function useFocusAfterCommit(): (
  target: () => HTMLElement | null | undefined
) => void {
  const pending = useRef<(() => HTMLElement | null | undefined) | null>(null);

  useLayoutEffect(() => {
    const resolve = pending.current;
    if (resolve === null) return;
    pending.current = null;
    resolve()?.focus();
  });

  return useCallback((target) => {
    pending.current = target;
  }, []);
}
