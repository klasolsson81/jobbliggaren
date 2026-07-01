import { useTranslations } from "next-intl";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import { CompanyWatchListView } from "./company-watch-list-view";

interface CompanyWatchListProps {
  items: ReadonlyArray<CompanyWatch>;
}

/**
 * #311 #448 (ADR 0087 D2) — the list on `/foretag`: the user's followed companies, newest first
 * (backend `ListCompanyWatchesQuery` orders by `CreatedAt DESC`). A Server Component that owns only the
 * static shell: the civic empty state (client-free) and delegating the populated list to the client
 * `CompanyWatchListView`. The view holds the #452 "matchande / alla annonser" toggle state (shared
 * across rows) — the smallest possible client boundary; the RSC page still does all data fetching.
 *
 * <para>Empty state names where a follow is created (civic-utility, honest no-data copy — the follow
 * affordance lives on the job-ad detail, not here; consistent with `/sparade`).</para>
 */
export function CompanyWatchList({ items }: CompanyWatchListProps) {
  const t = useTranslations("jobads.companyWatches");

  if (items.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("emptyTitle")}</div>
        {t.rich("emptyBody", { i: (chunks) => <i>{chunks}</i> })}
      </div>
    );
  }

  return <CompanyWatchListView items={items} />;
}
