import type { ApiResult } from "@/lib/dto/_helpers";
import type { AccountSearchStatus, AccountSort, AccountsPage } from "@/lib/dto/admin-accounts";
import type { AdminAccountSort, AdminAccountStatus } from "./view-models";

/** The account list's page size, shared by the server's first page and the island's later ones. */
export const ACCOUNTS_PAGE_SIZE = 25;

/** The first page's order: the newest registration first. */
export const FIRST_SORT: AdminAccountSort = { key: "registeredAt", direction: "descending" };

/** Why a read failed, as the page words it. */
export type AccountsFailure =
  | { readonly reason: "rateLimited"; readonly retryAfterSeconds: number }
  | { readonly reason: "unauthorized" | "forbidden" | "error" };

/** The account list as last answered: a page, or why there is none. */
export type AccountsListing =
  | { readonly kind: "loaded"; readonly page: AccountsPage }
  | { readonly kind: "failed"; readonly failure: AccountsFailure };

export function wireSort({ key, direction }: AdminAccountSort): AccountSort {
  if (key === "email") return direction === "ascending" ? "AddressAscending" : "AddressDescending";
  return direction === "descending" ? "RegisteredNewest" : "RegisteredOldest";
}

const WIRE_STATUS: Readonly<Partial<Record<AdminAccountStatus, AccountSearchStatus>>> = {
  active: "Active",
  pendingDeletion: "PendingDeletion",
  profileMissing: "ProfileMissing",
  suspended: "Suspended",
};

/** The status a filter asks the backend for; "all" asks for none. */
export function wireStatus(filter: "all" | AdminAccountStatus): AccountSearchStatus | undefined {
  return filter === "all" ? undefined : WIRE_STATUS[filter];
}

/** A failed server read as the page words it. */
export function failureOf(result: Exclude<ApiResult<unknown>, { readonly kind: "ok" }>): AccountsFailure {
  switch (result.kind) {
    case "rateLimited":
      return { reason: "rateLimited", retryAfterSeconds: result.retryAfterSeconds };
    case "unauthorized":
    case "forbidden":
      return { reason: result.kind };
    default:
      return { reason: "error" };
  }
}
