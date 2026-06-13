import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense-fallback för gäst-tree @modal/(.)ansokningar/[id] (logo Fas 2, ADR
// 0070). Speglar den inloggade vägen för symmetri (de fyra parallella
// @modal-mapparna hålls lika) och ger en no-auth-väg för visual-verify. NB:
// gäst-data är synkron mock (findGuestApplication) → suspendar knappt, så
// fallbacken syns sällan här; den inloggade vägen är den verkligt känt-långa.
export default function Loading() {
  return <ModalLoadingShell statusText="Ansökan läses in…" />;
}
