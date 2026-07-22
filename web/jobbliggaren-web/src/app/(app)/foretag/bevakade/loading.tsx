import { useTranslations } from "next-intl";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { ForetagSurfaceSkeleton } from "@/components/foretag/foretag-surface-skeleton";

/**
 * Route-level loading UI for `/foretag/bevakade` (S1 #996) — paints the real pagehero + persistent
 * sub-nav so the /foretag chrome is stable across navigations, then a civic list skeleton.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <ForetagPagehero
        title={t("foretag.watchesHeading")}
        lede={t("foretag.watchesLede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="bevakade" />
        <ForetagSurfaceSkeleton />
      </div>
    </>
  );
}
