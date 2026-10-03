import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";
import { ForetagSurfaceSkeleton } from "@/components/foretag/foretag-surface-skeleton";

export default function Loading() {
  const t = useTranslations("pages.foretag.criteria");
  const tPages = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {tPages("foretag.loading")}
      </span>
      <PageHeroSkeleton lede={t("ads.lede")} aside={null} />
      <div className="jp-container jp-page" aria-hidden="true">
        <span className="jp-skeleton mb-4 block h-5 w-48 max-w-full" />
        <span className="jp-skeleton mb-4 block h-6 w-80 max-w-full" />
        <ForetagSurfaceSkeleton />
      </div>
    </>
  );
}
