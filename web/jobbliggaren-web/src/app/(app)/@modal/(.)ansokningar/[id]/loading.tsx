import { useTranslations } from "next-intl";
import { DrawerLoadingShell } from "@/components/applications/application-drawer-loading";

// Suspense fallback for @modal/(.)ansokningar/[id] (#630 PR 6, ADR 0092 D7). Next
// wraps page.tsx in a <Suspense>, so this fallback paints instantly while the
// server component (getServerSession + getApplicationById) streams in, then the
// ApplicationDrawerBody swaps in. Drawer-shaped (not the centred ModalLoadingShell)
// so the loading surface matches the right drawer that replaces it. Detail loading
// is a known-slow formless wait — the right place for BrandSpinner, not a skeleton.
export default function Loading() {
  const t = useTranslations("pages");
  return <DrawerLoadingShell statusText={t("ansokningar.loading")} />;
}
