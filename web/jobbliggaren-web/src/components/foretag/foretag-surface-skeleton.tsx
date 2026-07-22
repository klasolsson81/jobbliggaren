import { useTranslations } from "next-intl";

/**
 * Shared loading skeleton for the /foretag list surfaces (S1 #996), rendered by each surface's
 * `loading.tsx` beneath the persistent pagehero + sub-nav so the chrome shows instantly on navigation.
 * Civic-utility: flat neutral grey rows (`.jp-skeleton`) — no shimmer, no pulse, no spinner (parity
 * with `ForetagSokResultsSkeleton`).
 *
 * a11y: `role="status"` + `aria-live="polite"` announces the single visible "Laddar…" sentence; the
 * grey rows are `aria-hidden` so the announcement is a short sentence, not empty decoration.
 */

const SKELETON_ROWS = 6;

export function ForetagSurfaceSkeleton() {
  const t = useTranslations("pages.foretag");
  return (
    <div role="status" aria-live="polite" aria-busy="true">
      <p className="text-body-sm text-text-primary">{t("loading")}</p>
      <div className="mt-6 flex flex-col gap-2" aria-hidden="true">
        {Array.from({ length: SKELETON_ROWS }, (_, i) => (
          <div key={i} className="jp-skeleton h-16 w-full" />
        ))}
      </div>
    </div>
  );
}
