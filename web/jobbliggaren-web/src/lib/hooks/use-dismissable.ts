"use client";

import { useEffect, useRef } from "react";

/**
 * Delat dismiss-idiom för popover/dropdown-ytor (DRY — CLAUDE.md §9.1).
 * Extraherat verbatim ur app-shell.tsx (NotificationsBell/UserMenu) så att
 * F4:s hero-filter-popovers återanvänder exakt samma stängningssemantik:
 *
 * - Klick utanför panelen (och utanför triggern) stänger.
 * - Escape stänger och återför fokus till triggern (WCAG 2.4.3 — fokus får
 *   inte fastna i en stängd yta; jobbliggaren-design-a11y / ADR 0047).
 *
 * `triggerRef` är generisk över HTMLElement-subtyp (app-shell använder
 * `<button>`, hero-pillen likaså, men typen låses inte till button så
 * andra triggers kan återanvända idiomet utan cast).
 */
export function useDismissable<
  TPanel extends HTMLElement = HTMLDivElement,
  TTrigger extends HTMLElement = HTMLButtonElement,
>(
  open: boolean,
  onClose: () => void,
  triggerRef: React.RefObject<TTrigger | null>,
) {
  const ref = useRef<TPanel>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      const target = e.target as Element | null;
      // Ignorera klick inuti Radix-portalerade ytor (Select/Popover/Dropdown
      // /Menu/HoverCard via Popper) — de portaleras till document.body utanför
      // panel-DOM:en, så ett SelectItem-klick inuti en modal skulle annars
      // läsas som "klick utanför" och stänga modalen (Klas-rapporterad bug
      // 2026-05-20: AddFollowUpForm Kanal + RecordFollowUpOutcomeForm Utfall
      // gick ej att välja — samma rot, alla portalerade dropdowns inuti
      // modaler påverkades).
      if (
        target?.closest?.(
          '[data-radix-popper-content-wrapper], [data-radix-portal]',
        )
      ) {
        return;
      }
      if (
        ref.current &&
        !ref.current.contains(target as Node) &&
        triggerRef.current &&
        !triggerRef.current.contains(target as Node)
      ) {
        onClose();
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [open, onClose, triggerRef]);

  return ref;
}
