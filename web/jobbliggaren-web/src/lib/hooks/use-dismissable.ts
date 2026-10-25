"use client";

import { useEffect, useRef } from "react";

/**
 * Portaled surfaces that belong to the open panel but live outside its DOM (portaled to
 * document.body), so an interaction there is not read as leaving the panel:
 * - `[data-radix-popper-content-wrapper]`: Popper surfaces (Select/Popover/Dropdown/Menu/HoverCard).
 *   Klas reported a bug on 2026-05-20: AddFollowUpForm's Kanal and RecordFollowUpOutcomeForm's Utfall
 *   could not be picked, because a SelectItem click inside a modal read as a click outside.
 * - `[data-slot="dialog-content"]`/`[data-slot="dialog-overlay"]`: a Radix MODAL Dialog (e.g. the
 *   InfoDialog "?") opened from INSIDE a dismissable popover. It is not Popper-based and is portaled
 *   via DialogPortal (#419 pt7). The `data-slot` attributes are set by `ui/dialog.tsx`, our own
 *   stable convention, sturdier than a Radix-internal attribute.
 */
export const PORTALED_LAYER_SELECTOR =
  '[data-radix-popper-content-wrapper], [data-slot="dialog-content"], [data-slot="dialog-overlay"]';

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
 *
 * `keepOpenOnEscape` (optional, #2198): when it returns true for an Escape, the panel neither
 * closes nor moves focus. Omitted, every Escape closes as before. Mousedown is not affected.
 */
export function useDismissable<
  TPanel extends HTMLElement = HTMLDivElement,
  TTrigger extends HTMLElement = HTMLButtonElement,
>(
  open: boolean,
  onClose: () => void,
  triggerRef: React.RefObject<TTrigger | null>,
  keepOpenOnEscape?: (event: KeyboardEvent) => boolean,
) {
  const ref = useRef<TPanel>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      const target = e.target as Element | null;
      // A click inside a portaled layer of the open panel is not a click outside it.
      if (target?.closest?.(PORTALED_LAYER_SELECTOR)) {
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
        // #419 pt7 — om FOKUS just nu ligger inuti en modal Radix Dialog (t.ex. en
        // InfoDialog "?" öppnad ovanpå popovern) äger DEN Escape (Radix stänger dialogen
        // själv); stäng då INTE popovern under — annars stänger ett enda Escape båda lagren.
        // Scopat till `document.activeElement.closest(...)` (INTE en dokument-bred
        // querySelector): en modal dialog trap:ar fokus, så detta är precis "en dialog är
        // det aktiva lagret". Det undviker att en ORELATERAD öppen dialog någon annanstans
        // sväljer en app-shell-popovers Escape (WCAG 2.1.2) — endast den dialog som faktiskt
        // har fokus defereras till. Dialogen bär `data-slot="dialog-content"` (ui/dialog.tsx).
        if (
          (document.activeElement as Element | null)?.closest?.(
            '[data-slot="dialog-content"]',
          )
        ) {
          return;
        }
        if (keepOpenOnEscape?.(e)) return;
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
  }, [open, onClose, triggerRef, keepOpenOnEscape]);

  return ref;
}
