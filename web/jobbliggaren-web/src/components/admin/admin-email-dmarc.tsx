import { Fragment, type ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { dmarcView, type DmarcLine } from "@/lib/admin/email-dmarc";
import type { AdminEmailDmarc as Dmarc, AdminEmailRead } from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminTableScroll } from "./admin-table-scroll";
import { AdminUnknown } from "./admin-unknown";

const HEADING_ID = "admin-email-dmarc";
const CAPTION_ID = "admin-email-dmarc-caption";

interface Said {
  readonly rich: ReactNode;
  readonly plain: string;
}

/**
 * "DMARC-rapporter" on `/admin/e-post` (#1981 PR 4b; ADR 0185 D9, ADR 0150 Amendment (4)): what receiving mail services
 * report about mail sent as the domain, over the region's own fixed window. A third source, kept apart from the
 * provider's figures and from our own handover count, and never a claim about where a mail landed.
 *
 * Every state is one line under the summary, from the one view `dmarcView` builds; an unknown figure is an en-dash, never
 * a zero. The note names the reports as the figures' source and the span they cover. A row is one sending address and
 * header-from domain, the address its row header; nothing a reporter wrote reaches the page beyond those two.
 */
export function AdminEmailDmarc({ read }: { readonly read: AdminEmailRead<Dmarc> }) {
  const t = useTranslations("admin.email.dmarc");
  const admin = useTranslations("admin");
  const format = useFormatter();
  const view = dmarcView(read);
  const shown = (at: string) => formatDateTime(format, at) ?? at;

  function timed(key: "fetched" | "stale" | "signInResumes" | "failure.Incomplete", at: string): Said {
    return {
      rich: t.rich(key, { at: shown(at), time: (chunks) => <time dateTime={at}>{chunks}</time> }),
      plain: t.markup(key, { at: shown(at), time: (chunks) => chunks }),
    };
  }

  function said(line: DmarcLine): Said {
    const plain = (text: string): Said => ({ rich: text, plain: text });
    switch (line.kind) {
      case "loading":
        return plain(admin("regions.loading"));
      case "failed":
        return plain(line.refusal ?? admin("regions.failed"));
      case "notObservable":
        return plain(t("notObservable"));
      case "notConfigured":
        return plain(t("notConfigured"));
      case "misconfigured":
        return plain(t("misconfigured"));
      case "noReports":
        return plain(t("noReports"));
      case "stale":
        return timed("stale", line.since);
      case "conflicting":
        return plain(t("conflicting", { count: line.count }));
      case "mailbox":
        return plain(t(`mailbox.${line.which}`, { count: line.count }));
      case "failure": {
        // dmarcView gives an unfinished first run its start; no other failure has one.
        const reason =
          line.failure === "Incomplete"
            ? timed("failure.Incomplete", line.startedAt ?? "")
            : plain(t(`failure.${line.failure}`));
        const parts = [reason, ...(line.resumesAt === null ? [] : [timed("signInResumes", line.resumesAt)]), plain(t("runbook"))];
        return {
          rich: parts.map((part, index) => (
            <Fragment key={index}>
              {index > 0 ? " " : null}
              {part.rich}
            </Fragment>
          )),
          plain: parts.map((part) => part.plain).join(" "),
        };
      }
    }
  }

  const lines = view.lines.map((line) => ({ line, said: said(line) }));
  const announced = view.announced;
  const note = view.note;
  const value = (figure: number | null, danger = false): ReactNode => {
    if (figure === null) return <AdminUnknown />;
    const text = format.number(figure);
    return danger && figure > 0 ? <span className="jp-admin-danger">{text}</span> : text;
  };

  return (
    <section aria-labelledby={HEADING_ID} className="flex flex-col gap-4">
      <h2 id={HEADING_ID} className="jp-h2">
        {t("heading")}
      </h2>
      {announced === null ? null : (
        <p className="sr-only" role={announced.announce === "status" ? "status" : "alert"}>
          {admin("overview.observation.announcement", { source: t("heading"), state: said(announced).plain })}
        </p>
      )}
      <div className="flex flex-col gap-4">
        <dl className="jp-adminmailsum">
          <div>
            <dt>{t("messages")}</dt>
            <dd>{value(view.figures?.messages ?? null)}</dd>
          </div>
          <div>
            <dt>{t("failing")}</dt>
            <dd>{value(view.figures?.failing ?? null, true)}</dd>
          </div>
        </dl>
        {lines.map(({ line, said: text }) => (
          <p key={line.kind === "mailbox" ? `mailbox-${line.which}` : line.kind} className="jp-adminsoon">
            {text.rich}
          </p>
        ))}
        {note === null ? null : (
          <div className="jp-adminmailsum__note">
            <p>
              {t.rich("note", {
                reports: note.reports,
                from: shown(note.from),
                to: shown(note.to),
                start: (chunks) => <time dateTime={note.from}>{chunks}</time>,
                end: (chunks) => <time dateTime={note.to}>{chunks}</time>,
              })}
              {note.fetchedAt === null ? null : <> {timed("fetched", note.fetchedAt).rich}</>}
            </p>
            <p>{t("fixed")}</p>
          </div>
        )}
      </div>
      <AdminTableScroll labelledBy={CAPTION_ID}>
        <table className="jp-table jp-admintable">
          <caption id={CAPTION_ID} className="sr-only">
            {note === null ? t("captionUnobserved") : t("caption", { from: shown(note.from), to: shown(note.to) })}
          </caption>
          <thead>
            <tr>
              <th scope="col">{t("address")}</th>
              <th scope="col">{t("headerFrom")}</th>
              <th scope="col" className="jp-admintable__num">
                {t("messages")}
              </th>
              <th scope="col" className="jp-admintable__num">
                {t("dkim")}
              </th>
              <th scope="col" className="jp-admintable__num">
                {t("spf")}
              </th>
              <th scope="col" className="jp-admintable__num">
                {t("failing")}
              </th>
            </tr>
          </thead>
          <tbody>
            {view.rows === null ? (
              <tr>
                <td colSpan={6} className="jp-admintable__soon">
                  <p className="jp-adminsoon">
                    {view.emptyRow === "loading" ? admin("regions.loading") : admin("unavailable.unknownSpoken")}
                  </p>
                </td>
              </tr>
            ) : (
              view.rows.map((row) => (
                <tr key={row.key}>
                  <th scope="row">
                    <span className="jp-admindmarc__addr">
                      {row.addressParts.map((part, index) => (
                        <Fragment key={index}>
                          {index > 0 ? <wbr /> : null}
                          {part}
                        </Fragment>
                      ))}
                    </span>
                    {row.sharesAddress ? <span className="sr-only">{t("rowDomain", { domain: row.headerFrom })}</span> : null}
                  </th>
                  <td className="jp-admindmarc__domain">{row.headerFrom}</td>
                  <td className="jp-admintable__num">{format.number(row.messages)}</td>
                  <td className="jp-admintable__num">{format.number(row.dkimAligned)}</td>
                  <td className="jp-admintable__num">{format.number(row.spfAligned)}</td>
                  <td className="jp-admintable__num">{value(row.failingDmarc, true)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </AdminTableScroll>
      {view.cap === null ? null : <p className="jp-adminmailsum__note">{t("capped", view.cap)}</p>}
    </section>
  );
}
