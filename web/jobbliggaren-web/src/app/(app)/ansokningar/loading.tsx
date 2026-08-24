import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /ansokningar (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the
 * applications-list ledger shape immediately on navigation, instead of freezing
 * the previous page.
 *
 * Re-uses `jp-pagehero` + `jp-section` structural classes. Also covers the deeper
 * `/ansokningar/[id]` route only until that segment's own `loading.tsx` (the detail
 * skeleton) takes over. sr-only `role="status"` announces; visuals decorative. Sync RSC.
 *
 * **The band this file reserves used to model a page layout that no longer exists**
 * (#1467, measured 2026-08-23 at `173e767c`: the hero grew by up to 202px on swap, worst
 * at 375–414). It reserved ONE row-shaped block in the aside and drew the secondary
 * actions as a separate right-aligned row BELOW the hero — but `page.tsx` puts all three
 * controls INSIDE the aside, in two `__btnrow`s under the `--stacked` modifier. So the
 * fallback under-reserved the band and over-reserved beneath it, in the same swap.
 *
 * It now mirrors that structure: the real title and lede (static translations, so the
 * browser wraps them exactly as the page does — `cv/(hub)/loading.tsx` is the precedent),
 * the `--stacked` modifier via `stacked`, and both rows at the `.jp-btn` height.
 *
 * ⚠ **The bar widths approximate rather than mirror**, and that is where this file can
 * still disagree with the page. Row 2's COMBINED width decides where it wraps, and the
 * wrap is what the band's height is made of — but a fixed bar stands in for a control
 * whose width follows its label, so the two thresholds cannot coincide at every width,
 * and they move apart again in a locale whose labels are longer.
 * The residual is a narrow viewport band around the wrap transition, measured and named in
 * the PR that closes #1467; re-measure it rather than reasoning about it if these labels
 * change.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.ansokningar")}
      </span>

      <PageHeroSkeleton
        title={t("ansokningar.title")}
        lede={t("ansokningar.lede")}
        stacked
        aside={
          <>
            <div className="jp-pagehero__btnrow">
              <span className="jp-skeleton block h-11 w-36" />
            </div>
            <div className="jp-pagehero__btnrow">
              <span className="jp-skeleton block h-11 w-28" />
              <span className="jp-skeleton block h-11 w-64" />
            </div>
          </>
        }
      />

      <div className="jp-container jp-page" aria-hidden="true">
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-48" />
            <span className="jp-skeleton block h-4 w-12" />
          </div>
          <div className="flex flex-col gap-4">
            {[0, 1, 2, 3, 4].map((row) => (
              <div
                key={row}
                className="flex items-center justify-between gap-4"
              >
                <div className="flex min-w-0 flex-1 flex-col gap-2">
                  <span className="jp-skeleton block h-4 w-1/2 max-w-full" />
                  <span className="jp-skeleton block h-3 w-1/3 max-w-full" />
                </div>
                <span className="jp-skeleton block h-4 w-20 shrink-0" />
              </div>
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
