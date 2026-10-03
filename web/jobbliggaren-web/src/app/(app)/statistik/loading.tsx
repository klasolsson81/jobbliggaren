import { useTranslations } from "next-intl";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

export default function Loading() {
  const t = useTranslations("statistik");
  const tPages = useTranslations("pages");
  return (
    <>
      <span role="status" aria-live="polite" aria-busy="true" className="sr-only">
        {tPages("navLoading.statistik")}
      </span>
      <PageHeroSkeleton title={t("title")} lede={t("lede")} aside={null} />
      <div className="jp-container jp-page" aria-hidden="true">
        <span className="jp-skeleton mb-6 block h-5 w-48 max-w-full" />
        <div className="flex flex-col gap-4">
          {[0, 1, 2].map((row) => (
            <span key={row} className="jp-skeleton block h-11 w-full" />
          ))}
        </div>
      </div>
    </>
  );
}
