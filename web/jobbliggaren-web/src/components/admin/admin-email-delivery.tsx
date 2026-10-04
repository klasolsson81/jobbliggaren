import { useFormatter, useTranslations } from "next-intl";
import { formatDateTime } from "@/lib/i18n/format";
import type {
  AdminEmailDelivery as Delivery,
  AdminEmailPeriod,
  AdminValueRegion,
} from "@/lib/admin/view-models";
import { AdminPageHeader } from "./admin-page-header";
import { AdminRegionLine } from "./admin-region-line";
import { AdminSegment } from "./admin-segment";
import { AdminTableScroll } from "./admin-table-scroll";

const SOON_ID = "admin-email-soon";
const CAPTION_ID = "admin-email-caption";
const PERIODS: ReadonlyArray<AdminEmailPeriod> = ["h24", "d3", "d7"];

/**
 * Email delivery per email type (ADR 0150). The provider's outcomes are #1981: until then the
 * period group is disabled, the three totals are unknown (an en-dash, never 0), and both lists
 * hold one "Kommer snart" line (D2). No provider is named before an outcome from it is shown.
 * The period switches only while the delivery is loaded and the caller handles the switch.
 */
export function AdminEmailDelivery({
  region,
  period = "d7",
  onPeriodChange,
}: {
  readonly region: AdminValueRegion<Delivery>;
  readonly period?: AdminEmailPeriod;
  readonly onPeriodChange?: (period: AdminEmailPeriod) => void;
}) {
  const t = useTranslations("admin.email");
  const shared = useTranslations("admin.regions");
  const dash = useTranslations("admin.unavailable")("unknownValue");
  const format = useFormatter();
  const data = region.kind === "loaded" ? region.data : null;
  const lineKind = region.kind === "loaded" ? "empty" : region.kind;

  const total = (value: number | undefined, danger = false) =>
    value === undefined ? (
      dash
    ) : (
      <span className={danger && value > 0 ? "jp-admin-danger" : undefined}>{format.number(value)}</span>
    );

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader
        title={t("heading")}
        lede={t("lede")}
        aside={
          <AdminSegment
            label={t("period.label")}
            options={PERIODS.map((value) => ({ value, label: t(`period.${value}`) }))}
            value={period}
            onChange={data === null ? undefined : onPeriodChange}
            describedBy={region.kind === "unavailable" ? SOON_ID : undefined}
          />
        }
      />
      {region.kind === "failed" ? (
        <p className="sr-only" role="alert">
          {shared("failed")}
        </p>
      ) : null}
      {region.kind === "loading" ? (
        <p className="sr-only" role="status">
          {shared("loading")}
        </p>
      ) : null}

      <dl className="jp-adminmailsum">
        <div>
          <dt>{t("summary.sent")}</dt>
          <dd>{total(data?.totals.sent)}</dd>
        </div>
        <div>
          <dt>{t("summary.failed")}</dt>
          <dd>{total(data?.totals.failed, true)}</dd>
        </div>
        <div>
          <dt>{t("summary.noRecipient")}</dt>
          <dd>{total(data?.totals.noRecipient)}</dd>
        </div>
      </dl>

      <AdminTableScroll labelledBy={CAPTION_ID}>
        <table className="jp-table jp-admintable">
          <caption id={CAPTION_ID} className="sr-only">
            {t("table.caption")}
          </caption>
          <thead>
            <tr>
              <th scope="col">{t("table.function")}</th>
              <th scope="col" className="jp-admintable__num">
                {t("table.sent")}
              </th>
              <th scope="col" className="jp-admintable__num">
                {t("table.failed")}
              </th>
              <th scope="col">{t("table.lastError")}</th>
            </tr>
          </thead>
          <tbody>
            {data !== null && data.types.length > 0 ? (
              data.types.map((row) => (
                <tr key={row.type}>
                  <td>
                    <code>{row.type}</code>
                  </td>
                  <td className="jp-admintable__num">{format.number(row.sent)}</td>
                  <td className="jp-admintable__num">{total(row.failed, true)}</td>
                  <td>{row.lastError ?? dash}</td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={4} className="jp-admintable__soon">
                  <AdminRegionLine kind={lineKind} empty={t("table.empty")} soonId={SOON_ID} quiet />
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </AdminTableScroll>

      <section aria-labelledby="admin-email-failures" className="flex flex-col gap-4">
        <h2 id="admin-email-failures" className="jp-h2">
          {t("failures.heading")}
        </h2>
        {data !== null && data.failures.length > 0 ? (
          <ol className="jp-adminmailfail">
            {data.failures.map((failure) => (
              <li key={failure.id}>
                <span className="jp-adminmailfail__when">{formatDateTime(format, failure.occurredAt) ?? dash}</span>
                <span>{failure.recipient}</span>
                <span className="jp-adminmailfail__outcome" data-outcome={failure.outcome}>
                  {t(`outcome.${failure.outcome}`)}
                </span>
                <span className="jp-adminmailfail__message">{failure.message}</span>
                <code>{failure.type}</code>
              </li>
            ))}
          </ol>
        ) : (
          <AdminRegionLine kind={lineKind} empty={t("failures.empty")} quiet />
        )}
      </section>
    </div>
  );
}
