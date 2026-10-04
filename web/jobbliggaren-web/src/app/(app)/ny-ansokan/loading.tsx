import { useTranslations } from "next-intl";
import { PlainHeaderSkeleton } from "@/components/skeletons/plain-header-skeleton";

/**
 * Route-level loading state for /ny-ansokan (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0). Plain `jp-h1` header
 * (no jp-pagehero band).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <PlainHeaderSkeleton label={t("navLoading.nyAnsokan")} lede={false} />;
}
