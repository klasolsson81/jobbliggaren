import type { ReactNode } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import {
  EMAIL_DELIVERY_PERIODS,
  emailReadLine,
  failureMinute,
  isAlertingRead,
  isSharedCause,
  withEmailPeriod,
  type EmailRegionLine,
} from "@/lib/admin/email-delivery";
import type {
  AdminEmailDelivery as Delivery,
  AdminEmailFailures as Failures,
  AdminEmailKinds as Kinds,
  AdminEmailPeriod,
  AdminEmailRead,
} from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminEmailKinds, useEmailKindsLine } from "./admin-email-kinds";
import { AdminTableScroll } from "./admin-table-scroll";
import { AdminUnknown } from "./admin-unknown";

const HEADING_ID = "admin-email-delivery";
const FAILURES_HEADING_ID = "admin-email-failures";
const FAILURES_CAPTION_ID = "admin-email-failures-caption";

/**
 * The e-post page's "Leverans" section (#1981 PR 2 and PR 3; ADR 0150 D2, D4, D8): the provider's counts for a
 * period, the latest failed sends, and our own count per mail kind.
 *
 * Three regions, each in its own state (ADR 0150 D2). The summary shows Skickade and Misslyckade only from an observed
 * read; every other state is an en-dash with a line saying why, never 0. The failures table says "no failed sends" only
 * after an observed read with no rows: a read that could not be judged is unknown, never empty. The per-kind table is
 * its own region over its own read (`AdminEmailKinds`).
 *
 * The period is the URL's (D8): three links under the heading, so a period change is a navigation and the activated
 * link keeps focus. `period` is null while the route loads, since the loading state cannot read the URL and marks no
 * period. The section announces its failed, unjudgeable and loading states in one line, in the form
 * "{source}: {state}": the provider's two reads as Leverans, the per-kind read under its own heading, and a cause every
 * region shares once. The regions' own lines are quiet.
 */
