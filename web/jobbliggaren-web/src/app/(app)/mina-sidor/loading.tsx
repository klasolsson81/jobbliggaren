import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Route-level loading state for /mina-sidor (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0). Paints the page's pagehero with its real title and
 * lede, and no aside, because the page renders none (#1385), then the two card columns. sr-only
 * `role="status"` announces; the shapes are decorative. Sync RSC.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {t("navLoading.minaSidor")}
      </span>

      <PageHeroSkeleton
        title={t("minaSidor.title")}
        lede={t("minaSidor.lede")}
        aside={null}
      />

      <div className="jp-container jp-page" aria-hidden="true">
        <div className="jp-settings-grid">
          <div className="jp-settings-grid__col">
            <span className="jp-skeleton block h-64 w-full" />
            <span className="jp-skeleton block h-40 w-full" />
          </div>
          <div className="jp-settings-grid__col">
            <span className="jp-skeleton block h-40 w-full" />
            <span className="jp-skeleton block h-40 w-full" />
          </div>
        </div>
      </div>
    </>
  );
}
