"use client";

// "use client": an effect hook that moves DOM focus — browser-only.

import { useEffect } from "react";

/**
 * useFocusMainOnUnmount — when an error boundary's surface leaves after a
 * successful "Försök igen", put keyboard focus on the page's landmark.
 *
 * `retry()` is `startTransition(() => { router.refresh(); reset(); })`
 * (`next/dist/client/components/error-boundary.js`). When the refreshed
 * segment renders, the surface unmounts together with the button that had
 * focus, and the browser drops focus to `<body>`. A refresh carries no focus
 * or scroll intent, and the route announcer is keyed on the router tree,
 * which a refresh does not change — so nothing is announced and a keyboard
 * user is left at the top of the document (design-reviewer Major 4 on
 * #1949; the mirror of #1487 Major 3, which `useFocusOnMount` repaired in
 * the other direction).
 *
 * The cleanup runs on the boundary's unmount and moves focus on the next
 * animation frame, once the new subtree has committed, to
 * `<main id="main" tabIndex={-1}>` — every group's shell or page owns one.
 * It moves focus ONLY when focus is on `<body>`: a failed retry remounts the
 * surface and its `<h1>` takes focus through `useFocusOnMount`.
 */
export function useFocusMainOnUnmount() {
  useEffect(() => {
    return () => {
      requestAnimationFrame(() => {
        if (document.activeElement !== document.body) return;
        document.getElementById("main")?.focus({ preventScroll: true });
      });
    };
  }, []);
}
