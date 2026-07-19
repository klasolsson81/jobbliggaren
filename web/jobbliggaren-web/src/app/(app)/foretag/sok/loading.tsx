import { useTranslations } from "next-intl";
import { ForetagSokResultsSkeleton } from "@/components/company-criteria/foretag-sok-results-skeleton";

/**
 * #560 PR-B — route-level loading UI for `/foretag/sok`, painted on the first cross-route navigation
 * (before the page's own reference fetch resolves). It renders the real pagehero (title + lede) so the
 * page identity is stable, then the results skeleton. Mirrors `/jobb`'s `loading.tsx`.
 */
export default function Loading() {
  const t = useTranslations("pages.foretag.sok");
  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("title")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>
      <div className="jp-container jp-page">
        <ForetagSokResultsSkeleton />
      </div>
    </>
  );
}
