import { useTranslations } from "next-intl";
import { ForetagSokResultsSkeleton } from "@/components/company-criteria/foretag-sok-results-skeleton";
import { Announcer } from "@/components/common/announcer";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";

/**
 * #560 PR-B — route-level loading UI for `/foretag/sok`, painted on the first cross-route navigation
 * (before the page's own reference fetch resolves). It renders the real pagehero (title + lede) so the
 * page identity is stable, then the results skeleton. Mirrors `/jobb`'s `loading.tsx`.
 *
 * #1092 — the skeleton announces through a live region it does not own, so this host provides one
 * too. Next's route announcer already speaks the page title on a cross-route navigation; this adds
 * the load sentence beneath it, and without the wrapper the skeleton would simply be silent here.
 * This region is its own node, distinct from the one `page.tsx` mounts: the end-of-load sentence
 * lands in that one. Both are empty when they mount, which is what ARIA22 asks; neither spans the
 * whole cycle.
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
        <ForetagSubnav active="sok" />
        <Announcer>
          <ForetagSokResultsSkeleton />
        </Announcer>
      </div>
    </>
  );
}
