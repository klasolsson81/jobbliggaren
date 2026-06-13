import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense-fallback för @modal/(.)jobb/[id] (logo Fas 2, ADR 0070). Next lindar
// page.tsx i en <Suspense> → denna fallback målas direkt medan serverkomponenten
// (getServerSession + getJobAd) streamas in, varefter JobAdDetail swapas in.
// = "öppna tom modal direkt + spinner + statustext" (spinner-vs-skeleton-
// doktrinen — jobbpilot-design-components-skillen). Modal-laddning är en
// känt-lång formlös väntan — rätt plats för BrandSpinner, inte skeleton.
export default function Loading() {
  return <ModalLoadingShell statusText="Jobbannonsen läses in…" />;
}
