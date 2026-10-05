"use client";

// "use client": an effect hook that moves DOM focus — browser-only.

import { useEffect, useRef, type RefObject } from "react";

/**
 * useRouteModalFocus — focus for a modal that an intercepting route mounts in the
 * `@modal` slot (ADR 0053 Beslut 4).
 *
 * On mount it records the element that opened the modal and moves focus to the
 * close button; on unmount it returns focus to that element if it is still in the
 * document. Closing the modal is a history navigation, and Next applies no focus
 * on one, so without this the browser drops focus to `<body>`.
 *
 * Capture and initial focus share one effect: the opener must be read before the
 * close button takes focus. An element inside the panel is never the opener.
 */
export function useRouteModalFocus(
  panelRef: RefObject<HTMLElement | null>,
  closeRef: RefObject<HTMLElement | null>,
  resolveRestoredOpener?: (panel: HTMLElement | null) => HTMLElement | null,
): Readonly<RefObject<HTMLElement | null>> {
  const openerRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const panel = panelRef.current;
    const active = resolveRestoredOpener ? resolveRestoredOpener(panel) : document.activeElement;
    if (
      openerRef.current === null &&
      active instanceof HTMLElement &&
      active !== document.body &&
      !panelRef.current?.contains(active)
    ) {
      openerRef.current = active;
    }
    closeRef.current?.focus();
    return () => {
      const opener = resolveRestoredOpener ? resolveRestoredOpener(panel) : openerRef.current;
      if (opener?.isConnected) {
        opener.focus({ preventScroll: true });
      }
    };
  }, [panelRef, closeRef, resolveRestoredOpener]);
  return openerRef;
}
