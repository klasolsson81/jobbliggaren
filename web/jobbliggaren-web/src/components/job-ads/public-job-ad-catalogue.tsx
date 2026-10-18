import { useFormatter, useTranslations } from "next-intl";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { PublicJobAdList } from "@/components/job-ads/public-job-ad-list";
import type { PublicJobAdCataloguePage } from "@/lib/dto/public-job-ad-catalogue";
import { formatNumber } from "@/lib/i18n/format";
import { publicCatalogueHref } from "@/lib/job-ads/public-catalogue-url";

const COUNT_ID = "catalogue-count";

/**
 * One page of the public catalogue at `/lediga-jobb` (ADR 0175). The heading and the lede carry no number and stay
 * true at every tick, so they may be quoted in a search result. The count line, the list and the pager sit in ONE
 * `data-nosnippet` block: a quoted count or row goes stale within minutes in a copy we cannot update, and a row can
 * carry a sole trader's name (ADR 0166 W2, W4). On a count of 0 the page is the empty state, with no count line, no
 * list and no pager.
 */
export function PublicJobAdCatalogue({ page }: { page: PublicJobAdCataloguePage }) {
  const t = useTranslations("publicjob.catalogue");
  const tLanding = useTranslations("landing");
  const format = useFormatter();

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{tLanding("common.catalogueLink")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {page.totalCount === 0 ? (
          <div className="jp-empty">
            <h2 className="jp-empty__title">{t("empty.title")}</h2>
            <p className="jp-empty__body">{t("empty.body")}</p>
          </div>
        ) : (
          <div data-nosnippet="">
            <p id={COUNT_ID} className="jp-results-count">
              <b>{formatNumber(format, page.totalCount)}</b> {t("count", { count: page.totalCount })}
            </p>
            <PublicJobAdList rows={page.items} labelledBy={COUNT_ID} />
            <div className="mt-6">
              <JobAdPagination
                page={page.page}
                pageSize={page.pageSize}
                totalCount={page.totalCount}
                buildHref={publicCatalogueHref}
                showTotalCount={false}
                prefetch={false}
              />
            </div>
          </div>
        )}
      </div>
    </main>
  );
}
