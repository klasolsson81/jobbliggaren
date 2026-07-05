import { BrandSpinner } from "@/components/brand/brand-spinner";

/**
 * DrawerLoadingShell — pure RSC loading chrome for the /ansokningar detail
 * DRAWER (#630 PR 6). Rendered by the @modal loading.tsx as the Suspense fallback
 * while the interceptor's server work (getServerSession + getApplicationById)
 * streams in, so a right-side surface paints instantly with a BrandSpinner +
 * Swedish status line, then the real client drawer swaps in ("open the empty
 * surface instantly + spinner", logo Fas 2 / ADR 0070; spinner-vs-skeleton
 * doctrine — a known-short formless wait → spinner).
 *
 * Mirrors ModalLoadingShell exactly, but on the `.jp-appdrawer` surface so the
 * fallback shape matches the drawer that replaces it (no centred-modal → right-
 * drawer jump). It renders at a DEFAULT top (`.jp-appdrawer--loading`); the real
 * client shell repositions near the click on mount. No client mechanics: the
 * transient fallback is not inert (no focus-trap; focus stays on the trigger),
 * so it deliberately omits aria-modal. BrandSpinner carries the role=status live
 * region; the visible status line is aria-hidden (not announced twice).
 */
export function DrawerLoadingShell({ statusText }: { statusText: string }) {
  return (
    <div className="jp-appdrawer-scrim" role="presentation">
      <div
        className="jp-appdrawer jp-appdrawer--loading"
        role="dialog"
        aria-busy="true"
        aria-label={statusText}
      >
        <div className="jp-modal-loading">
          <BrandSpinner size={48} label={statusText} />
          <p className="jp-modal-loading__text" aria-hidden="true">
            {statusText}
          </p>
        </div>
      </div>
    </div>
  );
}
