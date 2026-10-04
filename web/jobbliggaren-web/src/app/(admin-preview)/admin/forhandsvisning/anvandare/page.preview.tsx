import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { requireAdminPreview } from "@/lib/admin-preview/runtime-gate";
import {
  PREVIEW_ACCOUNTS,
  PREVIEW_ADMIN_EMAIL,
  PREVIEW_DELETION_EARLIEST,
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
        adminEmail={PREVIEW_ADMIN_EMAIL}
        deletionEarliest={PREVIEW_DELETION_EARLIEST}
      />
    </div>
  );
}
