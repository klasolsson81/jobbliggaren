import { useTranslations } from "next-intl";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { ForetagSurfaceSkeleton } from "@/components/foretag/foretag-surface-skeleton";

/**
 * Route-level loading UI for `/foretag/historik` (S1 #996) — the persistent pagehero + sub-nav, then
 * a civic list skeleton.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      <ForetagPagehero
        title={t("foretag.historyHeading")}
        lede={t("foretag.historyLede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="historik" />
        <ForetagSurfaceSkeleton />
      </div>
    </>
  );
}
