import { useTranslations } from "next-intl";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import { CompanyWatchRow } from "./company-watch-row";

interface CompanyWatchListProps {
  items: ReadonlyArray<CompanyWatch>;
}

/**
 * #311 #448 (ADR 0087 D2) — the list on `/foretag`: the user's followed companies, newest first
 * (backend `ListCompanyWatchesQuery` orders by `CreatedAt DESC`). A pure presentation Server Component
 * (no client state — the per-row unfollow is the only interactivity, isolated in the client
 * `CompanyWatchRow`; parity with `MatchList` rendering client-free while its rows carry the behaviour).
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

  return (
    <ul className="jp-jobs" aria-label={t("listLabel")}>
      {items.map((item) => (
        <CompanyWatchRow key={item.id} item={item} />
      ))}
    </ul>
  );
}
