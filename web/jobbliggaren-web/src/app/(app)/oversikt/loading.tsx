import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /oversikt (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). The page runs an 8-way `Promise.all`
 * and is `force-dynamic`, so navigation to it dead-clicked until the whole
 * dashboard rendered. This paints the pagehero + the summary/notices ledger shape
 * immediately.
 *
 * Re-uses the real structural classes (`jp-pagehero`, `jp-section`, `jp-summary`)
 * so the shape matches on swap. The pagehero aside mirrors the floating TodayCard
 * with a card-sized block. sr-only `role="status"` announces; visuals are
 * decorative. Sync RSC, flat-grey skeletons, no animation.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.oversikt")}
      </span>

      {/* kicker = the greeting overline; aside mirrors the TodayCard
          (.jp-oversikt__today is 320px wide, ~146px tall) so the band height
          matches on swap. Exact aside height is rendered-verify-gated (#739). */}
      <PageHeroSkeleton
        kicker
        aside={<span className="jp-skeleton block h-36 w-80 max-w-full" />}
      />

      <div className="jp-container jp-page" aria-hidden="true">
        {/* Notices feed */}
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-40" />
            <span className="jp-skeleton block h-4 w-8" />
          </div>
          <div className="flex flex-col gap-3">
            {[0, 1].map((row) => (
              <span key={row} className="jp-skeleton block h-4 w-2/3 max-w-full" />
            ))}
          </div>
        </section>

        {/* Summary ledger: 3 groups (Ansökningar / Bevakning / Material) */}
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-48" />
            <span className="jp-skeleton block h-4 w-8" />
          </div>
          <div className="jp-summary">
            {[0, 1, 2].map((group) => (
              <div key={group} className="jp-summary__group">
                <span className="jp-skeleton mb-2 block h-4 w-28" />
                {[0, 1, 2, 3].map((line) => (
                  <div key={line} className="jp-summary__row">
                    <span className="jp-skeleton block h-4 w-40 max-w-full" />
                    <span className="jp-skeleton block h-4 w-8" />
                  </div>
                ))}
              </div>
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
