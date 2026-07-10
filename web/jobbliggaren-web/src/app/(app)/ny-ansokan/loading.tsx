import { useTranslations } from "next-intl";
import { PlainHeaderSkeleton } from "@/components/skeletons/plain-header-skeleton";

/**
 * Route-level loading state for /ny-ansokan (#739 — finding
 * `p1-no-loading-tsx-any-primary-route`, P0). Plain `jp-h1` + `jp-lede` header
 * (no jp-pagehero band). Unlike the other plain-header pages this route IS
 * V3-native (owns its `.jp-container.jp-page`, #332), so `contained` wraps the
 * skeleton the same way to keep the width consistent on swap.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <PlainHeaderSkeleton label={t("navLoading.nyAnsokan")} contained />;
}
