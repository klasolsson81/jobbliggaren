"use client";

// "use client": reads the trigger's rect and the viewport in an effect, and listens to `resize`.

import { useEffect, useState } from "react";
import { placePanel, type PanelPlacement } from "@/lib/popover/placement";

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
 * Until the first measurement `style` holds the panel off screen, and `ready` is false.
 */
export function usePanelPosition(
  open: boolean,
  triggerRef: React.RefObject<HTMLElement | null>,
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
    measure();
    window.addEventListener("resize", measure);
    return () => window.removeEventListener("resize", measure);
  }, [open, triggerRef, preferredWidth]);

  const style: React.CSSProperties =
    placement === null
      ? { top: -9999, left: -9999, width: preferredWidth }
      : { top: placement.top, left: placement.left, width: placement.width };
  return { style, ready: placement !== null };
}
