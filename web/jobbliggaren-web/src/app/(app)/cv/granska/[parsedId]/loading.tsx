import { BrandSpinner } from "@/components/brand/brand-spinner";

/**
 * Suspense-fallback för /cv/granska/[parsedId] (Fas 4 STEG B, F1). Next wrappar
 * page.tsx i en <Suspense> — denna fallback målas medan parse-hämtningen och
 * den compute-on-demand-granskningen streamar in. Granskningen kan överstiga
 * 1–2 s → spinner (formlös, känt-långsam väntan per spinner-doktrinen), inte
 * skeleton.
 */
export default function Loading() {
  return (
    <div className="jp-cv-loading">
      <BrandSpinner size={44} label="Läser in granskningen" />
      <p className="jp-cv-loading__text">Läser in granskningen…</p>
    </div>
  );
}
