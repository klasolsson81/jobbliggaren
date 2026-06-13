"use client";

import { useEffect, useId, useRef } from "react";
import { useRouter } from "next/navigation";
import { X } from "lucide-react";

/**
 * JobAdModalShell — modal-chrome (scrim / ESC / scrim-klick / focus-trap /
 * focus-return / body-scroll-lock) runt en server-renderad `JobAdDetail`.
 *
 * Children är ett Server Component-träd (JobAdDetail) — chrome och innehåll
 * separerade enligt Next-docs ("By separating the <Modal> functionality
 * from the modal content … any content inside the modal … are Server
 * Components"). Stängning = `router.back()` så URL:en återställs och
 * intercepting-routens slot rensas (Next parallel/intercepting modal-
 * mönster).
 *
 * Focus-trap-idiomet återanvänder app-shell Drawer-implementationen
 * (querySelectorAll focusable + shift/Tab-wrap) — inget nytt mönster
 * (DRY, CLAUDE.md §9.1). ADR 0053 Beslut 4: a11y adderas, ej argumenteras
 * bort (role=dialog, aria-modal, focus-trap, ESC, focus-return,
 * scrim-klick stänger).
 */
export function JobAdModalShell({
  title,
  company,
  children,
}: {
  title: string;
  company: string;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const labelId = useId();

  const close = () => router.back();

  // Fokus in i modalen vid öppning + body-scroll-lock. Fokus-retur till
  // det element som öppnade modalen sköts av Next: router.back() återställer
  // föregående route och DOM-fokus-position i listan (soft-nav-historik).
  useEffect(() => {
    closeRef.current?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
    };
  }, []);

  // ESC stänger; focus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3).
  // Idiom speglat från app-shell Drawer (rad ~234-259).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        close();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])'
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
    // app-shell Drawer-mönstret (mount-livstid = modal-livstid).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div
      className="jp-modal-scrim"
      onClick={close}
      role="presentation"
    >
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
            <h2 id={labelId} className="jp-modal__title">
              {title}
            </h2>
            <p className="jp-modal__company">{company}</p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="jp-icon-btn"
            aria-label="Stäng"
            onClick={close}
          >
            <X size={20} aria-hidden="true" />
          </button>
        </header>
        {children}
      </div>
    </div>
  );
}
