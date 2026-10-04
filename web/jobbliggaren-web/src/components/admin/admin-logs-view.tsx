import type { ReactNode } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { formatDateTime } from "@/lib/i18n/format";
import type {
  AdminErrorLogRow,
  AdminImportLogRow,
  AdminRegion,
  AdminSecurityEventKind,
  AdminSecurityLogRow,
} from "@/lib/admin/view-models";
import { AdminPageHeader } from "./admin-page-header";
import { AdminRegionLine } from "./admin-region-line";
import { AdminTableScroll } from "./admin-table-scroll";

export type AdminLogView = "security" | "errors" | "imports";

/** One view's rows; the view names the row shape. */
export type AdminLogData =
  | { readonly view: "security"; readonly region: AdminRegion<ReadonlyArray<AdminSecurityLogRow>> }
  | { readonly view: "errors"; readonly region: AdminRegion<ReadonlyArray<AdminErrorLogRow>> }
  | { readonly view: "imports"; readonly region: AdminRegion<ReadonlyArray<AdminImportLogRow>> };

const SECURITY_TONE: Readonly<Record<AdminSecurityEventKind, string>> = {
  loginFailed: "jp-pill--warning",
  rateLimited: "jp-pill--danger",
  accountLocked: "jp-pill--danger",
  adminImpersonation: "jp-pill--info",
};

/**
 * Each view is its own route, so the view switcher is the house `.jp-subnav` (links with
 * `aria-current`, no client JavaScript) rather than an ARIA tablist (ADR 0150 D8).
 */
const VIEWS = {
  security: {
    path: "/loggar",
    columns: [
      "security.time",
      "security.event",
      "security.account",
      "security.ip",
      "security.count",
      "security.detail",
    ],
  },
  errors: {
    path: "/loggar/applikationsfel",
    columns: [
      "errors.lastSeen",
      "errors.level",
      "errors.source",
      "errors.message",
      "errors.count24h",
    ],
  },
  imports: {
    path: "/loggar/platsbanken-import",
    columns: [
      "imports.run",
      "imports.type",
      "imports.start",
      "imports.duration",
      "imports.fetched",
      "imports.added",
      "imports.updated",
      "imports.closed",
      "imports.status",
    ],
  },
} as const;

const VIEW_ORDER: ReadonlyArray<AdminLogView> = ["security", "errors", "imports"];

const NUMERIC_COLUMNS: ReadonlySet<string> = new Set([
  "security.count",
  "errors.count24h",
  "imports.fetched",
  "imports.added",
  "imports.updated",
  "imports.closed",
]);

/**
 * One of the three log views (ADR 0150). The logs themselves are #1980: until then each view's
 * table keeps its column structure and holds one "Kommer snart" row, and the view labels carry
 * no counts (D2). Given rows, the table shows them, or the region's one line instead.
 */
