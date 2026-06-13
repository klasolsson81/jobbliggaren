import { ModalLoadingShell } from "@/components/modals/modal-loading-shell";

// Suspense fallback for @modal/(.)jobb/[id] (logo Fas 2, ADR 0070). Next wraps
// page.tsx in a <Suspense>, so this fallback paints instantly while the server
// component (getServerSession + getJobAd) streams in, then JobAdDetail swaps in.
// = "open the empty modal instantly + spinner + status line" (spinner-vs-skeleton
// doctrine — jobbpilot-design-components skill). Modal loading is a known-slow
// formless wait — the right place for BrandSpinner, not a skeleton.
export default function Loading() {
  return <ModalLoadingShell statusText="Jobbannonsen läses in…" />;
}
