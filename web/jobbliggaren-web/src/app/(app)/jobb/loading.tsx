import { useTranslations } from "next-intl";
import { JobAdListSkeleton } from "@/components/job-ads/job-ad-list-skeleton";

/**
 * Route-level loading state for /jobb (#739 — findings
 * `p1-no-loading-tsx-any-primary-route` P0 + `p3-jobb-initial-nav-hero-deps-block`
 * P1). The in-page `<Suspense>` in page.tsx only swaps the RESULT area during an
 * in-page search; the first cross-route navigation to /jobb had no `loading.tsx`,
 * so the hero's 4-way `Promise.all` blocked with zero affordance. This paints the
 * jp-hero plate + list skeleton instantly on navigation.
 *
 * The hero title + lede are static translations, so they render for real (no CLS
 * on the left column); the interactive right-panel controls (search, filters,
 * chips) need data, so they show flat-grey `.jp-skeleton` placeholders. The
 * result area re-uses `<JobAdListSkeleton />` — the exact shape the mounted page
 * streams into — which also owns the polite `role="status"` announcement, so the
 * hero shell stays `aria-hidden`. Sync RSC, skeleton-not-spinner per doctrine.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <section className="jp-hero" aria-hidden="true">
        <div className="jp-hero__inner">
          <div className="jp-hero__plate">
            <div>
              <h1 className="jp-hero__title">{t("jobb.title")}</h1>
              <p className="jp-hero__lede">{t("jobb.lede")}</p>
            </div>

            <div className="jp-hero__panel">
              <div className="jp-hero__actions">
                <span className="jp-skeleton block h-[38px] w-40" />
                <span className="jp-skeleton block h-[38px] w-32" />
              </div>
              {/* Search block: label + field + help, mirroring the real
                  searchblock's height so the plate does not grow on swap. */}
              <div className="flex flex-col gap-2">
                <span className="jp-skeleton block h-3 w-24" />
                <span className="jp-skeleton block h-11 w-full" />
                <span className="jp-skeleton block h-3 w-48 max-w-full" />
              </div>
              <div className="jp-hero__pills">
                <span className="jp-skeleton block h-[38px] w-24" />
                <span className="jp-skeleton block h-[38px] w-28" />
                <span className="jp-skeleton block h-[38px] w-20" />
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <JobAdListSkeleton />
      </div>
    </>
  );
}
