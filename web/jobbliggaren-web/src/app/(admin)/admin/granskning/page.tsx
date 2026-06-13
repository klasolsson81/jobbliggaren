import { getAuditLog } from "@/lib/api/admin";
import { AuditLogFilter } from "./audit-log-filter";
import { AuditLogTable } from "./audit-log-table";
import { AuditLogPagination } from "./audit-log-pagination";

type Params = {
  page?: string;
  pageSize?: string;
  from?: string;
  to?: string;
  userId?: string;
  eventType?: string;
  aggregateType?: string;
};

interface PageProps {
  searchParams: Promise<Params>;
}

const DEFAULT_PAGE_SIZE = 50;

export default async function GranskningPage({ searchParams }: PageProps) {
  const params = await searchParams;

  const page = parsePositiveInt(params.page, 1);
  // Klampa pageSize till backend-validator-takets max (200). Defense-in-depth —
  // backend FluentValidation kastar 400 vid >200 men FE bör inte ens skicka det.
  // FE-M5 (code-reviewer 2026-05-11).
  const pageSize = Math.min(
    parsePositiveInt(params.pageSize, DEFAULT_PAGE_SIZE),
    200
  );

  // datetime-local-input ger "YYYY-MM-DDTHH:mm" — backend förväntar ISO med sekund.
  const from = toIsoOrUndefined(params.from);
  const to = toIsoOrUndefined(params.to);

  const result = await getAuditLog({
    page,
    pageSize,
    from,
    to,
    userId: emptyToUndefined(params.userId),
    eventType: emptyToUndefined(params.eventType),
    aggregateType: emptyToUndefined(params.aggregateType),
  });

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="jp-h1">Granskning</h1>
        <p className="jp-lede">
          Granskningslogg över alla skrivande operationer. Senaste posten först.
        </p>
      </div>

      <AuditLogFilter
        current={{
          from: params.from,
          to: params.to,
          userId: params.userId,
          eventType: params.eventType,
          aggregateType: params.aggregateType,
        }}
      />

      {result.kind === "ok" ? (
        <>
          <AuditLogTable entries={result.data.items} />
          <AuditLogPagination
            page={result.data.page}
            totalPages={result.data.totalPages}
            totalCount={result.data.totalCount}
            buildHref={(targetPage) => buildPageHref(params, targetPage)}
          />
        </>
      ) : result.kind === "rateLimited" ? (
        <ErrorBlock kind="rateLimited" retryAfterSeconds={result.retryAfterSeconds} />
      ) : (
        <ErrorBlock kind={result.kind} />
      )}
    </div>
  );
}

type ErrorKind = "forbidden" | "unauthorized" | "notFound" | "rateLimited" | "error";

function ErrorBlock({
  kind,
  retryAfterSeconds,
}: {
  kind: ErrorKind;
  retryAfterSeconds?: number;
}) {
  // notFound och error har identisk copy — list-endpointen kan aldrig
  // runtime-faktiskt returnera 404 (responseToResult sätter inte
  // includeNotFound), men ApiResult-typen kräver case för exhaustiveness
  // (ADR 0030 §3).
  const messages: Record<ErrorKind, { title: string; body: string }> = {
    forbidden: {
      title: "Saknar behörighet",
      body: "Din session saknar Admin-rollen. Kontakta systemansvarig om du behöver åtkomst.",
    },
    unauthorized: {
      title: "Inte inloggad",
      body: "Logga in och försök igen.",
    },
    notFound: {
      title: "Kunde inte ladda granskningsloggen",
      body: "Försök igen om en stund. Kontakta drift om felet kvarstår.",
    },
    rateLimited: {
      title: "För många förfrågningar",
      body: `Du har gjort för många förfrågningar på kort tid. Försök igen om ${
        retryAfterSeconds ?? 60
      } sekunder.`,
    },
    error: {
      title: "Kunde inte ladda granskningsloggen",
      body: "Försök igen om en stund. Kontakta drift om felet kvarstår.",
    },
  };

  const m = messages[kind];
  return (
    <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
      <p className="text-body font-medium">{m.title}</p>
      <p className="mt-1 text-body-sm">{m.body}</p>
    </div>
  );
}

function parsePositiveInt(raw: string | undefined, fallback: number): number {
  if (!raw) return fallback;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

function emptyToUndefined(s: string | undefined): string | undefined {
  return s && s.trim().length > 0 ? s.trim() : undefined;
}

function toIsoOrUndefined(s: string | undefined): string | undefined {
  if (!s || s.trim().length === 0) return undefined;
  // "YYYY-MM-DDTHH:mm" → add seconds + Z för UTC. Servern förväntar ISO 8601.
  const m = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2})$/.exec(s);
  if (m) return `${m[1]}:00Z`;
  return s;
}

function buildPageHref(params: Params, page: number): string {
  const url = new URLSearchParams();
  if (page !== 1) url.set("page", String(page));
  if (params.pageSize) url.set("pageSize", params.pageSize);
  if (params.from) url.set("from", params.from);
  if (params.to) url.set("to", params.to);
  if (params.userId) url.set("userId", params.userId);
  if (params.eventType) url.set("eventType", params.eventType);
  if (params.aggregateType) url.set("aggregateType", params.aggregateType);
  const q = url.toString();
  return q.length > 0 ? `/admin/granskning?${q}` : "/admin/granskning";
}
