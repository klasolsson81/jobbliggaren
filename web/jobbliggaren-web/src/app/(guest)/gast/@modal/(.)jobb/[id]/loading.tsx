import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense-fallback för gäst-tree @modal/(.)jobb/[id] (logo Fas 2, ADR 0070).
// Speglar den inloggade vägen för symmetri (de fyra parallella @modal-mapparna
// hålls lika) och ger en no-auth-väg för visual-verify av spinnern. NB:
// gäst-data är synkron mock (findGuestJobAd) → suspendar knappt, så fallbacken
// syns sällan här i praktiken; den inloggade vägen är den verkligt känt-långa.
export default function Loading() {
  return <ModalLoadingShell statusText="Jobbannonsen läses in…" />;
}
