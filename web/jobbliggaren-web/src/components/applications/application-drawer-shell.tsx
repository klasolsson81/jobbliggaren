"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { clampDrawerTop } from "@/lib/applications/drawer-position";
import {
  readDrawerAnchor,
  resetDrawerAnchor,
} from "@/components/applications/drawer-anchor";

const GUTTER = 16;

/**
 * ApplicationDrawerShell — right-side detail-drawer chrome (ADR 0092 D7, amends
 * ADR 0053 for /ansokningar only). Replaces ApplicationModalShell on the (app)
 * route: a 464px panel anchored vertically NEAR the click (design handoff §9),
 * not a centred modal. Wraps a server-rendered ApplicationDrawerBody as children
 * (RSC children in a client shell — the same seam ModalShell used; no function
 * crosses the @modal boundary).
 *
 * Reuses the modal-shell idiom verbatim: close = router.back() (soft-nav clears
 * the intercept slot + restores the URL), Escape with the #565 defaultPrevented
 * yield (a nested Radix layer that handled Escape in the capture phase wins),
 * manual Tab focus-trap, body-scroll-lock, role=dialog + aria-modal +
 * aria-labelledby + aria-describedby="jp-modal-desc" (the body keeps that id).
 *
 * Two deliberate divergences from the modal shell:
 *  1. Vertical position from the click anchor (clampDrawerTop), with an internal
 *     scroll body capped to the viewport — never a fixed top (handoff §9).
 *  2. MANUAL focus-return to the triggering row/card on close. The modal shell
 *     delegated focus-return to router.back()'s history restore; a drawer is not
 *     a full route pop, so we capture the trigger from the anchor and re-focus it
 *     on unmount (WCAG 2.4.3).
 *
 * PR 6 is strict read-mode: no Withdraw footer / status-mutation (→ PR 7).
 */
export function ApplicationDrawerShell({
  title,
  subtitle,
  mono,
  children,
}: {
  title: string;
  subtitle: string;
  /** True när titeln ska renderas i mono (fallback-id, ingen kopplad annons). */
  mono?: boolean;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const tUi = useTranslations("applications.ui");
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const triggerRef = useRef<HTMLElement | null>(null);
  const labelId = useId();

  const close = () => router.back();

  // Vertikal position från klick-ankaret (handoff §9), beräknad i en lazy
  // initializer (drawern mountas klient-side vid soft-nav → window finns) så
  // panelen aldrig blinkar vid en default-position och ingen setState-i-effect
  // behövs. clampDrawerTop håller den i viewporten; body kapar egen höjd +
  // scrollar. Ankare saknas (t.ex. programmatisk nav) → ~en tredjedel ned;
  // ev. SSR-pass (window undefined) → gutter.
  const [top] = useState<number>(() => {
    if (typeof window === "undefined") return GUTTER;
    const anchor = readDrawerAnchor();
    const clientY = anchor?.clientY ?? window.innerHeight * 0.3;
    return clampDrawerTop(clientY, window.innerHeight, { gutter: GUTTER });
  });

  // Fokus in i drawern vid öppning + body-scroll-lock + fånga utlösaren för
  // fokus-RETUR (drawer är inte en full route-pop → router.back() återställer
  // inte DOM-fokus som modalen förlitade sig på). Ankaret nollställs vid unmount.
  useEffect(() => {
    triggerRef.current = readDrawerAnchor()?.trigger ?? null;
    closeRef.current?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
      triggerRef.current?.focus();
      resetDrawerAnchor();
    };
  }, []);

  // ESC stänger; focus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3). Idiom
  // speglat från ApplicationModalShell (inkl. #565 capture-fas-yield).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        // A nested Radix layer (a Select/Popover inside the body) may handle
        // Escape in the CAPTURE phase and preventDefault() before this
        // bubble-phase listener — yield to it, closing only the inner layer
        // (#565). PR 6 has no status-confirm Dialog, but keeping the yield makes
        // the shell safe for any future nested layer.
        if (e.defaultPrevented) return;
        e.preventDefault();
        close();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );
      if (focusable.length === 0) return;
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
    // close är stabil (router-bunden); tom dep-lista = mount-livstid = drawer-livstid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div
      className="jp-appdrawer-scrim"
      onClick={close}
      role="presentation"
    >
      <div
        ref={panelRef}
        className="jp-appdrawer"
        style={{
          top: `${top}px`,
          maxHeight: `calc(100dvh - ${top}px - ${GUTTER}px)`,
        }}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelId}
        aria-describedby="jp-modal-desc"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="jp-appdrawer__head">
          <div className="jp-appdrawer__heading">
            <h2
              id={labelId}
              className={
                mono ? "jp-appdrawer__title jp-mono" : "jp-appdrawer__title"
              }
            >
              {title}
            </h2>
            <p className="jp-appdrawer__subtitle">{subtitle}</p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="jp-icon-btn"
            aria-label={tUi("modalShell.closeAriaLabel")}
            onClick={close}
          >
            <X size={20} aria-hidden="true" />
          </button>
        </header>
        <div className="jp-appdrawer__body">{children}</div>
      </div>
    </div>
  );
}
