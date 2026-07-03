"use client";

import { useEffect, useId, useRef } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";

/**
 * ApplicationModalShell — modal-chrome (scrim / ESC / scrim-klick /
 * focus-trap / focus-return / body-scroll-lock) runt en server-renderad
 * `ApplicationDetail`. Speglar F3 JobAdModalShell exakt (samma
 * useDismissable/focus-trap/ESC/scrim-idiom) — medvetet INGEN
 * generalisering till delad ModalShell ännu: F3-shellen passar
 * title/company i headern, ansökan-shellen behöver titel + företag·#id.
 * En delad abstraktion infördes EJ för att undvika prematur generalisering
 * (Fowler "rule of three" — två kontexter räcker ej; markeras som
 * opportunistisk DRY-touch om en tredje modal tillkommer).
 *
 * Children är ett Server Component-träd (ApplicationDetail) — chrome och
 * innehåll separerade enligt Next-docs (Parallel/Intercepting Routes
 * §Modals). Stängning = `router.back()` så URL:en återställs och
 * intercepting-routens slot rensas. ADR 0053 Beslut 4 + ADR 0047 / HANDOVER
 * §9: a11y adderas, ej argumenteras bort (role=dialog, aria-modal,
 * aria-labelledby, focus-trap, ESC, focus-return, scrim-klick stänger).
 */
export function ApplicationModalShell({
  title,
  subtitle,
  mono,
  children,
  footer,
}: {
  title: string;
  subtitle: string;
  /** True när titeln ska renderas i mono (fallback-id, ingen kopplad annons). */
  mono?: boolean;
  children: React.ReactNode;
  /** Server-renderad footer (Stäng-knapp + ev. Återta ansökan). */
  footer?: React.ReactNode;
}) {
  const router = useRouter();
  const tUi = useTranslations("applications.ui");
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const labelId = useId();

  const close = () => router.back();

  // Fokus in i modalen vid öppning + body-scroll-lock. Fokus-retur till
  // utlösande element sköts av Next: router.back() återställer föregående
  // route och DOM-fokus-position i listan (soft-nav-historik). Identiskt
  // med F3 JobAdModalShell.
  useEffect(() => {
    closeRef.current?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
    };
  }, []);

  // ESC stänger; focus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3).
  // Idiom speglat från F3 JobAdModalShell / app-shell Drawer.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        // A nested Radix layer (the destructive-status confirm Dialog / "Återta
        // ansökan" Dialog, or a Select/Popover) handles Escape in the CAPTURE
        // phase and calls preventDefault() before this bubble-phase listener
        // runs. Yield to it — closing only the inner layer, not the whole modal
        // (#565: a bare Escape here used to router.back() the entire detail).
        if (e.defaultPrevented) return;
        e.preventDefault();
        close();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
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
    // close är stabil (router-bunden); medvetet tom dep-lista speglar
    // F3-mönstret (mount-livstid = modal-livstid).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="jp-modal-scrim" onClick={close} role="presentation">
      <div
        ref={panelRef}
        className="jp-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelId}
        aria-describedby="jp-modal-desc"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h2
              id={labelId}
              className={mono ? "jp-modal__title jp-mono" : "jp-modal__title"}
            >
              {title}
            </h2>
            <p className="jp-modal__company">{subtitle}</p>
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
        {children}
        <div className="jp-modal__foot">
          <span className="jp-modal__foot__spacer" />
          {footer}
          <button
            type="button"
            className="jp-btn jp-btn--secondary"
            onClick={close}
          >
            {tUi("modalShell.close")}
          </button>
        </div>
      </div>
    </div>
  );
}
