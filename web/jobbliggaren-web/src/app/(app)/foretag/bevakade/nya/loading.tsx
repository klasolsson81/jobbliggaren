import { useTranslations } from "next-intl";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { ForetagSurfaceSkeleton } from "@/components/foretag/foretag-surface-skeleton";

/**
 * Route-level loading UI for `/foretag/bevakade/nya` (#1576) — paints the real pagehero + persistent
 * sub-nav so the /foretag chrome is stable across navigations, then a civic list skeleton. Parity
 * with the sibling `/foretag/bevakade` skeleton; this surface always awaits a per-user read, so
 * unlike a page whose awaits the layout already resolved, it can genuinely paint.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <ForetagPagehero
        title={t("foretag.newAds.heading")}
        lede={t("foretag.newAds.lede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="bevakade" />
        <ForetagSurfaceSkeleton />
      </div>
    </>
  );
}
