import { useTranslations } from "next-intl";
import { PlainHeaderSkeleton } from "@/components/skeletons/plain-header-skeleton";

/**
 * Route-level loading state for /sokningar (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0). Plain `jp-h1` + `jp-lede` header
 * (no jp-pagehero band); not V3-native → shell transitional container supplies
 * the width. See PlainHeaderSkeleton for why bandless pages need their own skeleton.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <PlainHeaderSkeleton label={t("navLoading.sokningar")} />;
}