export function AdminLogsView({
  view,
  basePath = "/admin",
  data,
  counts,
}: {
  readonly view: AdminLogView;
  /** "/admin", or the local preview's own root (ADR 0150 D5). */
  readonly basePath?: string;
  /** This view's rows; absent while the logs are not built. */
  readonly data?: AdminLogData;
  /** Each view's count, shown in its label only when known. */
  readonly counts?: Readonly<Record<AdminLogView, number>>;
}) {
  const t = useTranslations("admin.logs");
  const captionId = `admin-logs-${view}-caption`;
  const { columns } = VIEWS[view];

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />

      <div>
        <nav className="jp-subnav" aria-label={t("subnav.label")}>
          {VIEW_ORDER.map((candidate) => {
            const active = candidate === view;
            return (
              <Link
                key={candidate}
                href={basePath + VIEWS[candidate].path}
                className="jp-subnav__item"
                data-active={active}
                aria-current={active ? "page" : undefined}
              >
                {counts === undefined
                  ? t(`subnav.${candidate}`)
                  : t("subnav.count", { label: t(`subnav.${candidate}`), count: counts[candidate] })}
              </Link>
            );
          })}
        </nav>

        <AdminTableScroll labelledBy={captionId}>
          <table className="jp-table jp-admintable">
            <caption id={captionId} className="sr-only">
              {t(`${view}.caption`)}
            </caption>
            <thead>
              <tr>
                {columns.map((column) => (
                  <th key={column} scope="col" className={NUMERIC_COLUMNS.has(column) ? "jp-admintable__num" : undefined}>
                    {t(column)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <LogRows data={data?.view === view ? data : undefined} columns={columns.length} />
            </tbody>
          </table>
        </AdminTableScroll>
      </div>
    </div>
  );
}

function LogRows({ data, columns }: { readonly data: AdminLogData | undefined; readonly columns: number }) {
  const t = useTranslations("admin.logs");
  const line = (region: AdminRegion<ReadonlyArray<unknown>>, empty: string): ReactNode =>
    region.kind === "loaded" && region.data.length > 0 ? null : (
      <tr>
        <td colSpan={columns} className="jp-admintable__soon">
          <AdminRegionLine kind={region.kind === "loaded" ? "empty" : region.kind} empty={empty} />
        </td>
      </tr>
    );

  if (data === undefined) return line({ kind: "unavailable" }, "");
  switch (data.view) {
    case "security":
      return data.region.kind === "loaded" && data.region.data.length > 0
        ? data.region.data.map((row) => <SecurityRow key={row.id} row={row} />)
        : line(data.region, t("security.empty"));
    case "errors":
      return data.region.kind === "loaded" && data.region.data.length > 0
        ? data.region.data.map((row) => <ErrorRow key={row.id} row={row} />)
        : line(data.region, t("errors.empty"));
    case "imports":
      return data.region.kind === "loaded" && data.region.data.length > 0
        ? data.region.data.map((row) => <ImportRow key={row.id} row={row} />)
        : line(data.region, t("imports.empty"));
  }
}

function SecurityRow({ row }: { readonly row: AdminSecurityLogRow }) {
  const t = useTranslations("admin.logs.security");
  const format = useFormatter();
  const dash = useTranslations("admin.unavailable")("unknownValue");
  return (
    <tr>
      <td className="jp-admintable__when">{formatDateTime(format, row.occurredAt) ?? dash}</td>
      <td>
        <span className={`jp-pill ${SECURITY_TONE[row.kind]}`}>{t(`kind.${row.kind}`)}</span>
      </td>
      <td>{row.account}</td>
      <td>{row.ip}</td>
      <td className="jp-admintable__num">{format.number(row.count)}</td>
      <td>{row.detail}</td>
    </tr>
  );
}

function ErrorRow({ row }: { readonly row: AdminErrorLogRow }) {
  const t = useTranslations("admin.logs.errors");
  const format = useFormatter();
  const dash = useTranslations("admin.unavailable")("unknownValue");
  return (
    <tr>
      <td className="jp-admintable__when">{formatDateTime(format, row.lastSeenAt) ?? dash}</td>
      <td>
        <span className={row.level === "error" ? "jp-pill jp-pill--danger" : "jp-pill jp-pill--warning"}>
          {t(`levelValue.${row.level}`)}
        </span>
      </td>
      <td>
        <code>{row.source}</code>
      </td>
      <td>{row.message}</td>
      <td className="jp-admintable__num">{format.number(row.count24h)}</td>
    </tr>
  );
}

function ImportRow({ row }: { readonly row: AdminImportLogRow }) {
  const t = useTranslations("admin.logs.imports");
  const format = useFormatter();
  const dash = useTranslations("admin.unavailable")("unknownValue");
  return (
    <tr>
      <td className="jp-admintable__run">{row.run}</td>
      <td>{t(`kind.${row.kind}`)}</td>
      <td className="jp-admintable__when">{formatDateTime(format, row.startedAt) ?? dash}</td>
      <td className="jp-admintable__when">
        {t("durationValue", { minutes: Math.floor(row.durationSeconds / 60), seconds: row.durationSeconds % 60 })}
      </td>
      <td className="jp-admintable__num">{format.number(row.fetched)}</td>
      <td className="jp-admintable__num">{format.number(row.added)}</td>
      <td className="jp-admintable__num">{format.number(row.updated)}</td>
      <td className="jp-admintable__num">{format.number(row.closed)}</td>
      <td>
        <span className={row.status === "succeeded" ? "jp-pill jp-pill--success" : "jp-pill jp-pill--danger"}>
          {t(`statusValue.${row.status}`)}
        </span>
      </td>
    </tr>
  );
}
