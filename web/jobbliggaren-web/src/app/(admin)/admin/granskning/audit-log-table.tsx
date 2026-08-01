import { useTranslations } from "next-intl";
import type { AuditLogEntryDto } from "@/lib/types/admin";
import { SWEDISH_TIME_ZONE } from "@/lib/time/swedish-calendar";

interface AuditLogTableProps {
  entries: ReadonlyArray<AuditLogEntryDto>;
}

/**
 * Tabell-vy för audit-log. Pure Server Component — ingen client-JS,
 * ingen state. Civic-utility-konvention: skim-läsbar svensk locale,
 * monospace för UUID-kolumner så ögonen kan jämföra rader.
 */
export function AuditLogTable({ entries }: AuditLogTableProps) {
  // Synchronous next-intl translator — håller AuditLogTable en icke-async RSC.
  const t = useTranslations("admin");
  if (entries.length === 0) {
    return (
      <div
        className="border-y border-border-default px-1 py-12 text-center"
        role="status"
      >
        <p className="text-body text-text-primary">
          {t("audit.table.empty.title")}
        </p>
        <p className="mt-1 text-body-sm text-text-primary">
          {t("audit.table.empty.body")}
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="jp-table w-full" aria-label={t("audit.table.ariaLabel")}>
        <caption className="sr-only">{t("audit.table.caption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("audit.table.occurredAt")}</th>
            <th scope="col">{t("audit.table.user")}</th>
            <th scope="col">{t("audit.table.eventType")}</th>
            <th scope="col">{t("audit.table.aggregate")}</th>
            <th scope="col">{t("audit.table.ipAddress")}</th>
            <th scope="col">{t("audit.table.client")}</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr key={entry.id} className="text-text-primary">
              <td className="whitespace-nowrap jp-admintable__dense font-mono text-text-secondary">
                {formatDateTime(entry.occurredAt)}
              </td>
              <td className="jp-admintable__dense font-mono text-text-secondary">
                {entry.userId ? shortId(entry.userId) : (
                  <span className="text-text-secondary">
                    {t("audit.table.systemUser")}
                  </span>
                )}
              </td>
              <td className="">{entry.eventType}</td>
              <td className="">
                <span>{entry.aggregateType}</span>
                <span className="text-text-tertiary"> · </span>
                <span className="font-mono text-micro leading-4">{shortId(entry.aggregateId)}</span>
              </td>
              <td className="jp-admintable__dense font-mono text-text-secondary">
                {entry.ipAddress ?? (
                  <span className="text-text-tertiary">–</span>
                )}
              </td>
              <td className="max-w-xs truncate text-text-secondary" title={entry.userAgent ?? undefined}>
                {entry.userAgent ?? (
                  <span className="text-text-tertiary">–</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatDateTime(iso: string): string {
  // Svensk locale: YYYY-MM-DD HH:mm:ss (CLAUDE.md §10). Zonen anges explicit så
  // server-tidszon inte påverkar utdata (Server Component renderar på server-side).
  // FE-M4 (design-reviewer 2026-05-11).
  //
  // The zone is SWEDISH_TIME_ZONE rather than a literal of this component's own.
  // The gap that closes is ONE-directional, and saying so precisely is the point:
  // changing the literal here was already caught by this file's own spec, while
  // changing the CONSTANT reached nothing here at all. Both directions measured.
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const formatted = d.toLocaleString("sv-SE", {
    timeZone: SWEDISH_TIME_ZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  });
  // sv-SE-locale ger "YYYY-MM-DD HH:mm:ss" (med Intl.DateTimeFormat), inget komma.
  return formatted;
}

function shortId(uuid: string): string {
  // Första 8 tecken — räcker för att korsverifiera rader i samma sökning.
  return uuid.length >= 8 ? uuid.slice(0, 8) : uuid;
}
