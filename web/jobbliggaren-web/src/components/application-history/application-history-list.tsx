import { useTranslations } from "next-intl";
import type { EmployerApplicationHistory } from "@/lib/dto/application-history";
import { ApplicationHistoryEmployerCard } from "./application-history-employer-card";

interface ApplicationHistoryListProps {
  items: ReadonlyArray<EmployerApplicationHistory>;
}

/**
 * #311 #448 (ADR 0087 D2) — the "Ansökningshistorik" section on `/foretag`: the caller's OWN submitted
 * applications grouped by employer (most-recently-applied first, backend order). A Server Component (no
 * interactivity — the per-employer entry list is a native `<details>`), parity `CompanyWatchList`: it
 * owns only the civic empty state and delegates each employer group to
 * `ApplicationHistoryEmployerCard`. The RSC page does all data fetching.
 *
 * <para>Honest empty state (no application history yet) names where history comes from — applying to a
 * job — without inventing data (no-mock doctrine, parity `/sparade` + the followed-companies list).</para>
 */
export function ApplicationHistoryList({ items }: ApplicationHistoryListProps) {
  const t = useTranslations("jobads.applicationHistory");

  if (items.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("emptyTitle")}</div>
        {t("emptyBody")}
      </div>
    );
  }

  return (
    <ul className="jp-jobs" aria-label={t("listLabel")}>
      {items.map((item, index) => (
        <ApplicationHistoryEmployerCard
          // org.nr is the natural group key, but a masked (pnr-shaped) group carries null — compose with
          // companyName + index so masked groups never collide and the value is never a raw org.nr key.
          key={`${item.organizationNumber ?? item.companyName ?? "grupp"}-${index}`}
          item={item}
        />
      ))}
    </ul>
  );
}
