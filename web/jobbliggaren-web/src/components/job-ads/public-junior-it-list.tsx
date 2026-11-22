import { useFormatter, useTranslations } from "next-intl";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { PublicCataloguePageFocus } from "@/components/job-ads/public-catalogue-page-focus";
import { PublicJuniorItJobAdList } from "@/components/job-ads/public-junior-it-job-ad-list";
import type { PublicJuniorItJobAdsPage } from "@/lib/dto/public-junior-it-job-ads";
import { formatNumber } from "@/lib/i18n/format";
import {
  DEFAULT_JUNIOR_IT_ORDER,
  JUNIOR_IT_PATH,
  juniorItHref,
  type JuniorItQuery,
} from "@/lib/job-ads/junior-it-params";

const COUNT_ID = "junior-count";
const PLACE_ID = "junior-place";
const SORT_ID = "junior-sort";

/**
 * The open junior IT list at `/junior` (ADR 0186). The heading and the lede carry no number. The place and order are a
 * plain GET form, so the list works without JavaScript; changing either starts again from page 1. The form, the count
 * line, the list and the pager sit in one `data-nosnippet` block, as in the catalogue: a quoted count or row goes stale,
 * and a row can carry a sole trader's name.
 *
 * Nothing here reads a session.
 */
export function PublicJuniorItList({ page, query }: { page: PublicJuniorItJobAdsPage; query: JuniorItQuery }) {
  const t = useTranslations("publicjob.junior");
  const tToolbar = useTranslations("jobads.ui.toolbar");
  const format = useFormatter();
  const { rows, places } = page;

  const chosen = query.municipality === null ? null : (places.find((p) => p.municipalityConceptId === query.municipality) ?? null);
  const hasList = rows.totalCount > 0;

  return (
    <main id="main" tabIndex={-1} className="focus:outline-none">
      <PublicCataloguePageFocus page={rows.page} />
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <h1 className="jp-pagehero__title">{t("title")}</h1>
            <p className="jp-pagehero__lede">{t("lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {!hasList && query.municipality === null && (
          <div className="jp-empty">
            <h2 className="jp-empty__title">{t("empty.title")}</h2>
            <p className="jp-empty__body">{t("empty.body")}</p>
          </div>
        )}

        {!hasList && query.municipality !== null && (
          <div className="jp-empty">
            <h2 className="jp-empty__title">{t("emptyPlace.title")}</h2>
            <p className="mt-4">
              <a href={juniorItHref({ municipality: null, sortBy: query.sortBy, page: 1 })} className="jp-btn jp-btn--secondary">
                {t("emptyPlace.allPlaces")}
              </a>
            </p>
          </div>
        )}

        {hasList && (
          <div data-nosnippet="">
            <form method="get" action={JUNIOR_IT_PATH} className="mb-6">
              <fieldset className="flex flex-wrap items-end gap-4">
                <legend className="sr-only">{t("filters.heading")}</legend>
                <div className="jp-sortfield">
                  <label htmlFor={PLACE_ID} className="jp-sortfield__label">
                    {t("filters.place")}
                  </label>
                  <select id={PLACE_ID} name="municipality" className="jp-sortfield__select" defaultValue={chosen?.municipalityConceptId ?? ""}>
                    <option value="">{t("filters.allPlaces")}</option>
                    {places.map((p) => (
                      <option key={p.municipalityConceptId} value={p.municipalityConceptId}>
                        {t("filters.placeOption", { name: p.name, count: p.count })}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="jp-sortfield">
                  <label htmlFor={SORT_ID} className="jp-sortfield__label">
                    {tToolbar("sortLabel")}
                  </label>
                  <select id={SORT_ID} name="sortBy" className="jp-sortfield__select" defaultValue={query.sortBy}>
                    <option value={DEFAULT_JUNIOR_IT_ORDER}>{tToolbar("sortPublishedDesc")}</option>
                    <option value="ExpiresAtAsc">{tToolbar("sortExpiresAsc")}</option>
                  </select>
                </div>
                <button type="submit" className="jp-btn jp-btn--secondary">
                  {t("filters.submit")}
                </button>
              </fieldset>
            </form>
            <p id={COUNT_ID} className="jp-results-count">
              <b>{formatNumber(format, rows.totalCount)}</b>{" "}
              {chosen === null
                ? t("count", { count: rows.totalCount })
                : t("countInPlace", { count: rows.totalCount, place: chosen.name })}
            </p>
            <PublicJuniorItJobAdList rows={rows.items} labelledBy={COUNT_ID} />
            <div className="mt-6">
              <JobAdPagination
                page={rows.page}
                pageSize={rows.pageSize}
                totalCount={rows.totalCount}
                buildHref={(target) => juniorItHref({ ...query, page: target })}
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
