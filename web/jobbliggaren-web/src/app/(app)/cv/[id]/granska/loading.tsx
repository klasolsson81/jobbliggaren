import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /cv/[id]/granska (#1385) — the canonical CV review.
 * Before this file the route inherited the CV hub's fallback, so a soft navigation
 * from a CV card painted the hub's three-card grid and a two-button aside, then
 * landed a review panel with neither. This is the
 * highest-traffic wrong-shape path in the issue: the review is reached from the hub's
 * own card (`components/resumes/resume-card.tsx`) and is NOT intercepted to a modal.
 *
 * The hero renders the REAL title and lede — static translations, wrapped by the
 * browser exactly as the loaded page wraps them, which is what a fixed line count
 * cannot do across viewports. `aside={null}` because this page renders no aside element
 * (see the component for why an empty one is not the same thing).
 *
 * The panel below stays flat grey: the findings are data. `.jp-cvreview` is a layout
 * class only, so re-using it takes the gaps from the same rule the loaded page uses
 * rather than from a number here.
 *
 * sr-only `role="status"` announces; visuals decorative. Sync RSC.
 */
const DIMENSIONS = [0, 1, 2];

export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.cvGranska")}
      </span>

      <PageHeroSkeleton
        title={t("cv.granska.title")}
        lede={t("cv.granska.lede")}
        aside={null}
      />

      <div className="jp-container jp-page flex flex-col gap-6" aria-hidden="true">
        {/* The bar sits INSIDE a real `.jp-backlink`, so the row's height comes from that
            rule rather than from a class chosen here: the control is 32px at desktop and
            44px under the touch floor, and a single height class can only match one of them. */}
        <span className="jp-backlink self-start">
          <span className="jp-skeleton block h-4 w-40" />
        </span>
        <span className="jp-skeleton block h-5 w-56 max-w-full" />

        <section className="jp-cvreview">
          <span className="jp-skeleton block h-7 w-64 max-w-full" />
          <span className="jp-skeleton block h-16 w-full" />
          {DIMENSIONS.map((dimension) => (
            <div key={dimension} className="flex flex-col gap-2">
              <span className="jp-skeleton block h-5 w-48 max-w-full" />
              <span className="jp-skeleton block h-4 w-full" />
              <span className="jp-skeleton block h-4 w-5/6 max-w-full" />
            </div>
          ))}
        </section>
      </div>
    </>
  );
}
