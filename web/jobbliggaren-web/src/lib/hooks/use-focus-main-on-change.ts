"use client";

// "use client": an effect hook that moves DOM focus — browser-only.

import { useEffect, useRef } from "react";

// design-reviewer Blocker 1, PR #2180 (#2172).
export function useFocusMainOnChange(key: string | number) {
  const shown = useRef(key);

  useEffect(() => {
    if (shown.current === key) return;
    shown.current = key;
    document.getElementById("main")?.focus({ preventScroll: true });
  }, [key]);
}
