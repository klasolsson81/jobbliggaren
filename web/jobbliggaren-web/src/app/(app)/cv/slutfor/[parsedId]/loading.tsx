import { useTranslations } from "next-intl";
import { BrandSpinner } from "@/components/brand/brand-spinner";

/**
 * Suspense-fallback för /cv/slutfor/[parsedId] (Fas 4b PR-8.3). Speglar sibling-
 * routen /cv/granska/[parsedId] (loading.tsx): Next wrappar page.tsx i en
 * <Suspense> och målar denna medan parse-artefakten hämtas server-side. Formlös,
 * känd väntan → spinner per spinner-doktrinen (inte skeleton).
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <div className="jp-cv-loading">
      <BrandSpinner size={44} label={t("cv.slutfor.loading")} />
      <p className="jp-cv-loading__text">{t("cv.slutfor.loading")}</p>
    </div>
  );
}
