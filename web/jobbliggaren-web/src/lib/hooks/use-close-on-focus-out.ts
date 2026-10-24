"use client";

// "use client": listens to the open panel's focusout.

import { useEffect } from "react";
import type { PanelPlacement } from "@/lib/popover/placement";
import { PORTALED_LAYER_SELECTOR } from "./use-dismissable";

/**
 * Closes a non-modal panel when keyboard focus leaves it (#2212): Tab past its last control would
 * otherwise land on page content the open panel covers. It closes without moving focus, so the
 * focus stays where the user sent it.
 *
 * Focus that moves into a portaled layer the panel opened (an InfoDialog) stays inside the panel's
 * reach. A focusout with no next element (the focused node re-rendered away, or the window lost
 * focus) never closes it.
 */
export function useCloseOnFocusOut(
  open: boolean,
  panelRef: React.RefObject<HTMLElement | null>,
  triggerRef: React.RefObject<HTMLElement | null>,
  onClose: () => void,
  placement: PanelPlacement["placement"] | null,
) {
  useEffect(() => {
    const panel = panelRef.current;
    if (!open || !panel) return;
    // A press on the trigger moves focus to it before its click toggles the panel.
    let pressOnTrigger = false;
    const onMouseDown = (e: MouseEvent) => {
      pressOnTrigger = e.target instanceof Node && triggerRef.current?.contains(e.target) === true;
    };
    const onKeyDown = () => {
      pressOnTrigger = false;
    };
    const onFocusOut = (e: FocusEvent) => {
      const next = e.relatedTarget;
      if (!(next instanceof Element)) return;
      if (panel.contains(next) || next.closest(PORTALED_LAYER_SELECTOR)) return;
      if (triggerRef.current?.contains(next) && (placement !== "sheet" || pressOnTrigger)) return;
      onClose();
    };
    document.addEventListener("mousedown", onMouseDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    panel.addEventListener("focusout", onFocusOut);
    return () => {
      document.removeEventListener("mousedown", onMouseDown, true);
      document.removeEventListener("keydown", onKeyDown, true);
      panel.removeEventListener("focusout", onFocusOut);
    };
  }, [open, panelRef, triggerRef, onClose, placement]);
}
