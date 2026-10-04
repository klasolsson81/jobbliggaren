import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import {
  ACCOUNTS_PAGE_SIZE,
  FIRST_SORT,
  failureOf,
  wireSort,
  type AccountsListing,
} from "@/lib/admin/account-directory";
import { searchAccounts } from "@/lib/api/admin-accounts";
import { toAccountsPage } from "@/lib/dto/admin-accounts";
import { AccountsDirectory } from "./accounts-directory";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.users");
  return { title: t("meta.title") };
}

/**
 * `/admin/anvandare` — every account, searchable by address (#1974, ADR 0151). The account is identified
 * by its email address (ADR 0150 D3; accounts store no name, ADR 0142 D7). The first page and the counts
 * are read here; the island reads every later page through the BFF.
 */
export default async function AdminUsersPage() {
  const t = await getTranslations("admin.users");
  const result = await searchAccounts({ sort: wireSort(FIRST_SORT), page: 1, pageSize: ACCOUNTS_PAGE_SIZE });
  const initial: AccountsListing =
    result.kind === "ok"
      ? { kind: "loaded", page: toAccountsPage(result.data) }
      : { kind: "failed", failure: failureOf(result) };

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <AccountsDirectory initial={initial} />
    </div>
  );
}
