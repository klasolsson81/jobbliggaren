import { useTranslations } from "next-intl";
import { DetailPageSkeleton } from "@/components/skeletons/detail-page-skeleton";

/**
 * Route-level loading state for the full-page /jobb/[id] (#739 — the loading half
 * of finding `g1-jobad-detail-open-serial-stages`). The modal *intercept* already
 * paints its `ModalLoadingShell` spinner, but the full page (direct link / hard
 * load) had none. Re-uses the shared detail-envelope skeleton; the ad label is
 * already translated (`pages.jobb.loading`).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return <DetailPageSkeleton label={t("jobb.loading")} />;
}
