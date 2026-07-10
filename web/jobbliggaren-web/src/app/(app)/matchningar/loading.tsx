import { useTranslations } from "next-intl";
import { PlainHeaderSkeleton } from "@/components/skeletons/plain-header-skeleton";

/**
 * Route-level loading state for /matchningar (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0). This page uses a plain
 * `jp-h1` + `jp-lede` header (no jp-pagehero band), so it gets the plain-header
 * skeleton rather than inheriting the group `(app)/loading.tsx` pagehero band
 * (which would flash a band that then vanishes on swap). Not V3-native → the
 * app-shell transitional container supplies the width.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <PlainHeaderSkeleton label={t("navLoading.matchningar")} />;
}