export function AdminEmailDelivery({
  pagePath,
  period,
  summary,
  failures,
  kinds,
}: {
  /** The page the period links lead to: the live page, or the preview's. */
  readonly pagePath: string;
  readonly period: AdminEmailPeriod | null;
  readonly summary: AdminEmailRead<Delivery>;
  readonly failures: AdminEmailRead<Failures>;
  readonly kinds: AdminEmailRead<Kinds>;
}) {
  const t = useTranslations("admin.email");
  const lines = useTranslations("admin.email.allowance");
  const admin = useTranslations("admin");
  const format = useFormatter();

  const observed = summary.kind === "loaded" && summary.data.status === "Observed" ? summary.data : null;
  const rows = failures.kind === "loaded" && failures.data.status === "Observed" ? failures.data : null;

  function stateLine(read: AdminEmailRead<Delivery | Failures>, unjudged: string): EmailRegionLine | null {
    if (read.kind === "loading") return { text: admin("regions.loading"), announce: "status" };
    if (read.kind === "failed") return { text: read.line ?? admin("regions.failed"), announce: "alert" };
    const key = emailReadLine(read.data);
    if (key === null) return null;
    const text = key === "notObservable" ? unjudged : lines(key);
    return { text, announce: isAlertingRead(read.data) ? "alert" : null };
  }

  const summaryLine = stateLine(summary, lines("notObservable"));
  // An unjudged list says only "Uppgift saknas": beside an unjudged summary, the summary's line says why, and alone its
  // cause need not be the read key's, so it never borrows that sentence.
  const unknown = admin("unavailable.unknownSpoken");
  const failuresLine = stateLine(failures, unknown);
  // A list whose cause the summary's line already gives says only "Uppgift saknas"; a load is not a cause.
  const sameCause = failures.kind !== "loading" && failuresLine !== null && failuresLine.text === summaryLine?.text;
  const failuresBody = rows === null
    ? (sameCause ? unknown : failuresLine?.text ?? null)
    : rows.failures.length === 0 ? t("failures.empty") : null;

  // Where both reads could not be judged, the summary's sentence is the whole announcement.
  const bothUnjudged = [summary, failures].every((read) => read.kind === "loaded" && read.data.status === "NotObservable");
  const announced = [summaryLine, bothUnjudged ? null : failuresLine]
    .filter((line): line is EmailRegionLine => line !== null && line.announce !== null);
  const kindsLine = useEmailKindsLine(kinds);
  // The per-kind read is its own source, unless its cause is every region's: then Leverans reads that cause once.
  const kindsAnnounced =
    kindsLine !== null && kindsLine.announce !== null && !isSharedCause([summary, failures, kinds]) ? kindsLine : null;
  const role = [...announced, kindsAnnounced].some((line) => line?.announce === "alert") ? "alert" : "status";
  const announcement = [
    announced.length === 0
      ? null
      : admin("overview.observation.announcement", {
          source: t("delivery.heading"),
          state: [...new Set(announced.map((line) => line.text))].join(" "),
        }),
    kindsAnnounced === null
      ? null
      : admin("overview.observation.announcement", { source: t("kinds.heading"), state: kindsAnnounced.text }),
  ].filter((part): part is string => part !== null);

  const value = (shown: ReactNode | null) => (shown === null ? <AdminUnknown /> : shown);

  return (
    <section aria-labelledby={HEADING_ID} className="flex flex-col gap-6">
      {announcement.length > 0 ? (
        <p className="sr-only" role={role}>
          {announcement.join(" ")}
        </p>
      ) : null}
      <div className="jp-adminsection__head">
        <h2 id={HEADING_ID} className="jp-h2">
          {t("delivery.heading")}
        </h2>
        <nav className="jp-subnav" aria-label={t("period.label")}>
          {EMAIL_DELIVERY_PERIODS.map((option) => {
            const active = option === period;
            return (
              <Link
                key={option}
                href={withEmailPeriod(pagePath, option)}
                scroll={false}
                className="jp-subnav__item"
                data-active={active}
                aria-current={active ? "true" : undefined}
              >
                {t(`period.${option}`)}
              </Link>
            );
          })}
        </nav>
      </div>

      <div className="flex flex-col gap-4">
        <dl className="jp-adminmailsum">
          <div>
            <dt>{t("summary.sent")}</dt>
            <dd>{value(observed === null ? null : format.number(observed.sent))}</dd>
          </div>
          <div>
            <dt>{t("summary.failed")}</dt>
            <dd>
              {value(observed === null ? null : (
                <span className={observed.failed > 0 ? "jp-admin-danger" : undefined}>{format.number(observed.failed)}</span>
              ))}
            </dd>
          </div>
        </dl>
        {summaryLine === null ? null : <p className="jp-adminsoon">{summaryLine.text}</p>}
        {observed === null ? null : (
          <div className="jp-adminmailsum__note">
            <p>
              {t.rich("delivery.source", {
                at: formatDateTime(format, observed.observedAt) ?? observed.observedAt,
                time: (chunks) => <time dateTime={observed.observedAt}>{chunks}</time>,
              })}
            </p>
          </div>
        )}
      </div>

      <section aria-labelledby={FAILURES_HEADING_ID} className="flex flex-col gap-4">
        <h3 id={FAILURES_HEADING_ID} className="jp-adminh3">
          {t("failures.heading")}
        </h3>
        <AdminTableScroll labelledBy={FAILURES_CAPTION_ID}>
          <table className="jp-table jp-admintable">
            <caption id={FAILURES_CAPTION_ID} className="sr-only">
              {period === null ? t("failures.captionNoPeriod") : t("failures.caption", { period: t(`period.${period}`) })}
            </caption>
            <thead>
              <tr>
                <th scope="col">{t("failures.when")}</th>
                <th scope="col">{t("failures.reason")}</th>
                <th scope="col">{t("failures.recipient")}</th>
              </tr>
            </thead>
            <tbody>
              {failuresBody === null && rows !== null ? (
                // A row has no identity of its own on the page; the provider's id is never read, so its place keys it.
                rows.failures.map((row, index) => {
                  const minute = failureMinute(row.createdAt);
                  return (
                    <tr key={`${minute}-${index}`}>
                      <td className="jp-admintable__when">
                        <time dateTime={minute}>{formatDateTime(format, minute) ?? <AdminUnknown />}</time>
                      </td>
                      <td>{t(`failures.reasons.${row.reason}`)}</td>
                      <td>{row.recipient}</td>
                    </tr>
                  );
                })
              ) : (
                <tr>
                  <td colSpan={3} className="jp-admintable__soon">
                    <p className="jp-adminsoon">{failuresBody}</p>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </AdminTableScroll>
        {rows !== null && rows.failures.length > 0 && rows.failures.length === rows.limit ? (
          <p className="jp-adminmailsum__note">{t("failures.capped", { count: rows.limit })}</p>
        ) : null}
      </section>

      <AdminEmailKinds kinds={kinds} />
    </section>
  );
}
