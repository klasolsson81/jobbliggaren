import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Group-level fallback loading state for the whole `(app)` area (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Segments with their own `loading.tsx`
 * (jobb, oversikt, ansokningar, cv, the two [id] detail routes) override this; the
 * rest — matchningar, foretag, installningar, sokningar, sparade, statistik,
 * aktivitetsrapport, ny-ansokan — fall back to this net so no primary navigation
 * is ever a dead click.
 *
 * A generic pagehero + a light section shape. Applies only to the `children`
 * slot; the `@modal` parallel-route slot keeps its own intercept loading states,
 * so opening a job/application modal is unaffected. sr-only `role="status"`
 * announces; visuals decorative. Sync RSC.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.generic")}
      </span>

      <PageHeroSkeleton />

      <div className="jp-container jp-page" aria-hidden="true">
        <section className="jp-section">
          <div className="jp-section__head">
            <span className="jp-skeleton block h-5 w-48" />
          </div>
          <div className="flex flex-col gap-4">
            {[0, 1, 2, 3].map((row) => (
              <span
                key={row}
                className="jp-skeleton block h-4 w-3/4 max-w-full"
              />
            ))}
          </div>
        </section>
      </div>
    </>
  );
}
