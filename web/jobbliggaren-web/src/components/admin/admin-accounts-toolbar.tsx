"use client";

import { useTranslations } from "next-intl";
import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import type { AdminAccountStatus } from "@/lib/admin/view-models";
import { AdminSegment } from "./admin-segment";

export type AdminAccountFilter = "all" | AdminAccountStatus;

export const ADMIN_ACCOUNT_FILTERS: ReadonlyArray<AdminAccountFilter> = [
  "all",
  "active",
  "suspended",
  "unverified",
  "pendingDeletion",
];

interface AdminAccountsToolbarProps {
  readonly filter: AdminAccountFilter;
  readonly query?: string;
  /** Absent while the account list is unbuilt: the search and the filter are then disabled. */
  readonly onQueryChange?: (value: string) => void;
  readonly onFilterChange?: (value: AdminAccountFilter) => void;
  /** Shown in the filter labels only when known (ADR 0150 D2). */
  readonly counts?: Readonly<Record<AdminAccountFilter, number>>;
  /** The unbuilt region's "Kommer snart" line, read with each disabled control. */
  readonly soonId?: string;
}

/**
 * Search by address and filter by status (ADR 0150 D3). The search term lives in this component's
 * caller and never in a URL, so it reaches no log (#1974).
 */
export function AdminAccountsToolbar({
  filter,
  query = "",
  onQueryChange,
  onFilterChange,
  counts,
  soonId,
}: AdminAccountsToolbarProps) {
  const t = useTranslations("admin.users");
  const live = onQueryChange !== undefined;

  return (
    <div className="jp-admintoolbar">
      <div className="jp-adminsearch">
        <Search className="jp-adminsearch__icon" size={18} aria-hidden="true" />
        <Input
          type="search"
          aria-label={t("search.label")}
          value={live ? query : undefined}
          onChange={live ? (event) => onQueryChange(event.target.value) : undefined}
          disabled={!live}
          aria-describedby={live ? undefined : soonId}
        />
      </div>
      <AdminSegment
        label={t("filter.label")}
        value={filter}
        onChange={onFilterChange}
        describedBy={onFilterChange === undefined ? soonId : undefined}
        options={ADMIN_ACCOUNT_FILTERS.map((value) => ({
          value,
          label:
            counts === undefined
              ? t(`filter.${value}`)
              : t("filterCount", { label: t(`filter.${value}`), count: counts[value] }),
        }))}
      />
    </div>
  );
}
