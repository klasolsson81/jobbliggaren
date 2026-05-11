import type { AuditLogEntryDto } from "@/lib/types/admin";

interface AuditLogTableProps {
  entries: ReadonlyArray<AuditLogEntryDto>;
}

/**
 * Tabell-vy för audit-log. Pure Server Component — ingen client-JS,
 * ingen state. Civic-utility-konvention: skim-läsbar svensk locale,
 * monospace för UUID-kolumner så ögonen kan jämföra rader.
 */
export function AuditLogTable({ entries }: AuditLogTableProps) {
  if (entries.length === 0) {
    return (
      <div
        className="rounded-md border border-border bg-surface-secondary px-6 py-10 text-center"
        role="status"
      >
        <p className="text-body text-text-secondary">Inga poster matchar filtret</p>
        <p className="mt-1 text-body-sm text-text-tertiary">
          Justera filterkriterierna eller rensa för att visa hela loggen.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full text-body-sm" aria-label="Granskningsposter">
        <caption className="sr-only">
          Granskningsposter sorterade efter tidpunkt, senaste först.
        </caption>
        <thead className="bg-surface-secondary">
          <tr className="text-left text-text-secondary">
            <th scope="col" className="px-3 py-2 font-medium">
              Tidpunkt
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Användare
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Händelse
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Aggregat
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              IP
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Klient
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {entries.map((entry) => (
            <tr key={entry.id} className="text-text-primary">
              <td className="px-3 py-2 whitespace-nowrap font-mono text-xs">
                {formatDateTime(entry.occurredAt)}
              </td>
              <td className="px-3 py-2 font-mono text-xs">
                {entry.userId ? shortId(entry.userId) : (
                  <span className="text-text-tertiary">system</span>
                )}
              </td>
              <td className="px-3 py-2">{entry.eventType}</td>
              <td className="px-3 py-2">
                <span>{entry.aggregateType}</span>
                <span className="text-text-tertiary"> · </span>
                <span className="font-mono text-xs">{shortId(entry.aggregateId)}</span>
              </td>
              <td className="px-3 py-2 font-mono text-xs">
                {entry.ipAddress ?? (
                  <span className="text-text-tertiary">—</span>
                )}
              </td>
              <td className="px-3 py-2 max-w-xs truncate text-text-secondary" title={entry.userAgent ?? undefined}>
                {entry.userAgent ?? (
                  <span className="text-text-tertiary">—</span>
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
  // Svensk locale: YYYY-MM-DD HH:mm:ss (CLAUDE.md §10.2). Explicit Europe/Stockholm
  // så server-tidszon inte påverkar utdata (Server Component renderar på server-side).
  // FE-M4 (design-reviewer 2026-05-11).
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const formatted = d.toLocaleString("sv-SE", {
    timeZone: "Europe/Stockholm",
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
