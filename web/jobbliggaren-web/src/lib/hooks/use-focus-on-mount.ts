"use client";

import { useEffect, useRef } from "react";

/**
 * useFocusOnMount — move keyboard focus to an element the moment it mounts.
 *
 * Written for the runtime error boundaries (`design-reviewer` Major 3 on PR
 * #1487, reported repo-wide rather than per-diff). When a throw is caught after
 * hydration, React swaps the boundary's subtree in place. That is NOT a
 * navigation, so the router tree never changes and Next's route announcer —
 * whose effect is keyed on `tree`, see
 * `next/dist/client/components/app-router-announcer.js` — does not re-run.
 * Nothing is announced. Meanwhile the element that had focus is unmounted with
 * the old subtree and focus falls to `<body>`, so a screen-reader user gets no
 * signal at all that the surface changed content (WCAG 4.1.3, 2.4.3).
 *
 * Moving focus to the boundary's `<h1>` announces the heading through the
 * ordinary focus path. That is also why the boundaries carry no `role="alert"`
 * on top: it would announce the same sentence a second time on the client path,
 * and once unprompted on the server-render path where there is no violation to
 * repair.
 *
 * The target must carry `tabIndex={-1}` — a heading is not focusable otherwise,
 * and `.focus()` on a non-focusable element is a silent no-op.
 */
export function useFocusOnMount<T extends HTMLElement>() {
  const ref = useRef<T>(null);

  useEffect(() => {
    ref.current?.focus();
  }, []);

  return ref;
}
