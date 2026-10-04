"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import type { AdminAccountDetail, AdminAccountRow, AdminRegion } from "@/lib/admin/view-models";
import {
  ADMIN_ACCOUNT_FILTERS,
  AdminAccountsToolbar,
  type AdminAccountFilter,
} from "@/components/admin/admin-accounts-toolbar";
import {
  AdminAccountsTable,
  type AdminAccountSort,
  type AdminAccountSortKey,
} from "@/components/admin/admin-accounts-table";
import { AdminAccountsPager, AdminAccountsSummary } from "@/components/admin/admin-accounts-pager";
import {
  AdminAccountPanel,
  type AdminAccountCommand,
  type AdminCommandRefusal,
  type AdminLiveAction,
} from "@/components/admin/admin-account-panel";
import { usePreviewState } from "./preview-shell.preview";

const PAGE_SIZE = 10;
const SOON_ID = "admin-preview-users-soon";
/** The MVP's account actions (#1975–#1977), shown working here with fictional data. */
const LIVE: ReadonlySet<AdminLiveAction> = new Set(["changeEmail", "suspend", "reinstate", "scheduleDeletion"]);
/** Long enough for the pending state to show. */
const SIMULATED_LATENCY_MS = 400;

function compare(sort: AdminAccountSort) {
  const sign = sort.direction === "ascending" ? 1 : -1;
  return (a: AdminAccountRow, b: AdminAccountRow) =>
    sort.key === "email"
      ? sign * a.email.localeCompare(b.email, "sv")
      : sign * (a.registeredAt ?? "").localeCompare(b.registeredAt ?? "");
}

function applyCommand(
  account: AdminAccountDetail,
  command: AdminAccountCommand,
  deletionEarliest: string,
): AdminAccountDetail {
  switch (command.kind) {
    case "suspend":
      return { ...account, status: "suspended" };
    case "reinstate":
      return { ...account, status: "active" };
    case "scheduleDeletion":
      return { ...account, status: "pendingDeletion", deletionEarliest };
    case "changeEmail":
      // A request, not a change: the address changes when the owner confirms.
      return account;
  }
}

/** The account list and panel over the fixtures, in the state the band chose. */
export function PreviewAccounts({
  accounts,
  adminEmail,
  deletionEarliest,
}: {
  readonly accounts: ReadonlyArray<AdminAccountDetail>;
  readonly adminEmail: string;
  readonly deletionEarliest: string;
}) {
  const t = useTranslations("admin.users");
  const { kind } = usePreviewState();
  const [rows, setRows] = useState(accounts);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<AdminAccountFilter>("all");
  const [sort, setSort] = useState<AdminAccountSort>({ key: "registeredAt", direction: "descending" });
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);

  const matching = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return needle === "" ? rows : rows.filter((row) => row.email.toLowerCase().includes(needle));
  }, [rows, query]);

  const counts = useMemo(
    () =>
      Object.fromEntries(
        ADMIN_ACCOUNT_FILTERS.map((value) => [
          value,
          value === "all" ? matching.length : matching.filter((row) => row.status === value).length,
        ]),
      ) as Record<AdminAccountFilter, number>,
    [matching],
  );

  const filtered = filter === "all" ? matching : matching.filter((row) => row.status === filter);
  const sorted = [...filtered].sort(compare(sort));
  const pages = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const current = Math.min(page, pages);
  const pageRows = sorted.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE);
  const loaded = kind === "loaded";
  const live = kind !== "unavailable";

  const region: AdminRegion<ReadonlyArray<AdminAccountRow>> = !loaded
    ? { kind }
    : pageRows.length === 0
      ? { kind: "empty" }
      : { kind: "loaded", data: pageRows };

  function toggleSort(key: AdminAccountSortKey) {
    setSort((previous) =>
      previous.key === key
        ? { key, direction: previous.direction === "ascending" ? "descending" : "ascending" }
        : { key, direction: key === "email" ? "ascending" : "descending" },
    );
  }

  async function command(
    account: AdminAccountDetail,
    next: AdminAccountCommand,
  ): Promise<AdminCommandRefusal> {
    await new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
    if (account.email === adminEmail) return t("refusal.ownAccount");
    setRows((previous) =>
      previous.map((row) => (row.id === account.id ? applyCommand(row, next, deletionEarliest) : row)),
    );
    return null;
  }

  return (
    <>
      <AdminAccountsToolbar
        filter={filter}
        query={query}
        onQueryChange={
          live
            ? (value) => {
                setQuery(value);
                setPage(1);
              }
            : undefined
        }
        onFilterChange={
          live
            ? (value) => {
                setFilter(value);
                setPage(1);
              }
            : undefined
        }
        counts={loaded ? counts : undefined}
        soonId={SOON_ID}
      />
      {loaded ? <AdminAccountsSummary shown={filtered.length} total={rows.length} /> : null}
      <AdminAccountsTable
        region={region}
        sort={sort}
        onSort={loaded ? toggleSort : undefined}
        selectedId={openId}
        onOpen={loaded ? setOpenId : undefined}
        soonId={SOON_ID}
      />
      {loaded ? <AdminAccountsPager page={current} pages={pages} onPage={setPage} /> : null}
      <AdminAccountPanel
        account={loaded ? (rows.find((row) => row.id === openId) ?? null) : null}
        onClose={() => setOpenId(null)}
        live={LIVE}
        onCommand={command}
        deletionEarliestIfScheduledNow={deletionEarliest}
      />
    </>
  );
}
