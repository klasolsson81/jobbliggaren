import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /cv (#739 — finding
 * `p1-no-loading-tsx-any-primary-route` P0). Paints the pagehero + the CV card
 * grid shape immediately on navigation.
 *
 * Re-uses `jp-pagehero` + `jp-cvgrid` + `jp-cv` structural classes so the grid
 * matches on swap; the default two-action aside mirrors "Importera" + "Nytt CV".
 * sr-only `role="status"` announces; visuals decorative. Sync RSC.
 */
const CARDS = [0, 1, 2];
const SKILL_CHIPS = [0, 1, 2, 3];

export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.cv")}
      </span>

      <PageHeroSkeleton />

      <div className="jp-container jp-page" aria-hidden="true">
        <div className="jp-cvgrid">
          {CARDS.map((card) => (
            <article key={card} className="jp-cv">
              <div className="jp-cv__head">
                <div className="min-w-0 flex-1">
                  <span className="jp-skeleton block h-5 w-2/3 max-w-full" />
                  <span className="jp-skeleton mt-2 block h-4 w-1/2 max-w-full" />
                </div>
                <span className="jp-skeleton block h-6 w-20" />
              </div>
              <div className="jp-cv__skills">
                {SKILL_CHIPS.map((chip) => (
                  <span key={chip} className="jp-skeleton block h-6 w-16" />
                ))}
              </div>
              <div className="jp-cv__meta">
                <span className="jp-skeleton block h-3 w-40 max-w-full" />
              </div>
              <div className="jp-cv__actions">
                <span className="jp-skeleton block h-9 w-24" />
                <span className="jp-skeleton block h-9 w-28" />
              </div>
            </article>
          ))}
        </div>
      </div>
    </>
  );
}
