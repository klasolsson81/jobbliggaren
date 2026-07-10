import { useTranslations } from "next-intl";
import { DetailPageSkeleton } from "@/components/skeletons/detail-page-skeleton";

/**
 * Route-level loading state for the full-page /ansokningar/[id] (#739 —
 * `p1-no-loading-tsx-any-primary-route` P0). Its own `loading.tsx` overrides the
 * parent /ansokningar list skeleton so the detail route paints the detail shape,
 * not the list shape. Re-uses the shared detail-envelope skeleton; the label is
 * already translated (`pages.ansokningar.loading`).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <DetailPageSkeleton label={t("ansokningar.loading")} />;
}
