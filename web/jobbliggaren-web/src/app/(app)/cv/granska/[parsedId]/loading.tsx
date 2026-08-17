import { useTranslations } from "next-intl";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { PageHeroSkeleton } from "@/components/skeletons/page-hero-skeleton";

/**
 * Suspense-fallback för /cv/granska/[parsedId] (Fas 4 STEG B, F1). Next wrappar
 * page.tsx i en <Suspense> — denna fallback målas medan parse-hämtningen och
 * den compute-on-demand-granskningen streamar in.
 *
 * Spinner och inte skeleton för INNEHÅLLET (formlös, känt-långsam väntan per
 * spinner-doktrinen) — men SKALET är känt och målas direkt (#1062, samma mönster som
 * /cv/loading.tsx). Före den här ändringen målade fallbacken varken hero eller
 * container, så skalet dök upp först när strömmen landade: exakt den CLS-klass
 * #1375 mätte på hubbens kort-skelett.
 *
 * ⚠ Väntans LÄNGD är inte mätt. Docblocket sa tidigare att granskningen "kan
 * överstiga 1–2 s"; mätt 2026-08-17 svarar review-endpointen på 24–39 ms varm
 * (staging) respektive 20–47 ms (kanonisk). Det mäter motorn, inte hela
 * RSC-renderingen — så fallbacken behålls, men utan det ogrundade tidspåståendet.
 */
export default function Loading() {
  const t = useTranslations("pages");
  return (
    <>
      {/* aside={<></>} och inte default: defaulten målar TVÅ knapp-block, och den här
          heron har ingen aside alls — `aside ?? …` betyder att `null` ger defaulten
          tillbaka, så tomheten måste vara ett icke-nullish element. Utan det hade
          fallbacken lovat två kontroller som aldrig kommer. */}
      <PageHeroSkeleton aside={<></>} />

      <div className="jp-container jp-page">
        <div className="jp-cv-loading">
          <BrandSpinner size={44} label={t("cv.review.loading")} />
          <p className="jp-cv-loading__text">{t("cv.review.loading")}</p>
        </div>
      </div>
    </>
  );
}
