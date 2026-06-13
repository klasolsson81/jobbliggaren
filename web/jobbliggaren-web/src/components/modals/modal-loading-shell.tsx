import { BrandSpinner } from "@/components/brand/brand-spinner";

/**
 * ModalLoadingShell — pure RSC loading chrome for the @modal-slot intercepting
 * routes. Rendered by each `loading.tsx` as the Suspense fallback while the
 * server component (JobAdDetail / ApplicationDetail) streams in — so the modal
 * surface paints instantly with a BrandSpinner + Swedish status line, then the
 * real content swaps in ("open the empty surface instantly + spinner", logo
 * Fas 2 / ADR 0070, spinner-vs-skeleton doctrine in DESIGN.md §11).
 *
 * Deliberately NO client mechanics (senior-cto-advisor Variant A, 2026-06-13):
 * the fallback shows for a known-short (<1–2s) wait until the real client shell
 * (JobAdModalShell / ApplicationModalShell — ESC / scrim-click / focus-trap /
 * body-scroll-lock) mounts with the resolved data. Keeping the fallback as a
 * Server Component lets it stream in the static shell with no hydration, which
 * is the whole point of "paints instantly". It reuses the existing
 * `jp-modal-scrim` / `jp-modal` CSS surface — no new tokens, nothing to keep in
 * sync with the two content shells. Doctrine (when spinner vs skeleton): see the
 * `jobbpilot-design-components` skill, "Spinner (BrandSpinner)" section.
 *
 * a11y: the panel is role=dialog + aria-modal + aria-busy, named by the status
 * text (aria-label). BrandSpinner carries the role=status live region (the
 * screen-reader "loading" announcement). The visible status line is aria-hidden
 * (sighted-only) so the text is not announced twice.
 */
export function ModalLoadingShell({ statusText }: { statusText: string }) {
  return (
    <div className="jp-modal-scrim" role="presentation">
      <div
        className="jp-modal jp-modal--loading"
        role="dialog"
        aria-modal="true"
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
