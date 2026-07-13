import { useTranslations } from "next-intl";
import { Info } from "lucide-react";
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
 *
 * <para><b>#824 PR 4 — the incompleteness disclosure is load-bearing, not polish.</b> An application is
 * attributed to an employer only while the ad it was sent to still carries the employer identity; the
 * history handler drops every application whose ad no longer does (`Where(r => r.OrgNr != null)`). That
 * silently undercounts a group AND can drop an employer group in full — up to and including a user who
 * has applied seeing the empty state. Every count on this page is therefore a FLOOR, and the disclosure
 * is the only surface that can say so for the two cases the per-group counter cannot reach: the missing
 * employer and the empty compilation. It renders in BOTH branches, above the content it qualifies (Art.
 * 5(1)(a)/(d) fairness — a count asserted as a total is a false statement to the data subject about her
 * own data). Worded from the RULE, not the retention mechanism, so it stays true after #841 stabilises
 * the number: the historical residue never returns.</para>
 */
export function ApplicationHistoryList({ items }: ApplicationHistoryListProps) {
  const t = useTranslations("jobads.applicationHistory");

  return (
    <>
      <p className="mb-4 flex items-start gap-2 text-body-sm font-medium">
        <Info size={16} aria-hidden="true" className="mt-0.5 shrink-0" />
        <span>{t("incompleteNote")}</span>
      </p>

      {items.length === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          {t("emptyBody")}
        </div>
      ) : (
        <ul className="jp-jobs" aria-label={t("listLabel")}>
          {items.map((item, index) => (
            <ApplicationHistoryEmployerCard
              // org.nr is the natural group key, but a masked (pnr-shaped) group carries null — compose
              // with companyName + index so masked groups never collide and the value is never a raw
              // org.nr key.
              key={`${item.organizationNumber ?? item.companyName ?? "grupp"}-${index}`}
              item={item}
            />
          ))}
        </ul>
      )}
    </>
  );
}
