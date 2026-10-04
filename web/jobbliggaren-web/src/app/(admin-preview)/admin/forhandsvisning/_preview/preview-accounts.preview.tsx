"use client";

// "use client": the list's search, filter, sort, pages and commands run in memory.
import { useMemo, useState } from "react";
import {
  listRegion,
  type AdminAccountDetail,
  type AdminAccountRow,
  type AdminAccountSort,
  type AdminAccountSortKey,
  type AdminAddressedAccount,
  type AdminRegion,
} from "@/lib/admin/view-models";
import { AdminAccountsToolbar, type AdminAccountFilter } from "@/components/admin/admin-accounts-toolbar";
import { AdminAccountsTable } from "@/components/admin/admin-accounts-table";
import { AdminAccountsPager, AdminAccountsSummary } from "@/components/admin/admin-accounts-pager";
import {
  AdminAccountPanel,
  type AdminAccountCommand,
  type AdminCommandRefusal,
  type AdminLiveAction,
} from "@/components/admin/admin-account-panel";
import { useTranslations } from "next-intl";
import { usePreviewState } from "./preview-shell.preview";

const PAGE_SIZE = 10;
const SOON_ID = "admin-preview-users-soon";
/** The MVP's account actions (#1975–#1977), shown working here with fictional data. */
const LIVE: ReadonlySet<AdminLiveAction> = new Set(["changeEmail", "suspend", "reinstate", "scheduleDeletion"]);
/** The production filters, and Suspenderade, which the suspend flow here can fill. */
const FILTERS: ReadonlyArray<AdminAccountFilter> = ["all", "active", "suspended", "pendingDeletion", "profileMissing"];
/** Long enough for the pending state to show. */
const SIMULATED_LATENCY_MS = 400;

/** The directory's order: by the key, an unknown value last in both directions, then by id. */
function compare(sort: AdminAccountSort) {
  const sign = sort.direction === "ascending" ? 1 : -1;
  return (a: AdminAccountRow, b: AdminAccountRow) => {
    const left = sort.key === "email" ? a.email : a.registeredAt;
    const right = sort.key === "email" ? b.email : b.registeredAt;
    if (left === null || right === null) {
      if (left !== right) return left === null ? 1 : -1;
      return a.id.localeCompare(b.id);
    }
    const order = sort.key === "email" ? left.localeCompare(right, "sv") : left.localeCompare(right);
    return order === 0 ? a.id.localeCompare(b.id) : sign * order;
  };
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
      // A pending deletion's counts are unknown, as the directory reports them.
      return {
        ...account,
        status: "pendingDeletion",
        deletionEarliest,
        applicationCount: null,
        savedSearchCount: null,
        resumeCount: null,
      };
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
    return needle === "" ? rows : rows.filter((row) => row.email?.toLowerCase().includes(needle) ?? false);
  }, [rows, query]);

  const counts = useMemo(
    () =>
      Object.fromEntries(
        FILTERS.map((value) => [
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
  const open = loaded ? (rows.find((row) => row.id === openId) ?? null) : null;

  const region: AdminRegion<ReadonlyArray<AdminAccountRow>> = loaded ? listRegion(pageRows) : { kind };

  function toggleSort(key: AdminAccountSortKey) {
    setSort((previous) =>
      previous.key === key
        ? { key, direction: previous.direction === "ascending" ? "descending" : "ascending" }
        : { key, direction: key === "email" ? "ascending" : "descending" },
    );
  }

  async function command(
    account: AdminAddressedAccount,
    next: AdminAccountCommand,
  ): Promise<AdminCommandRefusal> {
    await new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
    if (account.email === adminEmail) return t(`refusal.ownAccount.${next.kind}`);
    setRows((previous) =>
      previous.map((row) => (row.id === account.id ? applyCommand(row, next, deletionEarliest) : row)),
    );
    return null;
  }

  return (
    <>
      <AdminAccountsToolbar
        filter={filter}
        filters={FILTERS}
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
      {loaded ? <AdminAccountsSummary shown={filtered.length} total={matching.length} /> : null}
      <AdminAccountsTable
        region={region}
        sort={loaded ? sort : undefined}
        onSort={loaded ? toggleSort : undefined}
        selectedId={openId}
        onOpen={loaded ? setOpenId : undefined}
        soonId={SOON_ID}
      />
      {loaded ? <AdminAccountsPager page={current} pages={pages} onPage={setPage} /> : null}
      <AdminAccountPanel
        account={open}
        details={open === null ? { kind: "loading" } : { kind: "loaded", data: open }}
        onClose={() => setOpenId(null)}
        commands={{ live: LIVE, run: command, deletionEarliestIfScheduledNow: deletionEarliest }}
      />
    </>
  );
}
