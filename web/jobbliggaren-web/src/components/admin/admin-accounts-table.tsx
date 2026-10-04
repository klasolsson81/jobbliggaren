"use client";

// "use client": its sort and open buttons carry click handlers.
import { useFormatter, useTranslations } from "next-intl";
import { ArrowDown, ArrowUp, UserRound } from "lucide-react";
import type {
  AdminAccountRow,
  AdminAccountSort,
  AdminAccountSortKey,
  AdminRegion,
} from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminAccountStatusCell, AdminRolePill } from "./admin-account-status";
import { AdminTableScroll } from "./admin-table-scroll";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";

const CAPTION_ID = "admin-users-caption";
const COLUMN_COUNT = 7;

interface AdminAccountsTableProps {
  readonly region: AdminRegion<ReadonlyArray<AdminAccountRow>>;
  readonly sort?: AdminAccountSort;
  /** Absent while the region is not built: the sort buttons are then disabled. */
  readonly onSort?: (key: AdminAccountSortKey) => void;
  readonly selectedId?: string | null;
  /** Absent while there is no panel to open: the account is then plain text. */
  readonly onOpen?: (id: string) => void;
  /** The id of the unavailable row's line, for the toolbar's disabled controls to point to. */
  readonly soonId?: string;
  /** True while newer rows are on their way: the rows shown stay until they arrive. */
  readonly busy?: boolean;
  /** The failed region's sentence, when the caller knows more than that the read failed. */
  readonly failedMessage?: string;
}

/**
 * The account ledger (ADR 0150 D2/D3). An account names itself by its address; "Senast inloggad"
 * and "Senast aktiv" have no source, so they read as unknown on every row (D8). A row opens the
 * panel through a button in its first cell, never through a clickable `<tr>`.
 */
export function AdminAccountsTable({
  region: given,
  sort,
  onSort,
  selectedId = null,
  onOpen,
  soonId,
  busy = false,
  failedMessage,
}: AdminAccountsTableProps) {
  const region = given.kind === "loaded" && given.data.length === 0 ? ({ kind: "empty" } as const) : given;
  const t = useTranslations("admin.users");
  const format = useFormatter();

  function sortHeader(key: AdminAccountSortKey, label: string) {
    const active = sort?.key === key;
    const Arrow = active && sort.direction === "descending" ? ArrowDown : ArrowUp;
    return (
      <th scope="col" aria-sort={active ? sort.direction : undefined}>
        <button
          type="button"
          className="jp-adminusers__sortbtn"
          disabled={onSort === undefined}
          aria-describedby={onSort === undefined ? soonId : undefined}
          onClick={() => onSort?.(key)}
        >
          {label}
          {active ? <Arrow size={14} aria-hidden="true" /> : null}
        </button>
      </th>
    );
  }

  return (
    <AdminTableScroll labelledBy={CAPTION_ID}>
      <table
        className="jp-table jp-admintable jp-adminusers"
        aria-busy={region.kind === "loading" || busy || undefined}
      >
        <caption id={CAPTION_ID} className="sr-only">
          {t("table.caption")}
        </caption>
        <thead>
          <tr>
            {sortHeader("email", t("table.account"))}
            <th scope="col">{t("table.role")}</th>
            <th scope="col">{t("table.status")}</th>
            {sortHeader("registeredAt", t("table.registered"))}
            <th scope="col">{t("table.lastLogin")}</th>
            <th scope="col">{t("table.lastActive")}</th>
            <th scope="col" className="jp-adminusers__num">
              {t("table.applications")}
            </th>
          </tr>
        </thead>
        <tbody>
          {region.kind === "loaded" ? (
            region.data.map((row) => (
              <tr key={row.id} data-selected={row.id === selectedId || undefined}>
                <td>
                  {onOpen === undefined ? (
                    <span className="jp-adminusers__account">
                      <UserRound size={18} aria-hidden="true" />
                      <span className="jp-adminusers__email">{row.email ?? <AdminUnknown />}</span>
                    </span>
                  ) : (
                    <button
                      type="button"
                      className="jp-adminusers__account jp-adminusers__open"
                      aria-haspopup="dialog"
                      onClick={() => onOpen(row.id)}
                    >
                      <UserRound size={18} aria-hidden="true" />
                      <span className="jp-adminusers__email">{row.email ?? <AdminUnknown />}</span>
                    </button>
                  )}
                </td>
                <td>
                  <AdminRolePill role={row.role} />
                </td>
                <td>
                  <AdminAccountStatusCell
                    status={row.status}
                    deletionEarliest={row.deletionEarliest}
                    emailConfirmed={row.emailConfirmed}
                  />
                </td>
                <td className="jp-adminusers__when">
                  {formatDateTime(format, row.registeredAt) ?? <AdminUnknown />}
                </td>
                <td className="jp-adminusers__when">
                  <AdminUnknown />
                </td>
                <td className="jp-adminusers__when">
                  <AdminUnknown />
                </td>
                <td className="jp-adminusers__num">
                  {row.applicationCount === null ? <AdminUnknown /> : format.number(row.applicationCount)}
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td colSpan={COLUMN_COUNT} className="jp-admintable__soon">
                <AdminRegionLine
                  kind={region.kind}
                  empty={t("regions.empty")}
                  failed={failedMessage ?? t("regions.failed")}
                  loading={t("regions.loading")}
                  soonId={soonId}
                />
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </AdminTableScroll>
  );
}
