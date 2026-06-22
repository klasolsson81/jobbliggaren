import { useTranslations } from "next-intl";
import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense fallback for the guest-tree @modal/(.)jobb/[id] (logo Fas 2, ADR 0070).
// Mirrors the authenticated route for symmetry (the four parallel @modal folders
// stay aligned) and gives a no-auth path for visual-verifying the spinner. NB:
// guest data is a synchronous mock (findGuestJobAd) → barely suspends, so the
// fallback rarely shows here in practice; the authenticated route is the truly
// known-slow one.
export default function Loading() {
  // Synchronous next-intl translator — keeps this a non-async RSC.
  const t = useTranslations("guest");
  return <ModalLoadingShell statusText={t("modal.jobAdLoading")} />;
}
