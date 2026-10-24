"use client";

// "use client": listens to the open panel's focusout.

import { useEffect } from "react";
import { PORTALED_LAYER_SELECTOR } from "./use-dismissable";

/**
 * Closes a non-modal panel when keyboard focus leaves it (#2212): Tab past its last control would
 * otherwise land on page content the open panel covers. It closes without moving focus, so the
 * focus stays where the user sent it.
 *
 * Focus that moves to the trigger, or into a portaled layer the panel opened (an InfoDialog), stays
 * inside the panel's reach. A focusout with no next element (the focused node re-rendered away, or
 * the window lost focus) never closes it.
 */
export function useCloseOnFocusOut(
  open: boolean,
  panelRef: React.RefObject<HTMLElement | null>,
  triggerRef: React.RefObject<HTMLElement | null>,
  onClose: () => void,
) {
  useEffect(() => {
    const panel = panelRef.current;
    if (!open || !panel) return;
    const onFocusOut = (e: FocusEvent) => {
      const next = e.relatedTarget;
      if (!(next instanceof Element)) return;
      if (panel.contains(next) || triggerRef.current?.contains(next) || next.closest(PORTALED_LAYER_SELECTOR)) {
        return;
      }
      onClose();
    };
    panel.addEventListener("focusout", onFocusOut);
    return () => panel.removeEventListener("focusout", onFocusOut);
  }, [open, panelRef, triggerRef, onClose]);
}
