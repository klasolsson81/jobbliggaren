import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "./admin-page-header";
import { AdminTableScroll } from "./admin-table-scroll";
import { ComingSoon } from "./coming-soon";

export type AdminLogView = "security" | "errors" | "imports";

/**
 * Each view is its own route, so the view switcher is the house `.jp-subnav` (links with
 * `aria-current`, no client JavaScript) rather than an ARIA tablist (ADR 0150 D8).
 */
const VIEWS = {
  security: {
    href: "/admin/loggar",
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
    href: "/admin/loggar/applikationsfel",
    columns: [
      "errors.lastSeen",
      "errors.level",
      "errors.source",
      "errors.message",
      "errors.count24h",
    ],
  },
  imports: {
    href: "/admin/loggar/platsbanken-import",
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

/**
 * One of the three log views (ADR 0150). The logs themselves are #1980: until then each view's
 * table keeps its column structure and holds one "Kommer snart" row, and the view labels carry
 * no counts (D2).
 */
export async function AdminLogsView({ view }: { readonly view: AdminLogView }) {
  const t = await getTranslations("admin.logs");
  const captionId = `admin-logs-${view}-caption`;
  const { columns } = VIEWS[view];

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} lede={t("lede")} />

      <div>
        <nav className="jp-subnav" aria-label={t("subnav.label")}>
          {VIEW_ORDER.map((candidate) => {
            const active = candidate === view;
            return (
              <Link
                key={candidate}
                href={VIEWS[candidate].href}
                className="jp-subnav__item"
                data-active={active}
                aria-current={active ? "page" : undefined}
              >
                {t(`subnav.${candidate}`)}
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
                  <th key={column} scope="col">
                    {t(column)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr>
                <td colSpan={columns.length} className="jp-admintable__soon">
                  <ComingSoon />
                </td>
              </tr>
            </tbody>
          </table>
        </AdminTableScroll>
      </div>
    </div>
  );
}
