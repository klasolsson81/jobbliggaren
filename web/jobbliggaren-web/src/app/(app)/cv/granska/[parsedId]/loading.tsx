import { useTranslations } from "next-intl";
import { BrandSpinner } from "@/components/brand/brand-spinner";

/**
 * Suspense-fallback för /cv/granska/[parsedId] (Fas 4 STEG B, F1). Next wrappar
 * page.tsx i en <Suspense> — denna fallback målas medan parse-hämtningen och
 * den compute-on-demand-granskningen streamar in. Granskningen kan överstiga
 * 1–2 s → spinner (formlös, känt-långsam väntan per spinner-doktrinen), inte
 * skeleton.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <div className="jp-cv-loading">
      <BrandSpinner size={44} label={t("cv.review.loading")} />
      <p className="jp-cv-loading__text">{t("cv.review.loading")}</p>
    </div>
  );
}
