import type { Metadata } from "next";
import Link from "next/link";
import { TEXT_LINK } from "@/components/auth/mail-link";
import { redirect } from "next/navigation";
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
import { getServerSession } from "@/lib/auth/session";
import { toAccountsPage } from "@/lib/dto/admin-accounts";
import { parseAccountFilters } from "@/lib/admin/account-filters";
import { AccountsDirectory } from "./accounts-directory";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.users");
  return { title: t("meta.title") };
}

/**
 * `/admin/anvandare` — every account, searchable by address (#1974, ADR 0151). The account is identified
 * by its email address (ADR 0150 D3; accounts store no name, ADR 0142 D7). The first page and the counts
 * are read here; the island reads every later page through the BFF.
 *
 * The signed-in administrator goes to the island as well (#1975): their own account is told by its id, and
 * their own step-up code goes to their own address. The layout has already read the session, and the read is
 * cached for the request.
 */
export default async function AdminUsersPage({ searchParams = Promise.resolve({}) }: {
  readonly searchParams?: Promise<Record<string, string | string[] | undefined>>;
} = {}) {
  const t = await getTranslations("admin.users");
  const filters = parseAccountFilters(await searchParams);
  if (filters === null) return <div className="flex flex-col gap-6">
    <AdminPageHeader title={t("heading")} />
    <p role="alert">{t("errors.invalidPeriod")} <Link href="/admin/anvandare" className={TEXT_LINK}>{t("clearRegistrationPeriod")}</Link></p>
  </div>;
  const [result, user] = await Promise.all([
    searchAccounts({ ...filters, sort: wireSort(FIRST_SORT), page: 1, pageSize: ACCOUNTS_PAGE_SIZE }),
    getServerSession(),
  ]);
  if (!user) redirect("/logga-in");
  const initial: AccountsListing =
    result.kind === "ok"
      ? { kind: "loaded", page: toAccountsPage(result.data) }
      : { kind: "failed", failure: failureOf(result) };

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <AccountsDirectory initial={initial} initialFilters={filters} self={{ userId: user.userId, email: user.email }} />
    </div>
  );
}
