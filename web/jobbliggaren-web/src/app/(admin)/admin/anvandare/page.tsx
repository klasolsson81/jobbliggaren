import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminAccountsTable } from "@/components/admin/admin-accounts-table";
import { AdminAccountsToolbar } from "@/components/admin/admin-accounts-toolbar";
import { AdminPageHeader } from "@/components/admin/admin-page-header";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.users");
  return { title: t("meta.title") };
}

const SOON_ID = "admin-users-soon";

/**
 * `/admin/anvandare` — the account list (ADR 0150). The account is identified by its email
 * address (D3; accounts store no name, ADR 0142 D7). Until #1974 connects the account directory,
 * the toolbar, the sortable header and the table keep their designed structure: the controls are
 * natively disabled and described by the table's one "Kommer snart" line, and no count, row or
 * page is shown (D2).
 */
export default async function AdminUsersPage() {
  const t = await getTranslations("admin.users");

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <AdminAccountsToolbar filter="all" soonId={SOON_ID} />
      <AdminAccountsTable region={{ kind: "unavailable" }} soonId={SOON_ID} />
    </div>
  );
}
