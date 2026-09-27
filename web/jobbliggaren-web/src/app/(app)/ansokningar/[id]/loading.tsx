import { useTranslations } from "next-intl";
import { ApplicationDetailSkeleton } from "@/components/applications/application-detail-skeleton";

/**
 * Route-level loading state for the full-page /ansokningar/[id] (#739 —
 * `p1-no-loading-tsx-any-primary-route` P0). Its own `loading.tsx` overrides the
 * parent /ansokningar list skeleton so the detail route paints the detail shape,
 * not the list shape: the one detail body's skeleton (#1827 M6). The label is
 * already translated (`pages.ansokningar.loading`).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <ApplicationDetailSkeleton label={t("ansokningar.loading")} />;
}
