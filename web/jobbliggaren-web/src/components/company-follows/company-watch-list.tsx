import Link from "next/link";
import { useTranslations } from "next-intl";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import { CompanyWatchListView } from "./company-watch-list-view";

interface CompanyWatchListProps {
  items: ReadonlyArray<CompanyWatch>;
  /**
   * F4b: taxonomins län (med kommuner) för filter-dialogens ort-picker. Tom lista när taxonomin inte
   * kunde läsas in — pickern degraderar då civilt (samma hållning som matchnings-kortet).
   */
  regions: ReadonlyArray<TaxonomyRegion>;
}

/**
 * #311 #448 (ADR 0087 D2) — the list on `/foretag`: the user's followed companies, newest first
 * (backend `ListCompanyWatchesQuery` orders by `CreatedAt DESC`). A Server Component that owns only the
 * static shell: the civic empty state (client-free) and delegating the populated list to the client
 * `CompanyWatchListView`. The view holds the #452 "matchande / alla annonser" toggle state (shared
 * across rows) — the smallest possible client boundary; the RSC page still does all data fetching.
 *
 * The empty state links directly to the register search where a company can be followed.
 */
export function CompanyWatchList({ items, regions }: CompanyWatchListProps) {
  const t = useTranslations("jobads.companyWatches");

  if (items.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("emptyTitle")}</div>
        <p className="jp-empty__body">{t("emptyBody")}</p>
        <Link href="/foretag/sok" className="jp-btn jp-btn--secondary">
          {t("emptyCta")}
        </Link>
      </div>
    );
  }

  return <CompanyWatchListView items={items} regions={regions} />;
}
