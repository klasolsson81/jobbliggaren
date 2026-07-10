import { useTranslations } from "next-intl";
import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense fallback for @modal/(.)ansokningar/[id]. Next wraps page.tsx in a
// <Suspense>, so this fallback paints instantly while the server component
// (getServerSession + getApplicationById) streams in, then the detail body
// swaps in. Centred ModalLoadingShell — matches the centred modal that
// replaces it (Klas 2026-07-10: the PR 6 right-drawer is retired; ADR 0092
// Livscykel-amendment). Detail loading is a known-slow formless wait — the
// right place for BrandSpinner, not a skeleton.
export default function Loading() {
  const t = useTranslations("pages");
  return <ModalLoadingShell statusText={t("ansokningar.loading")} />;
}
