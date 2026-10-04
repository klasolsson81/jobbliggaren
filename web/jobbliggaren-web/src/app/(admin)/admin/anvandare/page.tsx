import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { AdminSegmented } from "@/components/admin/admin-segmented";
import { AdminTableScroll } from "@/components/admin/admin-table-scroll";
import { ComingSoon } from "@/components/admin/coming-soon";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.users");
  return { title: t("meta.title") };
}

const SOON_ID = "admin-users-soon";
const CAPTION_ID = "admin-users-caption";

/**
 * `/admin/anvandare` — the account list (ADR 0150). The account is identified by its email
 * address (D3; accounts store no name, ADR 0142 D7). Until #1974 connects the account directory,
 * the toolbar, the sortable header and the table keep their designed structure: the controls are
 * natively disabled and described by the table's one "Kommer snart" line, and no count, row or
 * page is shown (D2).
 */
export default async function AdminUsersPage() {
  const t = await getTranslations("admin.users");

  // The two columns the list will sort by; the rest are plain headers.
  const sortable = (label: string) => (
    <button type="button" className="jp-adminusers__sortbtn" disabled aria-describedby={SOON_ID}>
      {label}
    </button>
  );

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />

      <div className="jp-admintoolbar">
        <div className="jp-adminsearch">
          <Search className="jp-adminsearch__icon" size={18} aria-hidden="true" />
          <Input
            type="search"
            aria-label={t("search.label")}
            disabled
            aria-describedby={SOON_ID}
          />
        </div>
        <AdminSegmented
          label={t("filter.label")}
          options={[
            { key: "all", label: t("filter.all") },
            { key: "active", label: t("filter.active") },
            { key: "suspended", label: t("filter.suspended") },
            { key: "unverified", label: t("filter.unverified") },
            { key: "pendingDeletion", label: t("filter.pendingDeletion") },
          ]}
          selected="all"
          disabled
          describedBy={SOON_ID}
        />
      </div>

      <AdminTableScroll labelledBy={CAPTION_ID}>
        <table className="jp-table jp-admintable jp-adminusers">
          <caption id={CAPTION_ID} className="sr-only">
            {t("table.caption")}
          </caption>
          <thead>
            <tr>
              <th scope="col">{sortable(t("table.account"))}</th>
              <th scope="col">{t("table.role")}</th>
              <th scope="col">{t("table.status")}</th>
              <th scope="col">{sortable(t("table.registered"))}</th>
              <th scope="col">{t("table.lastLogin")}</th>
              <th scope="col">{t("table.lastActive")}</th>
              <th scope="col">{t("table.applications")}</th>
              <th scope="col">{t("table.actions")}</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td colSpan={8} className="jp-admintable__soon">
                <ComingSoon id={SOON_ID} />
              </td>
            </tr>
          </tbody>
        </table>
      </AdminTableScroll>
    </div>
  );
}
