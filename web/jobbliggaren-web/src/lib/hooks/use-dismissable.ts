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
      // Ignorera klick inuti portalerade ytor som logiskt hör till den öppna
      // panelen men lever utanför dess DOM (portalerade till document.body), så
      // ett klick där inte miss-läses som "klick utanför" och stänger panelen:
      // - `[data-radix-popper-content-wrapper]`: Popper-ytor (Select/Popover/
      //   Dropdown/Menu/HoverCard). Klas-rapporterad bug 2026-05-20: AddFollowUpForm
      //   Kanal + RecordFollowUpOutcomeForm Utfall gick ej att välja — SelectItem-
      //   klick inuti en modal lästes som utanför-klick.
      // - `[data-slot="dialog-content"]`/`[data-slot="dialog-overlay"]`: en Radix
      //   MODAL Dialog (t.ex. InfoDialog "?") öppnad från INUTI en dismissable
      //   popover. Dialogen är INTE Popper-baserad (ingen popper-content-wrapper)
      //   och portaleras via DialogPortal, så utan detta skulle första mousedown
      //   inuti dialogen/overlayen stänga popovern under modalen (#419 pt7). De
      //   `data-slot`-attributen sätts av `ui/dialog.tsx` (våra egna stabila
      //   konventionsattribut, robustare än ett Radix-internt data-attribut).
      //   (Den tidigare `[data-radix-portal]`-token togs bort — den finns inte i
      //   den installerade Radix-buildens output och matchade därför noll.)
      if (
        target?.closest?.(
          '[data-radix-popper-content-wrapper], [data-slot="dialog-content"], [data-slot="dialog-overlay"]',
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
