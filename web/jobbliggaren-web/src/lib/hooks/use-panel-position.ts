"use client";

// "use client": reads the trigger's rect and the viewport in an effect, and listens to `resize`.

import { useEffect, useState } from "react";
import { FOCUSABLE_SELECTOR } from "@/lib/popover/focusable";
import { PANEL_GAP, placePanel, type PanelPlacement } from "@/lib/popover/placement";

/**
 * The inline position of a `.jp-popover` panel anchored to its trigger: one home for every filter
 * popover shell (#2212). It measures in an effect, because refs are not read during render, and
 * hands `placePanel` the trigger's rect and the document element's client size, so the panel stays
 * inside the viewport.
 *
 * Precondition: the panel's containing block is the initial containing block and no scroll
 * container sits between the trigger and the document. Under it a page scroll moves the trigger and
 * the page offset by the same amount, so only a `resize` re-measures.
 *
 * Once per opening, before the first placement: when the panel's first focusable element would end
 * below the viewport, the page scrolls the trigger to the top of the scroll padding, so the pill
 * stays in view as the panel's anchor. Until the panel is placed, `style` holds it off screen and
 * `ready` is false.
 */
export function usePanelPosition(
  open: boolean,
  triggerRef: React.RefObject<HTMLElement | null>,
  panelRef: React.RefObject<HTMLElement | null>,
  preferredWidth: number,
): { style: React.CSSProperties; ready: boolean } {
  const [placement, setPlacement] = useState<PanelPlacement | null>(null);

  useEffect(() => {
    const trigger = triggerRef.current;
    if (!open || !trigger) {
      setPlacement(null);
      return;
    }
    const measure = () => {
      const r = trigger.getBoundingClientRect();
      const root = document.documentElement;
      const next = placePanel(
        { left: r.left, bottom: r.bottom },
        {
          clientWidth: root.clientWidth,
          clientHeight: root.clientHeight,
          scrollX: window.scrollX,
          scrollY: window.scrollY,
        },
        preferredWidth,
      );
      setPlacement((prev) =>
        prev !== null &&
        prev.top === next.top &&
        prev.left === next.left &&
        prev.width === next.width
          ? prev
          : next,
      );
    };
    if (firstStopOpensBelowFold(panelRef.current, trigger)) {
      trigger.scrollIntoView?.({ block: "start" });
    }
    measure();
    window.addEventListener("resize", measure);
    return () => window.removeEventListener("resize", measure);
  }, [open, triggerRef, panelRef, preferredWidth]);

  const style: React.CSSProperties =
    placement === null
      ? { top: -9999, left: -9999, width: preferredWidth }
      : { top: placement.top, left: placement.left, width: placement.width };
  return { style, ready: placement !== null };
}

/**
 * Read while the panel still sits off screen: its first stop's distance below the panel's top is
 * the same wherever the panel goes, so the placed bottom edge is the trigger's plus the gap plus it.
 */
function firstStopOpensBelowFold(panel: HTMLElement | null, trigger: HTMLElement): boolean {
  const first = panel?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
  const clientHeight = document.documentElement.clientHeight;
  if (!panel || !first || clientHeight <= 0) return false;
  const depth = first.getBoundingClientRect().bottom - panel.getBoundingClientRect().top;
  return trigger.getBoundingClientRect().bottom + PANEL_GAP + depth > clientHeight;
}
