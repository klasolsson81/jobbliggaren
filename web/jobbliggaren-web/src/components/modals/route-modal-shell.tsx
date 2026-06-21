"use client";

import { useEffect, useId, useRef } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";

/**
 * RouteModalShell — generic modal-chrome (scrim / ESC / scrim-klick /
 * focus-trap / focus-return / body-scroll-lock) runt ett server-renderat
 * innehållsträd, för @modal-slottens intercepting routes (ADR 0053).
 *
 * Speglar exakt det idiom som F3 `JobAdModalShell` och F5
 * `ApplicationModalShell` redan etablerat — samma `.jp-modal*`-CSS, samma
 * a11y (role=dialog / aria-modal / aria-labelledby / focus-trap / ESC /
 * focus-return / scrim-klick stänger). Skillnaden mellan de två tidigare
 * shellsen låg ENBART i header-props (title/company resp. title+subtitle+id).
 * Med en tredje/fjärde modal-kontext (CV: Importera + Nytt) passeras Fowlers
 * "rule of three" — `ApplicationModalShell` flaggade själv detta som den
 * opportunistiska DRY-touchen. Denna shell är den generaliseringen; de två
 * äldre shellsen lämnas orörda (deras tester förblir gröna — låg risk).
 *
 * Children är ett Server Component-träd (CvUploadForm / CreateResumeForm är
 * klient-öar i trädet) — chrome och innehåll separeras enligt Next-docs
 * (Parallel/Intercepting Routes §Modals, verifierat node_modules/next/dist/
 * docs Next 16.2.x): "By separating the <Modal> functionality from the modal
 * content … any content inside the modal … are Server Components." Stängning
 * = `router.back()` så URL:en återställs och intercepting-routens slot rensas.
 *
 * Focus-trap-selektorn inkluderar formfält (input/select/textarea) — CV-
 * modalerna innehåller filväljare och textfält, till skillnad från jobb-
 * modalens länk/knapp-innehåll. Speglat från `ApplicationModalShell`.
 */
export function RouteModalShell({
  title,
  subtitle,
  description,
  children,
}: {
  title: string;
  /** Valfri rad under titeln i headern (t.ex. kort kontext). */
  subtitle?: string;
  /**
   * Valfri beskrivning som kopplas via aria-describedby. Sätts endast när
   * den finns → ingen danglande referens (a11y: aria-describedby pekar bara
   * på ett element som faktiskt renderas).
   */
  description?: string;
  children: React.ReactNode;
}) {
  const t = useTranslations("common");
  const router = useRouter();
  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const labelId = useId();
  const descId = useId();

  const close = () => router.back();

  // Fokus in i modalen vid öppning + body-scroll-lock. Fokus-retur till
  // utlösande element sköts av Next: router.back() återställer föregående
  // route och DOM-fokus-position (soft-nav-historik). Identiskt med F3/F5.
  useEffect(() => {
    closeRef.current?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
    };
  }, []);

  // ESC stänger; focus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3).
  // Idiom speglat från F5 ApplicationModalShell (inkluderar formfält i
  // selektorn) / app-shell Drawer.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
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
    // F3/F5-mönstret (mount-livstid = modal-livstid).
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
        aria-describedby={description ? descId : undefined}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h2 id={labelId} className="jp-modal__title">
              {title}
            </h2>
            {subtitle ? <p className="jp-modal__company">{subtitle}</p> : null}
          </div>
          <button
            ref={closeRef}
            type="button"
            className="jp-icon-btn"
            aria-label={t("modal.closeAriaLabel")}
            onClick={close}
          >
            <X size={20} aria-hidden="true" />
          </button>
        </header>
        {description ? (
          <p id={descId} className="sr-only">
            {description}
          </p>
        ) : null}
        {children}
      </div>
    </div>
  );
}
