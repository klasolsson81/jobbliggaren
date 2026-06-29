import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import type {
  ApplicationRateDto,
  ApplicationStatsDto,
} from "@/lib/dto/application-stats";
import { PIPELINE_ORDER } from "@/lib/applications/status";

/**
 * #313 — application-statistics view (BUILD.md §6.2). Server component, civic and
 * tables-first: no decorative charts, every figure is a plain count, and every
 * rate shows its base (numerator / denominator) beside the percentage so a number
 * is never opaque (§5). Status labels reuse the SPOT in `applications.enums.status`
 * and the canonical `PIPELINE_ORDER`; the funnel and the rates reuse the backend's
 * already-computed values — no metric is re-implemented in TypeScript.
 */
export function ApplicationStats({ data }: { data: ApplicationStatsDto }) {
  const t = useTranslations("statistik");
  const tStatus = useTranslations("applications");
  const format = useFormatter();

  const num = (value: number) => format.number(value);
  const pct = (percent: number) =>
    format.number(percent / 100, { style: "percent", maximumFractionDigits: 0 });
  const monthLabel = (year: number, month: number) =>
    format.dateTime(new Date(Date.UTC(year, month - 1, 1)), {
      month: "short",
      year: "numeric",
    });

  if (data.totalApplications === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("empty.title")}</div>
        <p className="jp-empty__body">{t("empty.body")}</p>
        <div className="jp-empty__actions">
          <Link href="/ny-ansokan" className="jp-btn jp-btn--primary">
            {t("empty.createFirst")}
          </Link>
          <Link href="/jobb" className="jp-btn jp-btn--ghost">
            {t("empty.searchFirst")}
          </Link>
        </div>
      </div>
    );
  }

  const hasSent = data.totalSent > 0;

  // The backend emits status counts in ordinal order already; re-sorting on the
  // canonical PIPELINE_ORDER keeps the table stable even if that ever changes.
  const orderedStatusCounts = [...data.statusCounts].sort(
    (a, b) => PIPELINE_ORDER.indexOf(a.status) - PIPELINE_ORDER.indexOf(b.status),
  );

  const rateRows: { label: string; rate: ApplicationRateDto }[] = [
    { label: t("rates.response"), rate: data.responseRate },
    { label: t("rates.interview"), rate: data.interviewRate },
    { label: t("rates.rejection"), rate: data.rejectionRate },
  ];

  return (
    <div className="flex flex-col gap-6">
      <p className="text-body">
        {t("summary", { total: data.totalApplications, sent: data.totalSent })}
      </p>

      <section className="jp-card" aria-labelledby="stats-rates-heading">
        <h2 id="stats-rates-heading" className="jp-card__title">
          {t("rates.heading")}
        </h2>
        {hasSent ? (
          <>
            <p className="mb-4 text-body">{t("rates.intro")}</p>
            <table className="jp-table">
              <thead>
                <tr>
                  <th scope="col">{t("rates.metric")}</th>
                  <th scope="col">{t("rates.value")}</th>
                  <th scope="col">{t("rates.share")}</th>
                </tr>
              </thead>
              <tbody>
                {rateRows.map(({ label, rate }) => (
                  <tr key={label}>
                    <td>{label}</td>
                    <td className="tabular-nums">
                      {t("rates.ofSent", {
                        numerator: rate.numerator,
                        denominator: rate.denominator,
                      })}
                    </td>
                    <td className="tabular-nums">{pct(rate.percent)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ) : (
          <p className="text-body">{t("rates.empty")}</p>
        )}
      </section>

      {hasSent && (
        <section className="jp-card" aria-labelledby="stats-funnel-heading">
          <h2 id="stats-funnel-heading" className="jp-card__title">
            {t("funnel.heading")}
          </h2>
          <p className="mb-4 text-body">{t("funnel.intro")}</p>
          <table className="jp-table">
            <thead>
              <tr>
                <th scope="col">{t("funnel.stage")}</th>
                <th scope="col">{t("funnel.count")}</th>
                <th scope="col">{t("funnel.share")}</th>
              </tr>
            </thead>
            <tbody>
              {data.funnel.map((stage) => (
                <tr key={stage.stage}>
                  <td>{t(`funnel.stages.${stage.stage}`)}</td>
                  <td className="tabular-nums">{num(stage.count)}</td>
                  <td className="tabular-nums">{pct(stage.percentOfSent)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {data.offFunnelExitCount > 0 && (
            <p className="mt-4 text-body">
              {t("funnel.limitation", { count: data.offFunnelExitCount })}
            </p>
          )}
        </section>
      )}

      <section className="jp-card" aria-labelledby="stats-status-heading">
        <h2 id="stats-status-heading" className="jp-card__title">
          {t("statusBreakdown.heading")}
        </h2>
        <p className="mb-4 text-body">{t("statusBreakdown.intro")}</p>
        <table className="jp-table">
          <thead>
            <tr>
              <th scope="col">{t("statusBreakdown.status")}</th>
              <th scope="col">{t("statusBreakdown.count")}</th>
            </tr>
          </thead>
          <tbody>
            {orderedStatusCounts.map((statusCount) => (
              <tr key={statusCount.status}>
                <td>{tStatus(`enums.status.${statusCount.status}`)}</td>
                <td className="tabular-nums">{num(statusCount.count)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="jp-card" aria-labelledby="stats-monthly-heading">
        <h2 id="stats-monthly-heading" className="jp-card__title">
          {t("monthly.heading")}
        </h2>
        <p className="mb-4 text-body">{t("monthly.intro")}</p>
        <table className="jp-table">
          <thead>
            <tr>
              <th scope="col">{t("monthly.month")}</th>
              <th scope="col">{t("monthly.count")}</th>
            </tr>
          </thead>
          <tbody>
            {data.monthlyApplications.map((month) => (
              <tr key={`${month.year}-${month.month}`}>
                <td>{monthLabel(month.year, month.month)}</td>
                <td className="tabular-nums">{num(month.count)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
