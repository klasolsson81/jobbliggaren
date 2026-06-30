"use client";

import { useEffect, useState } from "react";

/**
 * Delad position-mätning för enkelkolumns-popovers/paneler (DRY — CLAUDE.md
 * §9.1). Extraherad verbatim ur `JobbKlass2Panel.usePanelPosition` så att de
 * nya toolbar-popovrarna (Matchning/Status, #408) mäter sin position på exakt
 * samma sätt: under triggern, 8px luft, vänster-justerad mot triggern.
 *
 * Position härleds ur triggerns ref INNE I en effect (refs får inte läsas under
 * render). Mäts om vid resize/scroll så panelen följer triggern. `null` innan
 * mätningen (panelen positioneras off-screen tills `pos` finns).
 */
export function usePanelPosition(
  open: boolean,
  triggerRef: React.RefObject<HTMLButtonElement | null>,
) {
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);

  useEffect(() => {
    const trigger = triggerRef.current;
    if (!open || !trigger) {
      setPos(null);
      return;
    }
    const measure = () => {
      const r = trigger.getBoundingClientRect();
      setPos({
        top: r.bottom + 8 + window.scrollY,
        left: r.left + window.scrollX,
      });
    };
    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [open, triggerRef]);

  return pos;
}
