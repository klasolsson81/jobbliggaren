import { useFormatter, useTranslations } from "next-intl";
import type { EmailRegionLine } from "@/lib/admin/email-delivery";
import type { AdminEmailKinds as Kinds, AdminEmailRead } from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminTableScroll } from "./admin-table-scroll";

const HEADING_ID = "admin-email-kinds";
const CAPTION_ID = "admin-email-kinds-caption";

/**
 * What the per-kind region says in place of its rows, and whether Leverans announces it; null while it shows its rows.
 * The region and the section's announcement read the same line.
 */
export function useEmailKindsLine(read: AdminEmailRead<Kinds>): EmailRegionLine | null {
  const t = useTranslations("admin.email.kinds");
  const lines = useTranslations("admin.email.allowance");
  const admin = useTranslations("admin");
  if (read.kind === "loading") return { text: admin("regions.loading"), announce: "status" };
  if (read.kind === "failed") return { text: read.line ?? admin("regions.failed"), announce: "alert" };
  switch (read.data.status) {
    case "Observed":
      return null;
    case "NotObserved":
      return { text: lines("notConfigured"), announce: null };
    case "NotObservable":
      return { text: t("notObservable"), announce: "alert" };
  }
}

/**
 * "Utskick per mejltyp" inside Leverans (#1981 PR 3; ADR 0150 D2, D4): our own count of handovers to the provider for
 * each of the seven mail kinds in the period, with the failures and the latest day one failed.
 *
 * The rows exist only from an observed read: all seven, in the API's order, each named in a row header. A kind with no
 * failure says so in words, since an en-dash would mean unknown. Every other state is one line in the table, never a
 * zero: a deployment without the provider does not count, and a count with no sends in a period the provider sent mail
 * in could not be told from a count that stopped. The note says whose count this is and since when; a count that began
 * inside the period names when it began, so the figures are never read as the whole period's.
 *
 * The region's lines are quiet: Leverans announces them, with this region as their source.
 */
export function AdminEmailKinds({ kinds }: { readonly kinds: AdminEmailRead<Kinds> }) {
  const t = useTranslations("admin.email.kinds");
  const format = useFormatter();
  const line = useEmailKindsLine(kinds);
  const observed = kinds.kind === "loaded" && kinds.data.status === "Observed" ? kinds.data : null;
  // A count that began inside the period, shown to the minute in the page's time zone.
  const began = observed?.coveredFrom ?? null;
  const start = began === null ? null : { at: began, shown: formatDateTime(format, began) ?? began };

  // The table's name never repeats the section's: without counts it names what the table holds, with them their span.
  function caption(): string {
    if (observed === null) return t("captionUnobserved");
    return start === null ? t("caption", { from: observed.from }) : t("captionPartial", { start: start.shown });
  }

  return (
    <section aria-labelledby={HEADING_ID} className="flex flex-col gap-4">
      <h3 id={HEADING_ID} className="jp-adminh3">
        {t("heading")}
      </h3>
      {observed === null ? null : (
        <div className="jp-adminmailsum__note">
          <p>
            {start === null
              ? t.rich("note", {
                  from: observed.from,
                  time: (chunks) => <time dateTime={observed.from}>{chunks}</time>,
                })
              : t.rich("notePartial", {
                  start: start.shown,
                  time: (chunks) => <time dateTime={start.at}>{chunks}</time>,
                })}
          </p>
        </div>
      )}
      <AdminTableScroll labelledBy={CAPTION_ID}>
        <table className="jp-table jp-admintable">
          <caption id={CAPTION_ID} className="sr-only">
            {caption()}
          </caption>
          <thead>
            <tr>
              <th scope="col">{t("kind")}</th>
              <th scope="col" className="jp-admintable__num">
                {t("accepted")}
              </th>
              <th scope="col" className="jp-admintable__num">
                {t("failed")}
              </th>
              <th scope="col">{t("latestFailure")}</th>
            </tr>
          </thead>
          <tbody>
            {observed === null ? (
              <tr>
                <td colSpan={4} className="jp-admintable__soon">
                  <p className="jp-adminsoon">{line?.text}</p>
                </td>
              </tr>
            ) : (
              observed.kinds.map((row) => {
                const latest = row.latestFailure;
                const failed = format.number(row.failed);
                return (
                  <tr key={row.kind}>
                    <th scope="row">{t(`names.${row.kind}`)}</th>
                    <td className="jp-admintable__num">{format.number(row.accepted)}</td>
                    <td className="jp-admintable__num">
                      {row.failed > 0 ? <span className="jp-admin-danger">{failed}</span> : failed}
                    </td>
                    <td>
                      {latest === null
                        ? t("noFailure")
                        : t.rich("failedOn", {
                            day: latest.day,
                            outcomes: format.list(
                              latest.outcomes.map((outcome) => t(`outcomes.${outcome}`)),
                              { type: "conjunction" },
                            ),
                            time: (chunks) => <time dateTime={latest.day}>{chunks}</time>,
                          })}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </AdminTableScroll>
    </section>
  );
}
