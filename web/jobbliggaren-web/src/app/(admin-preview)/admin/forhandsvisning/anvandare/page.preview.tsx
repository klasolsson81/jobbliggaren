import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { ADMIN_PREVIEW_ROUTE } from "@/lib/admin-preview/gate.cjs";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import {
  PREVIEW_ACCOUNTS,
  PREVIEW_DELETION_EARLIEST,
  PREVIEW_EMAIL_CHANGE_STARTED,
  PREVIEW_EMAIL_CHANGES,
  PREVIEW_SELF,
} from "@/lib/admin-preview/fixtures";
import { PreviewAccounts } from "../_preview/preview-accounts.preview";

export const dynamic = "force-dynamic";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin-preview.meta");
  return { title: t("users"), robots: { index: false, follow: false } };
}

export default async function AdminPreviewUsersPage() {
  requireAdminPreview();
  const t = await getTranslations("admin.users");
  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <PreviewAccounts
        accounts={PREVIEW_ACCOUNTS}
        self={PREVIEW_SELF}
        deletionEarliest={PREVIEW_DELETION_EARLIEST}
        emailChanges={PREVIEW_EMAIL_CHANGES}
        startedChange={PREVIEW_EMAIL_CHANGE_STARTED}
        returnPath={`${ADMIN_PREVIEW_ROUTE}/anvandare`}
      />
    </div>
  );
}
